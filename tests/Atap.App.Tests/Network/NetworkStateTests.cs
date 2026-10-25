using System.Collections.Concurrent;
using Atap.Core.Net;
using Atap.Platform.Network;

namespace Atap.Tests.Network;

/// <summary>The network state against a fake Windows probe. Nothing here reads the real network.</summary>
public sealed class NetworkStateTests
{
    private readonly FakeProbe _probe = new();

    [Fact]
    public void NothingStarts_UntilTheStateIsFirstUsed()
    {
        using var state = Create();
        Assert.Equal(0, _probe.Starts);
        Assert.False(state.IsStarted);

        Assert.True(state.IsOnline);

        Assert.Equal(1, _probe.Starts);
        Assert.Equal(1, _probe.Reads);
    }

    [Fact]
    public void FollowsNetworkEvents_AndRaisesChangedOnlyForARealChange()
    {
        using var state = Create();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        _probe.Reading = new NetworkReading(IsOnline: false, IsMetered: false);
        _probe.Raise();
        Assert.False(state.IsOnline);
        Assert.Equal(1, changes);

        _probe.Raise();
        Assert.Equal(1, changes);

        _probe.Reading = new NetworkReading(IsOnline: true, IsMetered: true);
        _probe.Raise();
        Assert.True(state.IsOnline);
        Assert.True(state.IsMetered);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void TheWifiName_IsReadOnlyWhileWanted_AndOnlyOnNetworkEvents()
    {
        using var state = Create();
        var changes = 0;
        state.Changed += (_, _) => changes++;

        _probe.Raise();
        Assert.Equal(0, _probe.WifiReads);
        Assert.Equal(SsidAvailability.NotRequested, state.Ssid);
        Assert.Null(state.WifiSsid);

        var want = state.WantSsid();
        Assert.Equal(1, _probe.WifiReads);
        Assert.Equal(SsidAvailability.Available, state.Ssid);
        Assert.Equal("Rumah", state.WifiSsid);
        Assert.Equal(1, changes);

        var second = state.WantSsid();
        Assert.Equal(1, _probe.WifiReads);

        _probe.Wifi = new WifiReading(WifiKind.Named, "Pejabat");
        _probe.Raise();
        Assert.Equal(2, _probe.WifiReads);
        Assert.Equal("Pejabat", state.WifiSsid);
        Assert.Equal(2, changes);

        want.Dispose();
        want.Dispose();
        Assert.Equal("Pejabat", state.WifiSsid);
        second.Dispose();
        Assert.Equal(SsidAvailability.NotRequested, state.Ssid);
        Assert.Null(state.WifiSsid);
        Assert.Equal(3, changes);

        _probe.Raise();
        Assert.Equal(2, _probe.WifiReads);
        Assert.Equal(0, _probe.LocationAsks);
    }

    [Fact]
    public void AHiddenName_AsksForLocationOnce_AndReportsItDenied()
    {
        using var state = Create();
        _probe.Wifi = new WifiReading(WifiKind.NameHidden, null);

        using var want = state.WantSsid();
        _probe.Raise();
        _probe.Raise();

        Assert.Equal(SsidAvailability.LocationDenied, state.Ssid);
        Assert.Null(state.WifiSsid);
        Assert.Equal(1, _probe.LocationAsks);
    }

    [Fact]
    public void AllowingLocation_ShowsTheName()
    {
        using var state = Create();
        _probe.Wifi = new WifiReading(WifiKind.NameHidden, null);
        _probe.AllowLocation = true;
        _probe.WifiOnceAllowed = new WifiReading(WifiKind.Named, "Rumah");

        using var want = state.WantSsid();

        Assert.Equal(SsidAvailability.Available, state.Ssid);
        Assert.Equal("Rumah", state.WifiSsid);
        Assert.Equal(1, _probe.LocationAsks);
    }

    [Theory]
    [InlineData(nameof(WifiKind.NotOnWifi), SsidAvailability.NotOnWifi)]
    [InlineData(nameof(WifiKind.Denied), SsidAvailability.LocationDenied)]
    [InlineData(nameof(WifiKind.Failed), SsidAvailability.Unavailable)]
    public void OtherAnswers_MapToTheirAvailability(string kind, SsidAvailability expected)
    {
        using var state = Create();
        _probe.Wifi = new WifiReading(Enum.Parse<WifiKind>(kind), null);

        using var want = state.WantSsid();

        Assert.Equal(expected, state.Ssid);
        Assert.Null(state.WifiSsid);
        Assert.Equal(0, _probe.LocationAsks);
    }

    [Fact]
    public void Changed_IsRaisedThroughTheUiContext()
    {
        var ui = new QueueContext();
        using var state = new NetworkState(_probe, ui, run => run());
        var changes = 0;
        state.Changed += (_, _) => changes++;

        _probe.Reading = new NetworkReading(IsOnline: false, IsMetered: false);
        _probe.Raise();
        Assert.Equal(0, changes);

        ui.RunAll();
        Assert.Equal(1, changes);
        Assert.False(state.IsOnline);
    }

    [Fact]
    public async Task TheLocationQuestion_IsAskedOnTheUiThread()
    {
        var ui = new QueueContext();
        using var state = new NetworkState(_probe, ui, run => run());
        _probe.Wifi = new WifiReading(WifiKind.NameHidden, null);

        using var want = state.WantSsid();
        Assert.Equal(0, _probe.LocationAsks);

        ui.RunAll();
        Assert.Equal(1, _probe.LocationAsks);
        await ui.WaitForPostAsync();
        ui.RunAll();

        Assert.Equal(SsidAvailability.LocationDenied, state.Ssid);
    }

    [Fact]
    public void Dispose_StopsFollowingEvents()
    {
        var state = Create();
        _ = state.IsOnline;

        state.Dispose();
        _probe.Raise();

        Assert.Equal(1, _probe.Stops);
        Assert.Equal(1, _probe.Reads);
        Assert.False(_probe.HasListeners);
    }

    [Fact]
    public void Dispose_BeforeAnyUse_TouchesNothing()
    {
        Create().Dispose();

        Assert.Equal(0, _probe.Starts);
        Assert.Equal(0, _probe.Stops);
    }

    private NetworkState Create() => new(_probe, ui: null, run => run());

    private sealed class FakeProbe : INetworkProbe
    {
        public NetworkReading Reading { get; set; } = new(IsOnline: true, IsMetered: false);

        public WifiReading Wifi { get; set; } = new(WifiKind.Named, "Rumah");

        public WifiReading? WifiOnceAllowed { get; set; }

        public bool AllowLocation { get; set; }

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public int Reads { get; private set; }

        public int WifiReads { get; private set; }

        public int LocationAsks { get; private set; }

        public bool HasListeners => StatusChanged is not null;

        public event EventHandler? StatusChanged;

        public void Raise() => StatusChanged?.Invoke(this, EventArgs.Empty);

        public void Start() => Starts++;

        public void Stop() => Stops++;

        public NetworkReading Read()
        {
            Reads++;
            return Reading;
        }

        public WifiReading ReadWifi()
        {
            WifiReads++;
            return LocationAsks > 0 && AllowLocation && WifiOnceAllowed is { } allowed ? allowed : Wifi;
        }

        public Task<bool> RequestLocationAsync()
        {
            LocationAsks++;
            return Task.FromResult(AllowLocation);
        }
    }

    /// <summary>Holds posted work until the test runs it, like a UI thread that is busy.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunAll()
        {
            while (_posted.TryDequeue(out var item))
                item.Callback(item.State);
        }

        public async Task WaitForPostAsync()
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (_posted.IsEmpty)
            {
                if (DateTime.UtcNow > until)
                    throw new TimeoutException("Nothing was posted.");
                await Task.Delay(5);
            }
        }
    }
}
