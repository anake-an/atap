using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Features.KeyboardOverlay;
using Atap.Platform.Input;

[assembly: AtapFeature(typeof(KeyboardOverlayDescriptor))]

namespace Atap.Features.KeyboardOverlay;

/// <summary>Caps Lock and input language changes on the notch. Off: no keyboard hook is installed.</summary>
internal sealed class KeyboardOverlayDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "keyboardOverlay";
    public const string EnabledKey = "keyboardOverlay.enabled";

    public string Id => FeatureId;

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(EnabledKey, "keyboardoverlay.enabled", SettingType.Bool, true)
        {
            SectionId = SettingsSections.Behavior,
            DescriptionKey = "keyboardoverlay.enabledHint",
        },
    ];

    public IReadOnlyCollection<string> EnabledBy { get; } = [EnabledKey];

    public bool IsEnabled(SettingValues settings, AppMode mode) => mode == AppMode.Normal && settings.GetBool(EnabledKey);

    public IAppFeature Create(FeatureContext context) =>
        new KeyboardOverlayFeature(() => new HookKeyboardWatcher(context.Ui), context.Alerts.For(FeatureId), context.Text);
}

internal sealed class KeyboardOverlayFeature(Func<IKeyboardWatcher> createWatcher, IAlertSink alerts, ILocalizer text) : IAppFeature
{
    public const string AlertKey = "keyboard";

    private IKeyboardWatcher? _watcher;

    public Task StartAsync()
    {
        _watcher = createWatcher();
        _watcher.CapsLockChanged += OnCapsLock;
        _watcher.LanguageChanged += OnLanguage;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.CapsLockChanged -= OnCapsLock;
            _watcher.LanguageChanged -= OnLanguage;
            _watcher.Dispose();
            _watcher = null;
        }
        return ValueTask.CompletedTask;
    }

    // Overlay alerts share one key, so the next change replaces the last.
    private void OnCapsLock(object? sender, bool on) =>
        alerts.Show(new Alert(AlertKey, AlertPriority.Overlay, text[on ? "keyboardoverlay.capsOn" : "keyboardoverlay.capsOff"])
        {
            IconKey = "keyboard",
        });

    private void OnLanguage(object? sender, string name) =>
        alerts.Show(new Alert(AlertKey, AlertPriority.Overlay, name)
        {
            Subtitle = text["keyboardoverlay.language"],
            IconKey = "keyboard",
        });
}
