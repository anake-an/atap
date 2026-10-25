using System.Buffers;
using System.Text;
using System.Text.Json;
using Atap.Composition;
using Atap.Core.Custom;
using Atap.Core.Power;
using Atap.Core.Widgets;
using Atap.Platform.Mqtt;
using Atap.Tests.Infrastructure;
using Atap.Widgets.HomeAssistant;

namespace Atap.Tests.Custom;

public sealed class HomeAssistantTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("home-assistant");
    private readonly HomeAssistantDescriptor _descriptor = new();
    private readonly FakeLinks _links = new();
    private readonly FakePower _power = new();
    private readonly MqttBrokers _brokers;

    public HomeAssistantTests() => _brokers = new MqttBrokers(new InlineUi(), _services.Scheduler, _services.Time, _links.Create);

    public void Dispose()
    {
        _brokers.Dispose();
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task TwoInstancesOnOneBroker_ShareOneConnection()
    {
        var kitchen = await StartAsync("home-assistant#kitchen", Settings(("topic", "home/kitchen/temperature"), ("user", "atap"), ("password", "hunter2")));
        var garage = await StartAsync("home-assistant#garage", Settings(("topic", "home/garage/door"), ("user", "atap"), ("password", "hunter2")));

        var link = Assert.Single(_links.Made);
        Assert.Equal(1, _brokers.ConnectionCount);
        Assert.Equal(new[] { "home/kitchen/temperature", "home/garage/door" }, link.Subscribed);
        Assert.Equal(new MqttBrokerKey("nas.local", 1883, false, "atap"), link.Broker);
        Assert.Equal("hunter2", link.Password);
        Assert.Equal("nas.local:1883", link.Broker!.ToString());

        link.Publish("home/kitchen/temperature", "21");
        Assert.Equal("21", Model(kitchen).CompactText);
        Assert.False(Model(garage).HasValue);

        await kitchen.StopAsync();
        Assert.Equal(new[] { "home/kitchen/temperature" }, link.Unsubscribed);
        Assert.False(link.Disposed);

        await garage.StopAsync();
        Assert.True(link.SaidGoodbye);
        Assert.True(link.Disposed);
        Assert.Equal(0, _brokers.ConnectionCount);
    }

    [Fact]
    public async Task AnotherPortOrUser_GetsItsOwnConnection()
    {
        await StartAsync("home-assistant#a", Settings(("topic", "a")));
        await StartAsync("home-assistant#b", Settings(("topic", "b"), ("port", 1884)));
        await StartAsync("home-assistant#c", Settings(("topic", "c"), ("user", "guest")));

        Assert.Equal(3, _links.Made.Count);
        Assert.Equal(3, _brokers.ConnectionCount);
    }

    [Fact]
    public void Backoff_StartsAtFiveSeconds_AndDoublesUpToFiveMinutes()
    {
        var waits = Enumerable.Range(1, 9).Select(n => MqttConnection.RetryDelay(n).TotalSeconds);
        Assert.Equal(new[] { 5d, 10, 20, 40, 80, 160, 300, 300, 300 }, waits);
    }

    [Fact]
    public async Task BrokerDown_RetriesOnTheBackoff_AndAGoodConnectStartsItAgain()
    {
        _links.Fail = true;
        var widget = await StartAsync("home-assistant", Settings(("topic", "home/door")));
        Assert.Single(_links.Made);
        Assert.Equal(HomeAssistantState.Retrying, Model(widget).State);

        foreach (var (wait, made) in new[] { (5, 2), (10, 3), (20, 4) })
        {
            Advance(wait - 0.5);
            Assert.Equal(made - 1, _links.Made.Count);
            Advance(0.5);
            Assert.Equal(made, _links.Made.Count);
        }

        _links.Fail = false;
        Advance(40);
        Assert.Equal(5, _links.Made.Count);
        Assert.Equal(HomeAssistantState.Waiting, Model(widget).State);

        // A drop after a good connect waits 5 seconds again, and the last value stays, marked out of date.
        _links.Made[^1].Publish("home/door", "open");
        _links.Made[^1].Drop();
        Assert.True(Model(widget).Value!.Stale);
        Advance(5);
        Assert.Equal(6, _links.Made.Count);
        Assert.Equal(new[] { "home/door" }, _links.Made[^1].Subscribed);

        await widget.StopAsync();
        Assert.Equal(0, _services.Scheduler.Stats.ArmedItems);
    }

    [Fact]
    public async Task PayloadOver64KB_IsIgnored()
    {
        var widget = await StartAsync("home-assistant", Settings(("topic", "home/power")));
        var link = Assert.Single(_links.Made);

        link.Publish("home/power", "42");
        link.Publish("home/power", new string('7', MqttMapping.MaxPayloadBytes + 1));
        Assert.Equal("42", Model(widget).CompactText);

        link.Publish("home/power", new string(' ', MqttMapping.MaxPayloadBytes - 2) + "43");
        Assert.Equal("43", Model(widget).CompactText);
    }

    [Fact]
    public async Task UnderSaverWhileHidden_Disconnects_AndReconnectsWhenShown()
    {
        _power.SaverActive = true;
        var visibility = new ObservableValue<WidgetVisibility>(WidgetVisibility.Hidden);
        var widget = await StartAsync("home-assistant", Settings(("topic", "home/door")), visibility);

        Assert.Empty(_links.Made);
        Assert.False(widget.IsListening);
        Assert.Equal(HomeAssistantState.PausedSaver, Model(widget).State);

        visibility.Value = WidgetVisibility.Compact;
        var first = Assert.Single(_links.Made);
        first.Publish("home/door", "closed");

        visibility.Value = WidgetVisibility.Idle;
        Assert.True(first.SaidGoodbye);
        Assert.True(first.Disposed);
        Assert.Equal(0, _brokers.ConnectionCount);
        Assert.True(Model(widget).Value!.Stale);

        _power.SetSaver(false);
        Assert.Equal(2, _links.Made.Count);
        Assert.True(widget.IsListening);

        // Saver while on screen keeps the connection.
        visibility.Value = WidgetVisibility.Expanded;
        _power.SetSaver(true);
        Assert.False(_links.Made[^1].Disposed);

        await widget.StopAsync();
        Assert.True(_links.Made[^1].Disposed);
    }

    [Fact]
    public async Task AboveTheAlertLevel_GoesLive()
    {
        var widget = await StartAsync("home-assistant", Settings(("topic", "home/attic"), ("unit", "C"), ("alert", true), ("alertAt", 30d)));
        var link = Assert.Single(_links.Made);

        link.Publish("home/attic", "24");
        Assert.False(widget.IsLive);
        link.Publish("home/attic", "unavailable");
        Assert.False(widget.IsLive);
        link.Publish("home/attic", "35");
        Assert.True(widget.IsLive);
        Assert.Equal("35 C", Model(widget).CompactText);
        Assert.Equal(ValueLevel.Warning, Model(widget).Level);

        await widget.StopAsync();
        Assert.False(widget.IsLive);
    }

    [Fact]
    public async Task WithoutABroker_AsksForSetup_AndConnectsNowhere()
    {
        var widget = await StartAsync("home-assistant", Settings(("broker", ""), ("topic", "home/door")));
        Assert.Equal(HomeAssistantState.NeedsSetup, Model(widget).State);

        var badTopic = await StartAsync("home-assistant#b", Settings(("topic", "home/#/door")));
        Assert.Equal(HomeAssistantState.NeedsSetup, Model(badTopic).State);

        var badField = await StartAsync("home-assistant#c", Settings(("topic", "home/door"), ("field", "$..x")));
        Assert.Equal(HomeAssistantState.BadField, Model(badField).State);

        Assert.Empty(_links.Made);
        await widget.StopAsync();
    }

    private static HomeAssistantViewModel Model(HomeAssistantWidget widget) => Assert.IsType<HomeAssistantViewModel>(widget.ViewModel);

    private void Advance(double seconds) => _services.Time.Advance(TimeSpan.FromSeconds(seconds));

    private async Task<HomeAssistantWidget> StartAsync(string id, Dictionary<string, object> values, ObservableValue<WidgetVisibility>? visibility = null)
    {
        var context = WithPower(_services.CreateContext(id, _descriptor, _lifetime, visibility ?? new ObservableValue<WidgetVisibility>(WidgetVisibility.Hidden)));
        if (values.TryGetValue("password", out var password))
            values["password"] = context.Secrets.Put((string)password);
        var raw = values.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
        var widget = new HomeAssistantWidget(context, () => (_brokers, new Release()));
        await widget.StartAsync(new SettingValues(_descriptor.Settings, raw));
        return widget;
    }

    private static Dictionary<string, object> Settings(params (string Key, object Value)[] values)
    {
        var settings = new Dictionary<string, object> { ["broker"] = "NAS.local " };
        foreach (var (key, value) in values)
            settings[key] = value;
        return settings;
    }

    private WidgetContext WithPower(WidgetContext c) => new()
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
        Power = _power,
        Network = c.Network,
        Secrets = c.Secrets,
        Storage = c.Storage,
        Visibility = c.Visibility,
        Lifetime = c.Lifetime,
    };

    private sealed class Release : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class InlineUi : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private sealed class FakePower : IPowerState
    {
        public bool OnBattery => true;

        public int? BatteryPercent => 50;

        public bool EnergySaverOn => false;

        public bool SaverActive { get; set; }

        public event EventHandler? Changed;

        public void SetSaver(bool on)
        {
            SaverActive = on;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeLinks
    {
        public List<FakeLink> Made { get; } = [];

        public bool Fail { get; set; }

        public IMqttLink Create()
        {
            var link = new FakeLink(this);
            Made.Add(link);
            return link;
        }
    }

    private sealed class FakeLink(FakeLinks owner) : IMqttLink
    {
        public event MqttMessageHandler? MessageReceived;

        public event Action? ConnectionLost;

        public MqttBrokerKey? Broker { get; private set; }

        public string? Password { get; private set; }

        public List<string> Subscribed { get; } = [];

        public List<string> Unsubscribed { get; } = [];

        public bool SaidGoodbye { get; private set; }

        public bool Disposed { get; private set; }

        public Task ConnectAsync(MqttBrokerKey broker, string? password, CancellationToken cancellationToken)
        {
            Broker = broker;
            Password = password;
            return owner.Fail ? Task.FromException(new IOException("No route to host.")) : Task.CompletedTask;
        }

        public Task SubscribeAsync(string filter, CancellationToken cancellationToken)
        {
            Subscribed.Add(filter);
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(string filter, CancellationToken cancellationToken)
        {
            Unsubscribed.Add(filter);
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            SaidGoodbye = true;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        public void Publish(string topic, string payload) =>
            MessageReceived?.Invoke(topic, new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(payload)));

        public void Drop() => ConnectionLost?.Invoke();
    }
}
