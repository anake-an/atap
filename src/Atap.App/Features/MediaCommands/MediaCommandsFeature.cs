using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Core.Media;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Features.Gestures;
using Atap.Features.MediaCommands;
using Atap.Platform.Audio;
using Atap.Platform.Media;

[assembly: AtapFeature(typeof(MediaCommandsDescriptor))]

namespace Atap.Features.MediaCommands;

/// <summary>
/// Music and volume as commands: play or pause, next, previous, volume up and down, and mute. The notch's wheel
/// and middle click, hotkeys, the command bar and the local API all run these. A volume change shows its level
/// on the notch. Runs in safe mode too.
/// </summary>
internal sealed class MediaCommandsDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "mediaCommands";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyCollection<string> EnabledBy { get; } = [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) => new MediaCommandsFeature(
        context.Commands, context.Alerts.For(FeatureId), context.Text, new SharedMediaAccess(context.Shared), new SharedVolumeAccess(context.Shared));
}

/// <summary>Reaches whatever plays now. Tests pass their own.</summary>
internal interface IMediaAccess
{
    Task RunAsync(Func<IMediaControls, Task> action);
}

/// <summary>Reaches the default speakers. Tests pass their own.</summary>
internal interface IVolumeAccess
{
    void Run(Action<IVolumeControl> action);
}

/// <summary>A lease for the length of one command. The source's 30 second grace keeps it warm between wheel steps.</summary>
internal sealed class SharedMediaAccess(ISharedServices shared) : IMediaAccess
{
    public async Task RunAsync(Func<IMediaControls, Task> action)
    {
        using var lease = shared.Acquire<MediaSessionSource>();
        await lease.Value.WhenReady;
        await action(lease.Value);
    }
}

internal sealed class SharedVolumeAccess(ISharedServices shared) : IVolumeAccess
{
    public void Run(Action<IVolumeControl> action)
    {
        using var lease = shared.Acquire<AudioSource>();
        action(lease.Value);
    }
}

/// <summary>Registers the media and audio commands. Holds no timer and no source while idle.</summary>
internal sealed class MediaCommandsFeature(
    ICommandRegistry commands, IAlertSink alerts, ILocalizer text, IMediaAccess media, IVolumeAccess volume) : IAppFeature
{
    public const string NextCommand = "media.next";
    public const string PreviousCommand = "media.previous";
    public const string MuteCommand = "audio.mute";
    public const string VolumeAlertKey = "volume";

    private readonly List<IDisposable> _registrations = [];

    public Task StartAsync()
    {
        Register(GestureCommands.PlayPause, "mediacommands.playPause", "mediacommands.musicKeywords", () => media.RunAsync(m => m.PlayPauseAsync()));
        Register(NextCommand, "mediacommands.next", "mediacommands.musicKeywords", () => media.RunAsync(m => m.NextAsync()));
        Register(PreviousCommand, "mediacommands.previous", "mediacommands.musicKeywords", () => media.RunAsync(m => m.PreviousAsync()));
        Register(GestureCommands.VolumeUp, "mediacommands.volumeUp", "mediacommands.volumeKeywords", () => StepVolume(1));
        Register(GestureCommands.VolumeDown, "mediacommands.volumeDown", "mediacommands.volumeKeywords", () => StepVolume(-1));
        Register(MuteCommand, "mediacommands.mute", "mediacommands.volumeKeywords", ToggleMute);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var registration in _registrations)
            registration.Dispose();
        _registrations.Clear();
        return ValueTask.CompletedTask;
    }

    /// <summary>One step louder or quieter. Louder also unmutes, as the Windows volume keys do.</summary>
    public Task StepVolume(int steps)
    {
        volume.Run(v =>
        {
            if (!v.HasDevice)
                return;
            var next = VolumeStep.Next(v.VolumePercent, steps);
            if (steps > 0 && v.IsMuted)
                v.SetMuted(false);
            v.SetVolumePercent(next);
            ShowLevel(next, v.IsMuted);
        });
        return Task.CompletedTask;
    }

    public Task ToggleMute()
    {
        volume.Run(v =>
        {
            if (!v.HasDevice)
                return;
            v.SetMuted(!v.IsMuted);
            ShowLevel(v.VolumePercent, v.IsMuted);
        });
        return Task.CompletedTask;
    }

    // An overlay alert: the next change replaces it, and it is dropped while alerts are quiet.
    private void ShowLevel(int percent, bool muted) => alerts.Show(new Alert(VolumeAlertKey, AlertPriority.Overlay, text[muted ? "mediacommands.muted" : "mediacommands.volume"])
    {
        IconKey = muted || percent == 0 ? "mute" : "volume",
        Level = muted ? 0 : percent / 100.0,
    });

    private void Register(string id, string titleKey, string keywordsKey, Func<Task> run) =>
        _registrations.Add(commands.Register(new AtapCommand(id, titleKey, _ => run()) { KeywordsKey = keywordsKey }));
}
