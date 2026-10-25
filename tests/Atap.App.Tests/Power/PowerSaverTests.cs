using Atap.Composition;
using Atap.Core.Diagnostics;
using Atap.Core.Power;
using Atap.Core.Widgets;
using Atap.Features.BatterySaver;
using Atap.Notch;
using Atap.Platform.Diagnostics;
using Atap.Platform.Power;
using Atap.Settings.Cards.About.ResourceMeter;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Power;

/// <summary>Windows power readings into battery saver, its effects on the notch and the process, and the resource meter.</summary>
public sealed class PowerSaverTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakePowerReadings _readings = new();
    private readonly PowerState _power;

    public PowerSaverTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(new BatterySaverDescriptor().Settings);
        _power = new PowerState(_readings, _services.Settings);
    }

    public void Dispose()
    {
        _power.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void ByDefault_SaverIsOnWhileOnBattery()
    {
        var changes = 0;
        _power.Changed += (_, _) => changes++;
        Assert.False(_power.SaverActive);

        _readings.Set(onBattery: true, percent: 80);

        Assert.True(_power.SaverActive);
        Assert.True(_power.OnBattery);
        Assert.Equal(80, _power.BatteryPercent);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheSameReadingsAgain_RaiseNothing()
    {
        _readings.Set(onBattery: true, percent: 80);
        var changes = 0;
        _power.Changed += (_, _) => changes++;

        _readings.Set(onBattery: true, percent: 80);

        Assert.Equal(0, changes);
    }

    [Fact]
    public void WhenTheBatteryIsLow_FollowsThePercentSetting()
    {
        _services.Settings.Set(BatterySaverPolicy.ModeKey, BatterySaverPolicy.ToSettingValue(SaverMode.BelowPercent));
        _services.Settings.Set(BatterySaverPolicy.PercentKey, 30);

        _readings.Set(onBattery: true, percent: 35);
        Assert.False(_power.SaverActive);

        _readings.Set(onBattery: true, percent: 25);
        Assert.True(_power.SaverActive);

        _readings.Set(onBattery: false, percent: 25);
        Assert.False(_power.SaverActive);
    }

    [Theory]
    [InlineData(SaverMode.Always, false, false, true)]
    [InlineData(SaverMode.Never, true, true, false)]
    [InlineData(SaverMode.FollowWindows, false, true, true)]
    [InlineData(SaverMode.FollowWindows, true, false, false)]
    public void EveryMode_ReachesSaverActive(SaverMode mode, bool onBattery, bool energySaver, bool active)
    {
        _services.Settings.Set(BatterySaverPolicy.ModeKey, BatterySaverPolicy.ToSettingValue(mode));

        _readings.Set(onBattery, 50, energySaver);

        Assert.Equal(active, _power.SaverActive);
    }

    [Fact]
    public void ChangingTheMode_RaisesChanged()
    {
        var changes = 0;
        _power.Changed += (_, _) => changes++;

        _services.Settings.Set(BatterySaverPolicy.ModeKey, BatterySaverPolicy.ToSettingValue(SaverMode.Always));

        Assert.True(_power.SaverActive);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Disposing_LetsGoOfWindowsEvents()
    {
        var readings = new FakePowerReadings();
        new PowerState(readings, _services.Settings).Dispose();

        Assert.True(readings.Disposed);
    }

    [Fact]
    public async Task Saver_StopsAnimations_AndUsesEfficiencyMode_UntilItEnds()
    {
        var notch = new NotchControl();
        var efficiency = new FakeEfficiency();
        var feature = new BatterySaverFeature(_power, notch, efficiency, BatterySaverDescriptor.FeatureId);
        await feature.StartAsync();
        Assert.False(notch.IsSet(NotchFlag.LessMotion));
        Assert.Empty(efficiency.Calls);

        _readings.Set(onBattery: true, percent: 60);
        Assert.True(notch.IsSet(NotchFlag.LessMotion));
        Assert.Equal([true], efficiency.Calls);

        _readings.Set(onBattery: true, percent: 59);
        Assert.Equal([true], efficiency.Calls);

        _readings.Set(onBattery: false, percent: 59);
        Assert.False(notch.IsSet(NotchFlag.LessMotion));
        Assert.Equal([true, false], efficiency.Calls);
        await feature.DisposeAsync();
    }

    [Fact]
    public async Task StoppingTheFeature_EndsEfficiencyMode()
    {
        _readings.Set(onBattery: true, percent: 60);
        var notch = new NotchControl();
        var efficiency = new FakeEfficiency();
        var feature = new BatterySaverFeature(_power, notch, efficiency, BatterySaverDescriptor.FeatureId);
        await feature.StartAsync();

        await feature.DisposeAsync();
        notch.ClearOwner(BatterySaverDescriptor.FeatureId);

        Assert.Equal([true, false], efficiency.Calls);
        Assert.False(notch.IsSet(NotchFlag.LessMotion));
    }

    [Fact]
    public void BatterySaver_RunsInSafeModeToo()
    {
        Assert.True(new BatterySaverDescriptor().IsEnabled(SettingValues.Empty, AppMode.Safe));
    }

    [Fact]
    public async Task TheNotch_MovesLess_WhileTheFlagIsOn()
    {
        var notch = new NotchControl();
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        _services.Layout.Load();
        using var model = new NotchViewModel(_services.Layout, widgets, notch, new ForegroundStub());
        Assert.False(model.LessMotion);

        notch.SetFlag(NotchFlag.LessMotion, "batterySaver", on: true);

        Assert.True(model.LessMotion);
    }

    [Fact]
    public void TheMeter_TakesNoSamples_WhileHidden()
    {
        var probe = new FakeProbe();
        using var meter = Meter(probe);

        _services.Time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(0, probe.Samples);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal(_services.Text["meter.measuring"], meter.Summary);
    }

    [Fact]
    public void TheMeter_SamplesEveryTwoSeconds_WhileShown_AndStopsWhenHidden()
    {
        var probe = new FakeProbe();
        using var meter = Meter(probe);

        meter.SetShown(true);
        Assert.Equal(1, probe.Samples);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);

        _services.Time.Advance(TimeSpan.FromSeconds(2.05));
        _services.Time.Advance(TimeSpan.FromSeconds(2.05));
        Assert.Equal(3, probe.Samples);

        meter.SetShown(false);
        _services.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, probe.Samples);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void TheMeter_SaysMemoryAndProcessor_InOneLine()
    {
        using var meter = Meter(new FakeProbe());

        meter.SetShown(true);

        Assert.Equal(_services.Text.Format("meter.summary", ("mb", 45), ("cpu", 0.2.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture))), meter.Summary);
        Assert.Equal(11, meter.Rows.Count);
    }

    [Fact]
    public void CopyDetails_PutsTheNumbersOnTheClipboard()
    {
        string? copied = null;
        using var meter = new ResourceMeterViewModel(new FakeProbe(), _services.Scheduler, _services.Text, "1.0.0", text =>
        {
            copied = text;
            return true;
        });
        Assert.False(meter.CopyCommand.CanExecute(null));

        meter.SetShown(true);
        meter.CopyCommand.Execute(null);

        Assert.StartsWith("Atap 1.0.0", copied);
        Assert.Contains("Memory (private): 45 MB", copied);
        Assert.Equal(_services.Text["meter.copied"], meter.CopyStatus);
    }

    [Fact]
    public void ARealSample_ReadsThisProcess()
    {
        var sampler = new ProcessSampler(TimeProvider.System, () => default, () => 2, () => 1);

        var first = sampler.Sample();
        var second = sampler.Sample();

        Assert.True(first.PrivateBytes > 0);
        Assert.True(first.WorkingSet > 0);
        Assert.True(first.Threads > 0);
        Assert.True(first.Assemblies > 0);
        Assert.Equal(0, first.CpuPercent);
        Assert.InRange(second.CpuPercent, 0, 100);
        Assert.Equal(2, second.RunningWidgets);
        Assert.Equal(1, second.SharedSources);
    }

    private ResourceMeterViewModel Meter(IUsageProbe probe) =>
        new(probe, _services.Scheduler, _services.Text, "1.0.0", _ => true);

    private sealed class FakeEfficiency : IProcessEfficiency
    {
        public List<bool> Calls { get; } = [];

        public bool Set(bool efficient)
        {
            Calls.Add(efficient);
            return true;
        }
    }

    private sealed class FakeProbe : IUsageProbe
    {
        public int Samples { get; private set; }

        public ResourceUsage Sample()
        {
            Samples++;
            return new ResourceUsage(45L << 20, 80L << 20, 12L << 20, 0.2, 20, 500, 3, 60, 2, 1, 140);
        }
    }
}
