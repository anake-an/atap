using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Features.BrightnessOverlay;
using Atap.Platform.Brightness;

[assembly: AtapFeature(typeof(BrightnessOverlayDescriptor))]

namespace Atap.Features.BrightnessOverlay;

/// <summary>The brightness level on the notch when it changes, on laptop and tablet screens. Off: no watcher runs.</summary>
internal sealed class BrightnessOverlayDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "brightnessOverlay";
    public const string EnabledKey = "brightnessOverlay.enabled";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "brightnessoverlay.enabled", SettingType.Bool, true)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "brightnessoverlay.enabledHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [EnabledKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal && settings.GetBool(EnabledKey);

    public IAppFeature Create(FeatureContext context) =>
        new BrightnessOverlayFeature(() => new WmiBrightnessWatcher(context.Ui), context.Alerts.For(FeatureId), context.Text);
}

internal sealed class BrightnessOverlayFeature(Func<IBrightnessWatcher> createWatcher, IAlertSink alerts, ILocalizer text) : IAppFeature
{
    public const string AlertKey = "brightness";

    private IBrightnessWatcher? _watcher;

    /// <summary>False when the screen does not report brightness. The feature then does nothing.</summary>
    public bool IsSupported => _watcher?.IsSupported == true;

    public Task StartAsync()
    {
        _watcher = createWatcher();
        if (_watcher.IsSupported)
            _watcher.Changed += OnChanged;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        return ValueTask.CompletedTask;
    }

    // An overlay alert: the next change replaces it, and it is dropped while alerts are quiet.
    private void OnChanged(object? sender, int level) =>
        alerts.Show(new Alert(AlertKey, AlertPriority.Overlay, text["brightnessoverlay.title"])
        {
            IconKey = "brightness",
            Level = level / 100.0,
        });
}
