using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Notch;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.Alerts;
using Atap.Notch;
using Atap.Notch.Surfaces.Alerts;

[assembly: AtapFeature(typeof(AlertsFeatureDescriptor))]

namespace Atap.Features.Alerts;

/// <summary>Alerts on the notch, their sounds, and Windows quiet hours. Always on, safe mode too.</summary>
internal sealed class AlertsFeatureDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "alerts";
    public const string SoundKey = "alerts.sound";
    public const string SoundFileKey = "alerts.soundFile";
    public const string FollowWindowsKey = "alerts.followWindows";

    public const string SoundWindows = "windows";
    public const string SoundFile = "file";
    public const string SoundOff = "off";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(SoundKey, SoundKey, SettingType.Choice, SoundWindows, [SoundWindows, SoundFile, SoundOff])
        {
            DescriptionKey = "alerts.soundHint",
            SectionId = SettingsSections.General,
        },
        new(SoundFileKey, SoundFileKey, SettingType.File, string.Empty)
        {
            DescriptionKey = "alerts.soundFileHint",
            SectionId = SettingsSections.General,
            VisibleWhen = (SoundKey, SoundFile),
            // A path on this PC means nothing on another one.
            Scope = SettingScope.Machine,
        },
        new(FollowWindowsKey, FollowWindowsKey, SettingType.Bool, true)
        {
            DescriptionKey = "alerts.followWindowsHint",
            SectionId = SettingsSections.General,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    // The safe mode notice is an alert too.
    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) => new AlertsFeature(
        context.Alerts as AlertQueue ?? throw new InvalidOperationException("The alerts feature needs the app's alert queue."),
        context.Notch,
        context.Foreground,
        context.Settings,
        context.Text,
        context.Commands,
        context.Ui,
        new WindowsSoundOutput(),
        new WindowsQuietHours(),
        (message, ex) => Log.Warn(message, ex));
}

/// <summary>
/// Puts the current alert on the notch's Alert surface and plays alert sounds. Tells the queue when the
/// notch is hidden (a full-screen app or a hide rule) and asks Windows about quiet hours only when an
/// alert is about to show. No timer of its own: the queue's one-shot ends each alert.
/// </summary>
internal sealed class AlertsFeature : IAppFeature, INotchInputHandler
{
    public const string StopSoundCommand = "alerts.stopSound";
    public const string ClearMissedCommand = "alerts.clearMissed";

    // Owns the Alert surface content and the queue's hidden flag.
    private const string Owner = AlertsFeatureDescriptor.FeatureId;

    private readonly AlertQueue _queue;
    private readonly INotchControl _notch;
    private readonly IForegroundState _foreground;
    private readonly IAppSettingsView _settings;
    private readonly ILocalizer _text;
    private readonly ICommandRegistry _commands;
    private readonly IQuietHours _quietHours;
    private readonly AlertSoundPlayer _player;
    private readonly Dictionary<PlayingSound, IDisposable> _holds = [];
    private readonly List<IDisposable> _registrations = [];
    private AlertViewModel? _shown;
    private bool _started;
    private bool _disposed;

