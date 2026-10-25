using System.Diagnostics;
using Atap.Core.Media;
using Atap.Core.Shared;
using NAudio.CoreAudioApi;

namespace Atap.Platform.Audio;

/// <summary>The volume and mute of the default speakers. The audio commands use it, and tests pass their own.</summary>
internal interface IVolumeControl
{
    /// <summary>False when no speakers or headphones are connected.</summary>
    bool HasDevice { get; }

    /// <summary>0 to 100.</summary>
    int VolumePercent { get; }

    bool IsMuted { get; }

    void SetVolumePercent(int percent);

    void SetMuted(bool muted);
}

/// <summary>
/// The default output device through Windows Core Audio: its volume and mute, and a new default device when the
/// user switches speakers. Shared by everything that needs it, made on the first lease and gone 30 seconds after
/// the last. Changes Atap makes carry <see cref="AtapContext"/>, so a listener can tell them from changes made
/// elsewhere. Everything it raises arrives on the UI thread.
/// </summary>
internal sealed class AudioSource : ISharedService<AudioSource>, IVolumeControl
{
    /// <summary>The event context on volume changes Atap makes itself.</summary>
    public static readonly Guid AtapContext = new("6f1c2a4e-3b7d-4e0a-9c55-2d8e1f4a7b90");

    private readonly SynchronizationContext _ui;
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly MMDeviceNotificationClient? _notifications;
    private MMDevice? _device;
    private bool _disposed;

    private AudioSource(SynchronizationContext ui)
    {
        _ui = ui;
        try
        {
            _enumerator = new MMDeviceEnumerator();
            // Raised on the audio worker thread, which must not be held up, so it only posts to the UI thread.
            _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
            AttachDefault();
        }
        catch (Exception ex)
        {
            // No audio service: no device, and the commands do nothing.
            Debug.WriteLine($"Audio: Core Audio failed: {ex.Message}");
            _enumerator = null;
        }
    }

    /// <summary>Raised on the UI thread when the volume, mute or default device changes. True when Atap made the change.</summary>
    public event EventHandler<bool>? Changed;

    public bool HasDevice => _device is not null;

    public int VolumePercent => Read(d => VolumeStep.ToPercent(d.AudioEndpointVolume.MasterVolumeLevelScalar), 0);

    public bool IsMuted => Read(d => d.AudioEndpointVolume.Mute, false);

    public static AudioSource Create(SharedServiceContext context) => new(context.Ui);

    public void SetVolumePercent(int percent) =>
        Write(d => d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(percent, 0, 100) / 100f);

    public void SetMuted(bool muted) => Write(d => d.AudioEndpointVolume.Mute = muted);

    public void Dispose()
    {
        _disposed = true;
        DetachDevice();
        if (_notifications is not null)
        {
            _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _notifications.Dispose();
        }
        _enumerator?.Dispose();
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Render || e.Role != Role.Multimedia)
            return;
        _ui.Post(_ =>
        {
            if (_disposed)
                return;
            AttachDefault();
            Changed?.Invoke(this, false);
        }, null);
    }

    private void AttachDefault()
    {
        DetachDevice();
        if (_enumerator is null || !_enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out var device))
            return;
        _device = device;
        device.AudioEndpointVolume.NotificationGuid = AtapContext;
        device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
    }

    private void DetachDevice()
    {
        if (_device is null)
            return;
        try
        {
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio: detach failed: {ex.Message}");
        }
        _device.Dispose();
        _device = null;
    }

    // Core Audio calls this on its own thread.
    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        var byAtap = data.EventContext == AtapContext;
        _ui.Post(_ =>
        {
            if (!_disposed)
                Changed?.Invoke(this, byAtap);
        }, null);
    }

    private T Read<T>(Func<MMDevice, T> read, T fallback)
    {
        if (_device is null)
            return fallback;
        try
        {
            return read(_device);
        }
        catch (Exception ex)
        {
            // The device can go away between the notification and the read.
            Debug.WriteLine($"Audio: read failed: {ex.Message}");
            return fallback;
        }
    }

    private void Write(Action<MMDevice> write)
    {
        if (_device is null)
            return;
        try
        {
            write(_device);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Audio: write failed: {ex.Message}");
        }
    }
}
