using System.Net;
using System.Net.Http;
using Atap.Composition;
using Atap.Core.Net;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Features.NetworkPrivacy;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Network;

public sealed class NetworkPrivacyFeatureTests : IDisposable
{
    private static readonly WidgetCapabilities Capabilities = new(WidgetPermissions.Network | WidgetPermissions.UserUrls, ["example.com"]);

    private readonly TestServices _services = new();
    private readonly FakeNetwork _network = new();
    private readonly List<string> _log = [];
    private readonly HttpDataService _http;
    private int _sent;

    public NetworkPrivacyFeatureTests()
    {
        _services.Settings.Load();
        _services.Settings.Register(new NetworkPrivacyDescriptor().Settings);
        _http = new HttpDataService("test", _network, () => new Server(() => Interlocked.Increment(ref _sent)), _services.Time);
    }

    private string CacheFolder => Path.Combine(_services.Paths.Cache, NetworkPrivacyDescriptor.HttpCacheFolder);

    public void Dispose()
    {
        _http.Dispose();
        _services.Dispose();
    }

    [Fact]
    public void TheDescriptor_IsAlwaysOn_AndPutsItsSettingsOnThePrivacyPage()
    {
        var descriptor = new NetworkPrivacyDescriptor();

        Assert.True(descriptor.IsEnabled(SettingValues.Empty, AppMode.Normal));
        Assert.True(descriptor.IsEnabled(SettingValues.Empty, AppMode.Safe));
        Assert.All(descriptor.Settings, s => Assert.Equal(SettingsSections.Privacy, s.SectionId));
        Assert.All(descriptor.Settings, s => Assert.False((bool)s.DefaultValue!));
        var wifi = descriptor.Settings.Single(s => s.Key == NetworkPrivacyDescriptor.WifiNameKey);
        Assert.True(wifi.Advanced);
        Assert.Equal(SettingScope.Machine, wifi.Scope);
        Assert.All(descriptor.Settings, s =>
        {
            Assert.NotEqual(s.Label, _services.Text[s.Label]);
            Assert.NotEqual(s.DescriptionKey, _services.Text[s.DescriptionKey!]);
        });
    }

    [Fact]
    public async Task Start_PutsTheDiskCacheInTheAtapCacheFolder()
    {
        await using var feature = Create();

        await feature.StartAsync();

        Assert.Equal(CacheFolder, _http.DiskCacheFolder);
        Assert.StartsWith(_services.Paths.Local, _http.DiskCacheFolder!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheOfflineSwitch_ReachesTheHttpService_AtStartAndOnChange()
    {
        _services.Settings.Set(NetworkPrivacyDescriptor.OfflineKey, true);
        await using var feature = Create();

        await feature.StartAsync();
        Assert.True(_http.OfflineMode);
        var blocked = await _http.For(Capabilities).GetAsync(new FetchRequest(new Uri("https://example.com/a.json"), TimeSpan.FromMinutes(5)));

        _services.Settings.Set(NetworkPrivacyDescriptor.OfflineKey, false);
        Assert.False(_http.OfflineMode);
        var sent = await _http.For(Capabilities).GetAsync(new FetchRequest(new Uri("https://example.com/a.json"), TimeSpan.FromMinutes(5)));

        Assert.Equal((FetchStatus.Blocked, FetchError.OfflineMode), (blocked.Status, blocked.Error));
        Assert.Equal(FetchStatus.Fresh, sent.Status);
        Assert.Equal(1, _sent);
    }

    [Fact]
    public async Task TheWifiNameOption_HoldsALease_OnlyWhileItIsOn()
    {
        var feature = Create();
        await feature.StartAsync();
        Assert.Equal(0, _network.Leases);
        Assert.False(feature.WantsWifiName);

        _services.Settings.Set(NetworkPrivacyDescriptor.WifiNameKey, true);
        Assert.Equal(1, _network.Leases);
        _services.Settings.Set(NetworkPrivacyDescriptor.OfflineKey, true);
        Assert.Equal(1, _network.Leases);

        _services.Settings.Set(NetworkPrivacyDescriptor.WifiNameKey, false);
        Assert.Equal(0, _network.Leases);

        _services.Settings.Set(NetworkPrivacyDescriptor.WifiNameKey, true);
        await feature.DisposeAsync();
        Assert.Equal(0, _network.Leases);
    }

    [Fact]
    public async Task FailedRequests_AreLogged_BySiteAndStatusOnly()
    {
        await using var feature = Create();
        await feature.StartAsync();

        await _http.For(Capabilities).GetAsync(new FetchRequest(new Uri("https://example.com/fail?token=abc123"), TimeSpan.FromMinutes(5)));
        await _http.For(Capabilities).GetAsync(new FetchRequest(new Uri("https://my-nas.example.net/fail/private?id=9"), TimeSpan.FromMinutes(5)));
        var until = DateTime.UtcNow.AddSeconds(10);
        while (Count() < 2 && DateTime.UtcNow < until)
            await Task.Delay(5);

        var lines = Lines();
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("example.com", StringComparison.Ordinal) && l.Contains("HTTP 500", StringComparison.Ordinal));
        Assert.All(lines, l =>
        {
            foreach (var part in new[] { "/fail", "token", "abc123", "my-nas", "private", "id=9" })
                Assert.DoesNotContain(part, l, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(FetchStatus.Fresh, FetchError.None, false)]
    [InlineData(FetchStatus.Blocked, FetchError.OfflineMode, false)]
    [InlineData(FetchStatus.Stale, FetchError.Offline, false)]
    [InlineData(FetchStatus.Failed, FetchError.Timeout, true)]
    [InlineData(FetchStatus.Blocked, FetchError.InsecureHttp, true)]
    [InlineData(FetchStatus.Stale, FetchError.HttpStatus, true)]
    public void OnlyRealFailures_AreLogged(FetchStatus status, FetchError error, bool logged)
    {
        var line = NetworkPrivacyFeature.Describe(new FetchAttempt("example.com", null, status, error));

        Assert.Equal(logged, line is not null);
    }

    private NetworkPrivacyFeature Create() =>
        new(_http, _services.Settings, _network, CacheFolder, (message, _) =>
        {
            lock (_log)
                _log.Add(message);
        });

    private int Count()
    {
        lock (_log)
            return _log.Count;
    }

    private List<string> Lines()
    {
        lock (_log)
            return _log.ToList();
    }

    private sealed class FakeNetwork : INetworkState
    {
        public int Leases { get; private set; }

        public bool IsOnline => true;

        public bool IsMetered => false;

        public string? WifiSsid => null;

        public SsidAvailability Ssid => Leases > 0 ? SsidAvailability.Unavailable : SsidAvailability.NotRequested;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IDisposable WantSsid()
        {
            Leases++;
            return new Lease(this);
        }

        private sealed class Lease(FakeNetwork owner) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                owner.Leases--;
            }
        }
    }

    /// <summary>Answers 500 for paths with "fail" and "{}" for the rest.</summary>
    private sealed class Server(Action sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent();
            return Task.FromResult(request.RequestUri!.AbsolutePath.Contains("fail", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