    /// <param name="queue">The app's alert queue.</param>
    /// <param name="notch">Where the alert shows, and whether the notch is hidden.</param>
    /// <param name="foreground">Whether a full-screen app is in front.</param>
    /// <param name="settings">The sound and quiet hours settings.</param>
    /// <param name="text">For the alert's own words.</param>
    /// <param name="commands">Scoped: Stop the sound, Clear missed alerts.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="output">Makes the sounds. Tests pass one that plays nothing.</param>
    /// <param name="quietHours">Windows quiet hours. Tests pass their own.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    internal AlertsFeature(
        AlertQueue queue,
        INotchControl notch,
        IForegroundState foreground,
        IAppSettingsView settings,
        ILocalizer text,
        ICommandRegistry commands,
        SynchronizationContext ui,
        ISoundOutput output,
        IQuietHours quietHours,
        Action<string, Exception?> log)
    {
        _queue = queue;
        _notch = notch;
        _foreground = foreground;
        _settings = settings;
        _text = text;
        _commands = commands;
        _quietHours = quietHours;
        _player = new AlertSoundPlayer(output, ui, () => _queue.IsQuiet || WindowsQuietNow(), () => _queue.IsHidden, log);
    }

    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return Task.CompletedTask;
        _started = true;

        _queue.SystemQuiet = WindowsQuietNow;
        _queue.Changed += OnQueueChanged;
        _queue.SoundDue += OnSoundDue;
        _queue.Dismissed += OnDismissed;
        _notch.Changed += OnVisibilityChanged;
        _foreground.Changed += OnVisibilityChanged;
        _player.PlayingChanged += OnPlayingChanged;
        _player.Ended += OnSoundEnded;

        _registrations.Add(_notch.AddInputHandler(this));
        _registrations.Add(_commands.Register(new AtapCommand(StopSoundCommand, "alerts.command.stopSound", _ =>
        {
            _player.Stop();
            return Task.CompletedTask;
        })
        {
            IsAvailable = () => _player.IsPlaying,
        }));
        _registrations.Add(_commands.Register(new AtapCommand(ClearMissedCommand, "alerts.command.clearMissed", _ =>
        {
            _queue.ClearMissed();
            return Task.CompletedTask;
        })
        {
            IsAvailable = () => _queue.Missed.Count > 0,
        }));

        UpdateHidden();
        OnQueueChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;

        _queue.Changed -= OnQueueChanged;
        _queue.SoundDue -= OnSoundDue;
        _queue.Dismissed -= OnDismissed;
        _notch.Changed -= OnVisibilityChanged;
        _foreground.Changed -= OnVisibilityChanged;
        _player.PlayingChanged -= OnPlayingChanged;
        _player.Ended -= OnSoundEnded;
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();

        _player.Dispose();
        foreach (var hold in _holds.Values)
            hold.Dispose();
        _holds.Clear();

        if (_started)
        {
            _queue.SystemQuiet = null;
            _queue.SetHidden(Owner, false);
        }
        if (_shown is not null)
        {
            _shown.Dispose();
            _shown = null;
            _notch.SetSurfaceContent(NotchSurface.Alert, null, Owner);
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>A click on the alert closes it. While its sound plays, only Stop ends it, so a stray click never cuts a sound.</summary>
    public bool TryHandle(NotchInput input)
    {
        if (input.Kind != NotchInputKind.Click || input.Surface != NotchSurface.Alert || _queue.Current is null)
            return false;
        if (!_player.IsPlaying)
            _queue.DismissCurrent();
        return true;
    }

    /// <summary>Windows quiet hours, unless the user turned that off. Asked only when an alert is about to show.</summary>
    private bool WindowsQuietNow() =>
        _settings.Values.GetBool(AlertsFeatureDescriptor.FollowWindowsKey) && _quietHours.IsOn();

    /// <summary>What the "default" sound key plays: the user's pick, as a sound key, or null for no sound.</summary>
    private string? UserSound()
    {
        var values = _settings.Values;
        switch (values.GetString(AlertsFeatureDescriptor.SoundKey))
        {
            case AlertsFeatureDescriptor.SoundOff:
                return null;
            case AlertsFeatureDescriptor.SoundFile:
                // No usable file picked yet: the Windows sound, rather than silence.
                var path = values.GetString(AlertsFeatureDescriptor.SoundFileKey);
                return AlertSounds.IsUsableFile(path) ? AlertSounds.ForFile(path) : AlertSounds.Windows;
            default:
                return AlertSounds.Windows;
        }
    }

    // The notch hides for a full-screen app or a hide flag, the same test NotchViewModel uses.
    private void OnVisibilityChanged(object? sender, EventArgs e) => UpdateHidden();

    private void UpdateHidden() =>
        _queue.SetHidden(Owner, _notch.IsSet(NotchFlag.Hide) || _foreground.FullScreen != FullScreenKind.None);

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        var current = _queue.Current;
        if (current is null)
        {
            if (_shown is null)
                return;
            _shown.Dispose();
            _shown = null;
            _notch.SetSurfaceContent(NotchSurface.Alert, null, Owner);
            return;
        }
        if (_shown is not null && ReferenceEquals(_shown.Shown, current))
            return;

        _shown?.Dispose();
        _shown = new AlertViewModel(current, _text, StopAndClose, _queue.DismissCurrent) { CanStop = _player.IsPlaying };
        _notch.SetSurfaceContent(NotchSurface.Alert, _shown, Owner);
    }

    private void OnSoundDue(object? sender, ShownAlert alert)
    {
        if (_disposed || AlertSounds.Resolve(SoundKeyFor(alert), UserSound()) is not { } sound)
            return;
        // The same alert sent again while its sound plays: the sound already playing carries on, once.
        if (_player.Playing.Any(p => p.IsFor(alert.Source, alert.Alert.Key)))
            return;
        // While it plays, the alert stays, so its Stop button stays too.
        if (_player.Play(sound, alert.Alert.Priority, alert) is { IsEnded: false } playing)
            _holds[playing] = _queue.Keep(alert.Source, alert.Alert.Key);
    }

    /// <summary>
    /// Alerts pushed by scripts and other apps never name a file to open: a file sound from them becomes the
    /// Windows sound. Their "default" is the user's own pick, so it stays.
    /// </summary>
    internal static string? SoundKeyFor(ShownAlert alert) =>
        AlertQueue.TypeOf(alert.Source) == AlertQueue.PushSource
        && alert.Alert.SoundKey?.StartsWith(AlertSounds.FilePrefix, StringComparison.Ordinal) == true
            ? AlertSounds.Windows
            : alert.Alert.SoundKey;

    // Its source or the user dismissed it: its sound ends with it.
    private void OnDismissed(object? sender, ShownAlert alert) => _player.StopFor(alert.Source, alert.Alert.Key);

    private void OnSoundEnded(object? sender, PlayingSound playing)
    {
        if (_holds.Remove(playing, out var hold))
            hold.Dispose();
    }

    private void OnPlayingChanged(object? sender, EventArgs e)
    {
        if (_shown is not null)
            _shown.CanStop = _player.IsPlaying;
    }

    /// <summary>
    /// The Stop button. This alert's Windows sound stops first, before the next alert can start its own.
    /// Then the alert goes, which ends its own sound, then any other sound that was playing. Closing after
    /// the sounds instead, a kept alert whose time is up would go by itself and Stop would close the next one.
    /// </summary>
    private void StopAndClose()
    {
        var playing = _player.Playing.ToList();
        _player.Stop([]);
        _queue.DismissCurrent();
        _player.Stop(playing, notification: false);
    }
}
