using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Privacy;
using Atap.Core.Widgets;
using Atap.Features.KeyboardOverlay;
using Atap.Platform.Input;
using Atap.Platform.Privacy;
using Atap.Tests.Infrastructure;
using Atap.Widgets.PrivacyDot;

namespace Atap.Tests.Indicators;

public sealed class IndicatorTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("privacy-dot");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task PrivacyDot_IsLiveOnlyWhileASensorIsInUse()
    {
        var sensors = new FakeSensors();
        var descriptor = new PrivacyDotDescriptor();
        var widget = new PrivacyDotWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), () => sensors);
        await widget.StartAsync(SettingValues.Empty);
        var view = Assert.IsType<PrivacyDotViewModel>(widget.ViewModel);
        Assert.False(widget.IsLive);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        sensors.Set(new SensorUse(Sensor.Camera, "Zoom", null), new SensorUse(Sensor.Microphone, "Zoom", null));

        Assert.True(widget.IsLive);
        Assert.Equal("Camera and mic", view.CompactText);
        Assert.Equal("Camera in use by Zoom. Microphone in use by Zoom", view.AccessibleSummary);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        sensors.Set();

        Assert.False(widget.IsLive);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        await widget.StopAsync();
        Assert.True(sensors.Disposed);
    }

    [Fact]
    public async Task Keyboard_ShowsCapsLockAndLanguage()
    {
        var keys = new FakeKeyboard();
        var feature = new KeyboardOverlayFeature(() => keys, _services.Alerts.For("keyboardOverlay"), _services.Text);
        await feature.StartAsync();

        keys.Caps(true);
        var caps = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Caps Lock on", caps.Alert.Title);
        Assert.Equal(AlertPriority.Overlay, caps.Alert.Priority);

        keys.Language("Melayu (Malaysia)");
        var language = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Melayu (Malaysia)", language.Alert.Title);
        Assert.Equal("Keyboard language", language.Alert.Subtitle);

        await feature.DisposeAsync();
        Assert.True(keys.Disposed);
    }

    [Fact]
    public void LanguageTracker_IgnoresMovingToAnotherWindow()
    {
        var tracker = new LanguageSwitchTracker();

        Assert.False(tracker.Changed(1, 0x409));
        Assert.False(tracker.Changed(2, 0x43E));
        Assert.True(tracker.Changed(2, 0x409));
        Assert.False(tracker.Changed(2, 0x409));
    }

    private sealed class FakeSensors : ISensorWatcher
    {
        public IReadOnlyList<SensorUse> Current { get; private set; } = [];

        public bool Disposed { get; private set; }

        public event EventHandler? Changed;

        public void Set(params SensorUse[] uses)
        {
            Current = uses;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Refresh()
        {
        }

        public void Dispose() => Disposed = true;
    }

    private sealed class FakeKeyboard : IKeyboardWatcher
    {
        public bool Disposed { get; private set; }

        public event EventHandler<bool>? CapsLockChanged;

        public event EventHandler<string>? LanguageChanged;

        public void Caps(bool on) => CapsLockChanged?.Invoke(this, on);

        public void Language(string name) => LanguageChanged?.Invoke(this, name);

        public void Dispose() => Disposed = true;
    }
}
