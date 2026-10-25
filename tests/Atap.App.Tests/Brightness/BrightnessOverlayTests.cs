using Atap.Core.Alerts;
using Atap.Features.BrightnessOverlay;
using Atap.Platform.Brightness;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Brightness;

public sealed class BrightnessOverlayTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public async Task AChange_ShowsTheLevel_AndStoppingStopsTheWatcher()
    {
        var watcher = new FakeWatcher(supported: true);
        var feature = new BrightnessOverlayFeature(() => watcher, _services.Alerts.For("brightnessOverlay"), _services.Text);
        await feature.StartAsync();

        watcher.Raise(40);

        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Brightness", alert.Alert.Title);
        Assert.Equal(0.4, alert.Alert.Level);
        Assert.Equal(AlertPriority.Overlay, alert.Alert.Priority);

        await feature.DisposeAsync();
        Assert.True(watcher.Disposed);
    }

    [Fact]
    public async Task WithoutSupport_NothingHappens()
    {
        var watcher = new FakeWatcher(supported: false);
        var feature = new BrightnessOverlayFeature(() => watcher, _services.Alerts.For("brightnessOverlay"), _services.Text);
        await feature.StartAsync();

        watcher.Raise(40);

        Assert.False(feature.IsSupported);
        Assert.Null(_services.Alerts.Current);
        await feature.DisposeAsync();
    }

    private sealed class FakeWatcher(bool supported) : IBrightnessWatcher
    {
        public bool IsSupported => supported;

        public bool Disposed { get; private set; }

        public event EventHandler<int>? Changed;

        public void Raise(int level) => Changed?.Invoke(this, level);

        public void Dispose() => Disposed = true;
    }
}
