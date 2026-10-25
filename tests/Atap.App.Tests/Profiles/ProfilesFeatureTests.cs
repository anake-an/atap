using System.Text.Json;
using Atap.Core.Layout;
using Atap.Core.Net;
using Atap.Core.Notch;
using Atap.Core.Power;
using Atap.Features.Profiles;
using Atap.Notch;
using Atap.Settings.Cards.Behavior.AppRules;
using Atap.Settings.Cards.Profiles;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Profiles;

public sealed class ProfilesFeatureTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly Foreground _foreground = new();
    private readonly Network _network = new();
    private readonly Power _power = new();

    public void Dispose() => _services.Dispose();

    private ProfilesFeature Create() =>
        new(_services.Settings, _services.Layout, _notch, _services.Scheduler, _services.Time, _foreground, _network, _power);

    private void Rules(string json)
    {
        using var document = JsonDocument.Parse(json);
        _services.Settings.SetRules(document.RootElement.Clone());
    }

    [Fact]
    public async Task WithoutRules_NoInputIsWatched()
    {
        var feature = Create();
        await feature.StartAsync();

        Assert.Equal(0, _foreground.Leases);
        Assert.Equal(0, _network.Leases);
        Assert.False(_power.Watched);
        Assert.Equal("default", _services.Layout.ActiveProfileId);
        await feature.DisposeAsync();
    }

    [Fact]
    public async Task AppRule_SlimsAtOnce_AndSwitchesProfileAfterTheDelay()
    {
        _services.Layout.Update(d => d.Profiles.Add(new LayoutProfile { Id = "gaming", Name = "Gaming" }));
        Rules("""[{ "id": "r1", "when": [{ "kind": "foregroundApp", "apps": ["valorant.exe"] }], "then": { "profile": "gaming", "slim": true } }]""");
        var feature = Create();
        await feature.StartAsync();

        Assert.Equal(1, _foreground.Leases);
        Assert.Equal(0, _network.Leases);
        Assert.False(_power.Watched);

        _foreground.Show("VALORANT.exe");

        Assert.True(_notch.IsSet(NotchFlag.Slim));
        Assert.Equal("default", _services.Layout.ActiveProfileId);

        _services.Time.Advance(TimeSpan.FromSeconds(1.6));
        Assert.Equal("gaming", _services.Layout.ActiveProfileId);

        _foreground.Show("explorer.exe");
        Assert.False(_notch.IsSet(NotchFlag.Slim));

        await feature.DisposeAsync();
        Assert.Equal(0, _foreground.Leases);
    }

    [Fact]
    public async Task ProfileWaitsWhileTheEditorIsOpen()
    {
        _services.Layout.Update(d => d.Profiles.Add(new LayoutProfile { Id = "gaming", Name = "Gaming" }));
        Rules("""[{ "id": "r1", "when": [{ "kind": "foregroundApp", "apps": ["game.exe"] }], "then": { "profile": "gaming" } }]""");
        var feature = Create();
        await feature.StartAsync();

        _notch.SetFlag(NotchFlag.Editing, "layoutEditor", true);
        _foreground.Show("game.exe");
        _services.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("default", _services.Layout.ActiveProfileId);

        _notch.SetFlag(NotchFlag.Editing, "layoutEditor", false);
        Assert.Equal("gaming", _services.Layout.ActiveProfileId);
        await feature.DisposeAsync();
    }

    [Fact]
    public void Ids_AndExeNames_AreTidied()
    {
        Assert.Equal("work-2", ProfilesCardViewModel.NewId("Work!", ["work"]));
        Assert.Equal("profile", ProfilesCardViewModel.NewId("  ", []));
        Assert.Equal("Valorant.exe", AppRulesCardViewModel.ExeName("Valorant"));
        Assert.Equal("VALORANT.exe", AppRulesCardViewModel.ExeName(@"""C:\Riot\VALORANT.exe"""));
    }

    private sealed class Foreground : IForegroundState
    {
        private string? _name;

        public int Leases { get; private set; }

        public FullScreenKind FullScreen => FullScreenKind.None;

        public string? ProcessName => Leases > 0 ? _name : null;

        public event EventHandler? Changed;

        public void Show(string name)
        {
            _name = name;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public IDisposable WantProcessNames()
        {
            Leases++;
            return new Release(() => Leases--);
        }
    }

    private sealed class Network : INetworkState
    {
        public int Leases { get; private set; }

        public bool IsOnline => true;

        public bool IsMetered => false;

        public string? WifiSsid => null;

        public SsidAvailability Ssid => SsidAvailability.NotRequested;

        public event EventHandler? Changed;

        public IDisposable WantSsid()
        {
            Leases++;
            return new Release(() => Leases--);
        }

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Power : IPowerState
    {
        private EventHandler? _changed;

        public bool Watched => _changed is not null;

        public bool OnBattery => false;

        public int? BatteryPercent => null;

        public bool EnergySaverOn => false;

        public bool SaverActive => false;

        public event EventHandler? Changed
        {
            add => _changed += value;
            remove => _changed -= value;
        }
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
