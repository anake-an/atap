using Atap.Composition;
using Atap.Core.Alerts;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Platform.Network;
using Atap.Platform.Power;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Battery;
using Atap.Widgets.Wifi;

namespace Atap.Tests.SystemWidgets;

public sealed class BatteryWifiTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("system");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);
    private readonly FakePowerReadings _readings = new();
    private readonly FakeNetwork _network = new();
    private readonly FakeSignal _signal = new();

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Battery_PluggingIn_IsLiveForTenSeconds()
    {
        _readings.Set(onBattery: true, percent: 50);
        var (widget, battery) = await StartBatteryAsync();
        Assert.False(widget.IsLive);
        Assert.Equal("On battery", battery.StatusText);

        _readings.Set(onBattery: false, percent: 50);

        Assert.True(widget.IsLive);
        Assert.Equal("Charging", battery.CompactText);
        Assert.Equal("50%", battery.PercentText);
        _services.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.False(widget.IsLive);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Battery_Low_AlertsOncePerDischarge()
    {
        _readings.Set(onBattery: true, percent: 25);
        await StartBatteryAsync();
        Assert.Null(_services.Alerts.Current);

        _readings.Set(onBattery: true, percent: 20);
        var alert = Assert.IsType<ShownAlert>(_services.Alerts.Current);
        Assert.Equal("Battery low, 20%", alert.Alert.Title);
        Assert.Equal(AlertPriority.High, alert.Alert.Priority);
        _services.Alerts.DismissCurrent();

        _readings.Set(onBattery: true, percent: 15);
        Assert.Null(_services.Alerts.Current);

        _readings.Set(onBattery: false, percent: 15);
        _readings.Set(onBattery: true, percent: 19);
        Assert.Equal("Battery low, 19%", Assert.IsType<ShownAlert>(_services.Alerts.Current).Alert.Title);
    }

    [Fact]
    public async Task Battery_OnAPcWithout_SaysSo_AndIsNeverLive()
    {
        _readings.Set(onBattery: false, percent: null);
        var (widget, battery) = await StartBatteryAsync();

        _readings.Set(onBattery: false, percent: null);

        Assert.True(battery.HasNoBattery);
        Assert.Equal("This PC has no battery.", battery.StatusText);
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task Wifi_ReadsTheSignal_OnlyWhileOpen()
    {
        _signal.Reading = new WifiSignalReading(true, 5);
        var (_, wifi) = await StartWifiAsync(showName: false);
        Assert.Equal(0, _signal.Reads);
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);

        _visibility.Value = WidgetVisibility.Expanded;

        Assert.Equal(1, _signal.Reads);
        Assert.Equal(1, _services.Scheduler.Stats.ArmedItems);
        Assert.Equal("Connected", wifi.Title);
        Assert.Equal("Strong signal", wifi.SignalText);
        Assert.Equal(0, _network.NameLeases);

        _visibility.Value = WidgetVisibility.Hidden;
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task Wifi_ShowsTheName_OnlyWhenAskedAndAllowed()
    {
        _signal.Reading = new WifiSignalReading(true, 2);
        _network.Ssid = SsidAvailability.Available;
        _network.WifiSsid = "Rumah Kita";
        var (_, wifi) = await StartWifiAsync(showName: true);
        _visibility.Value = WidgetVisibility.Panel;

        Assert.Equal(1, _network.NameLeases);
        Assert.Equal("Rumah Kita", wifi.Title);
        Assert.Equal("Good signal", wifi.SignalText);

        _network.Ssid = SsidAvailability.LocationDenied;
        _network.WifiSsid = null;
        _network.Raise();
        Assert.Equal("Connected", wifi.Title);
        Assert.Equal("Turn on Windows Location to see the network name.", wifi.NameNote);
    }

    [Fact]
    public async Task Wifi_Offline_SaysNotConnected()
    {
        _network.IsOnline = false;
        var (_, wifi) = await StartWifiAsync(showName: false);
        _visibility.Value = WidgetVisibility.Expanded;

        Assert.Equal("Not connected", wifi.Title);
        Assert.False(wifi.HasBars);
    }

    private async Task<(BatteryWidget Widget, BatteryViewModel ViewModel)> StartBatteryAsync()
    {
        var descriptor = new BatteryDescriptor();
        var widget = Assert.IsType<BatteryWidget>(descriptor.Create(Context(descriptor)));
        await widget.StartAsync(new SettingValues(descriptor.Settings, null));
        return (widget, Assert.IsType<BatteryViewModel>(widget.ViewModel));
    }

    private async Task<(WifiWidget Widget, WifiViewModel ViewModel)> StartWifiAsync(bool showName)
    {
        var descriptor = new WifiDescriptor();
        var widget = new WifiWidget(Context(descriptor), _signal);
        var settings = new SettingValues(descriptor.Settings, new Dictionary<string, System.Text.Json.JsonElement>
        {
            [WifiDescriptor.ShowNameKey] = System.Text.Json.JsonSerializer.SerializeToElement(showName),
        });
        await widget.StartAsync(settings);
        return (widget, Assert.IsType<WifiViewModel>(widget.ViewModel));
    }

    /// <summary>The usual test context, with power and network readings this test controls.</summary>
    private WidgetContext Context(IWidgetDescriptor descriptor)
    {
        var c = _services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility);
        return new WidgetContext
        {
            InstanceId = c.InstanceId,
            Descriptor = c.Descriptor,
            Ui = c.Ui,
            Time = c.Time,
            Scheduler = c.Scheduler,
            Http = c.Http,
            Text = c.Text,
            Alerts = c.Alerts,
            Commands = c.Commands,
            Shared = c.Shared,
            App = c.App,
            Power = new PowerState(_readings, _services.Settings),
            Network = _network,
            Secrets = c.Secrets,
            Storage = c.Storage,
            Visibility = c.Visibility,
            Lifetime = c.Lifetime,
        };
    }

    private sealed class FakeSignal : IWifiSignal
    {
        public WifiSignalReading Reading { get; set; }

        public int Reads { get; private set; }

        public WifiSignalReading Read()
        {
            Reads++;
            return Reading;
        }
    }

    private sealed class FakeNetwork : INetworkState
    {
        public bool IsOnline { get; set; } = true;

        public bool IsMetered { get; set; }

        public string? WifiSsid { get; set; }

        public SsidAvailability Ssid { get; set; } = SsidAvailability.NotRequested;

        public int NameLeases { get; private set; }

        public event EventHandler? Changed;

        public IDisposable WantSsid()
        {
            NameLeases++;
            return new Lease();
        }

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
