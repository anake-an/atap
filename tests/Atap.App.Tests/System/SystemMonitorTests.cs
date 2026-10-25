using Atap.Composition;
using Atap.Core.Metrics;
using Atap.Core.Widgets;
using Atap.Platform.Metrics;
using Atap.Tests.Infrastructure;
using Atap.Widgets.SystemMonitor;

namespace Atap.Tests.SystemWidgets;

public sealed class SystemMonitorTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("system-monitor");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakeMetrics _metrics = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task SamplesOnlyWhileOpen_Every2Seconds()
    {
        var view = await StartAsync();
        Assert.Equal(0, _metrics.Reads);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Expanded;
        Assert.Equal(1, _metrics.Reads);
        Assert.Equal("...", view.Rows[0].Value);

        _services.Time.Advance(TimeSpan.FromSeconds(1));
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, _metrics.Reads);
        Assert.Equal("75%", view.Rows[0].Value);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task ShowsMemoryAndDisk_AndHidesWhatWindowsDidNotGive()
    {
        var view = await StartAsync();
        _visibility.Value = WidgetVisibility.Panel;

        Assert.Equal(3, view.Rows.Count);
        Assert.Equal("25%", view.Rows[1].Value);
        Assert.Equal("Drive C:", view.Rows[2].Name);

        _metrics.NoDisk = true;
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, view.Rows.Count);
    }

    private async Task<SystemMonitorViewModel> StartAsync()
    {
        var descriptor = new SystemMonitorDescriptor();
        var widget = new SystemMonitorWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), _metrics);
        await widget.StartAsync(SettingValues.Empty);
        return Assert.IsType<SystemMonitorViewModel>(widget.ViewModel);
    }

    private sealed class FakeMetrics : ISystemMetrics
    {
        public int Reads { get; private set; }

        public bool NoDisk { get; set; }

        // Each read adds 400 total and 100 idle: 75% busy.
        public SystemReading Read()
        {
            Reads++;
            var n = Reads;
            return new SystemReading(
                new CpuTimes(n * 100, n * 300, n * 100),
                new MemoryReading(16UL << 30, 12UL << 30),
                NoDisk ? null : new DiskReading("C:", 500UL << 30, 100UL << 30));
        }
    }
}
