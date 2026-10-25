using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Shared;
using Atap.Core.Widgets;
using Atap.Features.MediaCommands;
using Atap.Features.VolumeOverlay;
using Atap.Platform.Audio;

[assembly: AtapFeature(typeof(VolumeOverlayDescriptor))]

namespace Atap.Features.VolumeOverlay;

/// <summary>
/// The volume level on the notch for changes made outside Atap, such as the keyboard's volume keys. Changes made
/// from the notch already show it (MediaCommands), so those are skipped here by their event context.
/// </summary>
internal sealed class VolumeOverlayDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "volumeOverlay";
    public const string KeysKey = "volumeOverlay.keys";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(KeysKey, "volumeoverlay.keys", SettingType.Bool, true)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "volumeoverlay.keysHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [KeysKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal && settings.GetBool(KeysKey);

    public IAppFeature Create(FeatureContext context) =>
        new VolumeOverlayFeature(context.Shared, context.Alerts.For(FeatureId), context.Text);
}

internal sealed class VolumeOverlayFeature(ISharedServices shared, IAlertSink alerts, ILocalizer text) : IAppFeature
{
    private Lease<AudioSource>? _audio;
    private int? _lastPercent;
    private bool? _lastMuted;

    public Task StartAsync()
    {
        _audio = shared.Acquire<AudioSource>();
        _lastPercent = _audio.Value.VolumePercent;
        _lastMuted = _audio.Value.IsMuted;
        _audio.Value.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_audio is not null)
        {
            _audio.Value.Changed -= OnChanged;
            _audio.Dispose();
            _audio = null;
        }
        return ValueTask.CompletedTask;
    }

    private void OnChanged(object? sender, bool madeByAtap)
    {
        if (_audio?.Value is not { HasDevice: true } audio)
            return;
        var percent = audio.VolumePercent;
        var muted = audio.IsMuted;
        // A default device change raises the event too. Only a real level or mute change shows.
        var changed = percent != _lastPercent || muted != _lastMuted;
        _lastPercent = percent;
        _lastMuted = muted;
        if (madeByAtap || !changed)
            return;
        alerts.Show(new Alert(MediaCommandsFeature.VolumeAlertKey, AlertPriority.Overlay, text[muted ? "mediacommands.muted" : "mediacommands.volume"])
        {
            IconKey = muted || percent == 0 ? "mute" : "volume",
            Level = muted ? 0 : percent / 100.0,
        });
    }
}
