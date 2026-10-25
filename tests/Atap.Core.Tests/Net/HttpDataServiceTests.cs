using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Net;

/// <summary>
/// The HTTP service against a fake server and a fake clock. Nothing here touches the network, and the
/// disk cache lives in a temp folder of its own.
/// </summary>
public sealed partial class HttpDataServiceTests : IDisposable
{
    private static readonly WidgetCapabilities Capabilities = new(WidgetPermissions.Network, ["example.com"]);
    private static readonly WidgetCapabilities UserUrls = new(WidgetPermissions.Network | WidgetPermissions.UserUrls, []);
    private static readonly Uri Url = new("https://example.com/data.json?city=kuala-lumpur");
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeNetwork _network = new();
    private readonly FakeServer _server = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<HttpDataService> _services = [];

    public void Dispose()
    {
        foreach (var service in _services)
            service.Dispose();
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task FreshCacheHit_SendsNothing()
    {
        var http = Create().For(Capabilities);

        var first = await http.GetAsync(new FetchRequest(Url, MaxAge));
        _time.Advance(TimeSpan.FromMinutes(9));
        var second = await http.GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Fresh, first.Status);
        Assert.Equal(FetchStatus.Cached, second.Status);
        Assert.Equal("ok 1", Text(second));
        Assert.Equal(first.FetchedAt, second.FetchedAt);
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task RestartWithinMaxAge_UsesTheDiskCopy_WithoutARequest()
    {
        var before = Create(disk: true);
        await before.For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));
        before.Dispose();

        _time.Advance(TimeSpan.FromMinutes(5));
        var after = Create(disk: true);
        var result = await after.For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Cached, result.Status);
        Assert.Equal("ok 1", Text(result));
        Assert.Equal(1, _server.Count);
        Assert.False(after.IsCreated);
    }

    [Fact]
    public async Task CacheFiles_AreNamedByHash_AndHoldNoUrlText()
    {
        var http = Create(disk: true).For(Capabilities);

        await http.GetAsync(new FetchRequest(Url, MaxAge));

        var file = Assert.Single(Directory.GetFiles(_folder));
        Assert.Matches(CacheFileName(), Path.GetFileName(file));
        var content = Encoding.UTF8.GetString(File.ReadAllBytes(file));
        foreach (var part in new[] { "example", "data.json", "city", "kuala-lumpur" })
        {
            Assert.DoesNotContain(part, Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(part, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task RequestsWithHeadersOrFromASecret_StayInMemoryOnly()
    {
        var http = Create(disk: true).For(Capabilities);
        var withHeaders = new FetchRequest(Url, MaxAge) { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer abc" } };
        var fromSecret = new FetchRequest(new Uri("https://example.com/calendar/private-token.ics"), MaxAge) { FromSecret = true };

        await http.GetAsync(withHeaders);
        _time.Advance(TimeSpan.FromSeconds(1));
        await http.GetAsync(fromSecret);
        var again = await http.GetAsync(withHeaders);

        Assert.False(Directory.Exists(_folder) && Directory.EnumerateFileSystemEntries(_folder).Any());
        Assert.Equal(FetchStatus.Cached, again.Status);
        Assert.Equal(2, _server.Count);
        Assert.Equal("Bearer abc", _server.Requests.First().Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task NotModified_UpdatesFetchedAt_AndKeepsTheBody()
    {
        _server.Respond = (request, _) => Task.FromResult(request.Headers.IfNoneMatch.Count > 0
            ? new HttpResponseMessage(HttpStatusCode.NotModified)
            : WithETag(Ok("first body"), "\"v1\""));
        var http = Create().For(Capabilities);

        var first = await http.GetAsync(new FetchRequest(Url, MaxAge));
        _time.Advance(TimeSpan.FromMinutes(11));
        var second = await http.GetAsync(new FetchRequest(Url, MaxAge));
        var third = await http.GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Fresh, second.Status);
        Assert.Equal("first body", Text(second));
        Assert.Equal(_time.GetUtcNow(), second.FetchedAt);
        Assert.NotEqual(first.FetchedAt, second.FetchedAt);
        Assert.Equal("\"v1\"", _server.Requests.Last().Headers.IfNoneMatch.Single().ToString());
        Assert.Equal(FetchStatus.Cached, third.Status);
        Assert.Equal(2, _server.Count);
    }

    [Fact]
    public async Task IdenticalRequests_AreFetchedOnce()
    {
        var release = new TaskCompletionSource();
        _server.Respond = async (_, _) =>
        {
            await release.Task;
            return Ok("shared");
        };
        var service = Create();
        var request = new FetchRequest(Url, MaxAge);

        var a = service.For(Capabilities).GetAsync(request);
        var b = service.For(Capabilities).GetAsync(request);
        var c = service.For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));
        await _server.WaitForAsync(1);
        release.SetResult();
        var results = await Task.WhenAll(a, b, c).WaitAsync(Wait);

        Assert.All(results, r => Assert.Equal(FetchStatus.Fresh, r.Status));
        Assert.All(results, r => Assert.Equal("shared", Text(r)));
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task Failures_BackOff_OneSecondThenTwo()
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var http = Create().For(Capabilities);
        var request = new FetchRequest(Url, MaxAge);

        Assert.Equal(FetchError.HttpStatus, (await http.GetAsync(request)).Error);
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(request)).Error);
        _time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(request)).Error);
        Assert.Equal(1, _server.Count);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(FetchError.HttpStatus, (await http.GetAsync(request)).Error);
        Assert.Equal(2, _server.Count);

        _time.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(request)).Error);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(FetchError.HttpStatus, (await http.GetAsync(request)).Error);
        Assert.Equal(3, _server.Count);
    }

    [Fact]
    public async Task RetryAfter_IsHonoured()
    {
        _server.Respond = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return Task.FromResult(response);
        };
        var http = Create().For(Capabilities);
        var request = new FetchRequest(Url, MaxAge);

        var first = await http.GetAsync(request);
        _time.Advance(TimeSpan.FromSeconds(119));
        var early = await http.GetAsync(request);
        _time.Advance(TimeSpan.FromSeconds(1));
        await http.GetAsync(request);

        Assert.Equal(FetchStatus.Failed, first.Status);
        Assert.Equal(FetchError.HttpStatus, first.Error);
        Assert.Equal(FetchError.BackingOff, early.Error);
        Assert.Equal(2, _server.Count);
    }

    [Fact]
    public async Task AFailure_ServesTheStaleCopy()
    {
        var http = Create().For(Capabilities);
        var request = new FetchRequest(Url, MaxAge);
        var first = await http.GetAsync(request);

        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        _time.Advance(TimeSpan.FromMinutes(11));
        var result = await http.GetAsync(request);

        Assert.Equal(FetchStatus.Stale, result.Status);
        Assert.Equal(FetchError.HttpStatus, result.Error);
        Assert.Equal("ok 1", Text(result));
        Assert.Equal(first.FetchedAt, result.FetchedAt);
    }

    [Fact]
    public async Task RequestsToOneHost_AreSpaced()
    {
        var lyrics = new WidgetCapabilities(WidgetPermissions.Network, ["lrclib.net"]);
        var http = Create().For(lyrics);

        await http.GetAsync(new FetchRequest(new Uri("https://lrclib.net/api/get?track=a"), MaxAge));
        var second = http.GetAsync(new FetchRequest(new Uri("https://lrclib.net/api/get?track=b"), MaxAge));
        _time.Advance(TimeSpan.FromMilliseconds(1999));

        Assert.False(second.IsCompleted);
        Assert.Equal(1, _server.Count);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        var result = await second.WaitAsync(Wait);

        Assert.Equal(FetchStatus.Fresh, result.Status);
        Assert.Equal(2, _server.Count);
    }

    [Fact]
    public async Task HostsOutsideTheAllowlist_AreBlocked_AndNothingIsSent()
    {
        var service = Create();

        var otherHost = await service.For(Capabilities).GetAsync(new FetchRequest(new Uri("https://example.org/data.json"), MaxAge));
        var noNetwork = await service.For(new WidgetCapabilities(WidgetPermissions.None, ["example.com"])).GetAsync(new FetchRequest(Url, MaxAge));
        var plainHttp = await service.For(Capabilities).GetAsync(new FetchRequest(new Uri("http://example.com/data.json"), MaxAge));
        var otherScheme = await service.For(UserUrls).GetAsync(new FetchRequest(new Uri("ftp://example.com/data.json"), MaxAge));

        Assert.Equal((FetchStatus.Blocked, FetchError.HostNotAllowed), (otherHost.Status, otherHost.Error));
        Assert.Equal((FetchStatus.Blocked, FetchError.HostNotAllowed), (noNetwork.Status, noNetwork.Error));
        Assert.Equal((FetchStatus.Blocked, FetchError.InsecureHttp), (plainHttp.Status, plainHttp.Error));
        Assert.Equal((FetchStatus.Blocked, FetchError.HostNotAllowed), (otherScheme.Status, otherScheme.Error));
        Assert.Equal(0, _server.Count);
        Assert.False(service.IsCreated);
    }

    [Fact]
    public async Task ABlockedHost_IsNotServedFromAnotherWidgetsCache()
    {
        var service = Create();
        await service.For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));

        var result = await service.For(new WidgetCapabilities(WidgetPermissions.Network, ["example.org"])).GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Blocked, result.Status);
        Assert.True(result.Body.IsEmpty);
    }

    [Fact]
    public async Task PlainHttp_IsOnlyForAddressesTheUserTyped_AndNeverToAPublicAddress()
    {
        var http = Create().For(UserUrls);

        var publicAddress = await http.GetAsync(new FetchRequest(new Uri("http://8.8.8.8/data.json"), MaxAge));
        var homeServer = await http.GetAsync(new FetchRequest(new Uri("http://192.168.1.10:8123/api/states"), MaxAge));

        Assert.Equal((FetchStatus.Blocked, FetchError.InsecureHttp), (publicAddress.Status, publicAddress.Error));
        Assert.Equal(FetchStatus.Fresh, homeServer.Status);
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task ABodyOverTheCap_FailsWithTooLarge_AndIsNotRetriedByTheSameWidget()
    {
        _server.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/declared"
            ? Ok(new string('x', 2000))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnknownLengthContent(new byte[2000]) });
        var service = Create();
        var http = service.For(Capabilities);
        var declared = new FetchRequest(new Uri("https://example.com/declared"), MaxAge) { MaxBytes = 1000 };
        var streamed = new FetchRequest(new Uri("https://example.com/streamed"), MaxAge) { MaxBytes = 1000 };

        var byLength = await http.GetAsync(declared);
        _time.Advance(TimeSpan.FromSeconds(1));
        var byReading = await http.GetAsync(streamed);
        _time.Advance(TimeSpan.FromMinutes(1));
        var again = await http.GetAsync(declared);
        var otherWidget = await service.For(Capabilities).GetAsync(declared);

        Assert.Equal((FetchStatus.Failed, FetchError.TooLarge), (byLength.Status, byLength.Error));
        Assert.Equal((FetchStatus.Failed, FetchError.TooLarge), (byReading.Status, byReading.Error));
        Assert.Equal(FetchError.TooLarge, again.Error);
        Assert.Equal(FetchError.TooLarge, otherWidget.Error);
        Assert.Equal(3, _server.Count);
    }

    [Fact]
    public async Task ACrossOriginRedirect_IsRefused()
    {
        _server.Respond = (_, _) => Task.FromResult(Redirect(HttpStatusCode.Found, "https://tracker.example.net/collect"));
        var http = Create().For(Capabilities);

        var result = await http.GetAsync(new FetchRequest(Url, MaxAge) { Headers = new Dictionary<string, string> { ["X-Key"] = "secret" } });

        Assert.Equal((FetchStatus.Failed, FetchError.RedirectRefused), (result.Status, result.Error));
        Assert.Equal(1, _server.Count);
        Assert.All(_server.Requests, r => Assert.Equal("example.com", r.RequestUri!.Host));
    }

    [Fact]
    public async Task ARedirectToHttpOnTheSameHost_IsRefused()
    {
        _server.Respond = (_, _) => Task.FromResult(Redirect(HttpStatusCode.MovedPermanently, "http://example.com/data.json"));

        var result = await Create().For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchError.RedirectRefused, result.Error);
    }

    [Fact]
    public async Task SameOriginRedirects_AreFollowed_UpToThree()
    {
        _server.Respond = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/data.json" => Redirect(HttpStatusCode.MovedPermanently, "/moved.json"),
            "/moved.json" => Ok("moved"),
            "/loop" => Redirect(HttpStatusCode.TemporaryRedirect, "/loop"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        var http = Create().For(Capabilities);

        var moved = await http.GetAsync(new FetchRequest(Url, MaxAge));
        _time.Advance(TimeSpan.FromSeconds(1));
        var loop = await http.GetAsync(new FetchRequest(new Uri("https://example.com/loop"), MaxAge));

        Assert.Equal(FetchStatus.Fresh, moved.Status);
        Assert.Equal("moved", Text(moved));
        Assert.Equal(FetchError.RedirectRefused, loop.Error);
        Assert.Equal(2 + 4, _server.Count);
    }

    [Fact]
    public async Task Offline_ServesTheStaleCopy_WithoutARequest()
    {
        var http = Create().For(Capabilities);
        var request = new FetchRequest(Url, MaxAge);
        await http.GetAsync(request);

        _network.Online = false;
        _time.Advance(TimeSpan.FromHours(2));
        var stale = await http.GetAsync(request);
        var nothingSaved = await http.GetAsync(new FetchRequest(new Uri("https://example.com/other.json"), MaxAge));

        Assert.Equal((FetchStatus.Stale, FetchError.Offline), (stale.Status, stale.Error));
        Assert.Equal("ok 1", Text(stale));
        Assert.Equal((FetchStatus.Failed, FetchError.Offline), (nothingSaved.Status, nothingSaved.Error));
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task Offline_StillReachesAPrivateAddressByNumber()
    {
        _network.Online = false;

        var result = await Create().For(UserUrls).GetAsync(new FetchRequest(new Uri("http://192.168.1.10:8123/api/states"), MaxAge));

        Assert.Equal(FetchStatus.Fresh, result.Status);
    }

    [Fact]
    public async Task OfflineSwitch_StopsEveryRequestAtOnce()
    {
        _server.Respond = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Ok("never");
        };
        var service = Create();
        var http = service.For(Capabilities);

        var inFlight = http.GetAsync(new FetchRequest(Url, MaxAge));
        await _server.WaitForAsync(1);
        var waitingItsTurn = http.GetAsync(new FetchRequest(new Uri("https://example.com/next.json"), MaxAge));
        service.OfflineMode = true;
        var results = await Task.WhenAll(inFlight, waitingItsTurn).WaitAsync(Wait);
        var afterwards = await http.GetAsync(new FetchRequest(new Uri("https://example.com/later.json"), MaxAge));

        Assert.All(results, r => Assert.Equal((FetchStatus.Blocked, FetchError.OfflineMode), (r.Status, r.Error)));
        Assert.Equal((FetchStatus.Blocked, FetchError.OfflineMode), (afterwards.Status, afterwards.Error));
        Assert.Equal(1, _server.Count);
    }

    [Fact]
    public async Task OfflineSwitch_BlocksBeforeAnythingIsSent_AndStillServesSavedCopies()
    {
        var service = Create();
        var http = service.For(Capabilities);
        service.OfflineMode = true;

        var blocked = await http.GetAsync(new FetchRequest(Url, MaxAge));
        Assert.Equal((FetchStatus.Blocked, FetchError.OfflineMode), (blocked.Status, blocked.Error));
        Assert.Equal(0, _server.Count);
        Assert.False(service.IsCreated);

        service.OfflineMode = false;
        Assert.Equal(FetchStatus.Fresh, (await http.GetAsync(new FetchRequest(Url, MaxAge))).Status);
        service.OfflineMode = true;
        var cached = await http.GetAsync(new FetchRequest(Url, MaxAge));
        _time.Advance(TimeSpan.FromHours(1));
        var stale = await http.GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Cached, cached.Status);
        Assert.Equal((FetchStatus.Stale, FetchError.OfflineMode), (stale.Status, stale.Error));
        Assert.Equal("ok 1", Text(stale));
        Assert.Equal(1, _server.Count);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task MaxAge_IsFourTimesLonger_OnAMeteredNetworkOrUnderSaver(bool metered, bool saver, bool cached)
    {
        var service = Create();
        var http = service.For(Capabilities);
        await http.GetAsync(new FetchRequest(Url, MaxAge));

        _network.Metered = metered;
        service.SaverActive = saver;
        _time.Advance(MaxAge * 3);
        var result = await http.GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(cached ? FetchStatus.Cached : FetchStatus.Fresh, result.Status);
    }

    [Fact]
    public async Task ANetworkChange_EndsNetworkBackoff_ButNotAServersRetryAfter()
    {
        _server.Respond = (request, _) => request.RequestUri!.Host == "example.com"
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(30)) } })
            : throw new HttpRequestException("No route to host.");
        var http = Create().For(new WidgetCapabilities(WidgetPermissions.Network, ["example.com", "example.org"]));
        var busy = new FetchRequest(Url, MaxAge);
        var broken = new FetchRequest(new Uri("https://example.org/data.json"), MaxAge);

        Assert.Equal(FetchError.HttpStatus, (await http.GetAsync(busy)).Error);
        Assert.Equal(FetchError.Network, (await http.GetAsync(broken)).Error);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FetchError.Network, (await http.GetAsync(broken)).Error);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(broken)).Error);

        _network.Online = false;
        _network.RaiseChanged();
        _network.Online = true;
        _network.RaiseChanged();

        Assert.Equal(FetchError.Network, (await http.GetAsync(broken)).Error);
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(busy)).Error);
        Assert.Equal(4, _server.Count);
    }

    [Fact]
    public async Task ClientErrors_AreNotRetried_UntilTheWidgetRestarts()
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = Create();
        var request = new FetchRequest(Url, MaxAge);

        var first = await service.For(Capabilities).GetAsync(request);
        _time.Advance(TimeSpan.FromSeconds(1));
        var scope = service.For(Capabilities);
        await scope.GetAsync(request);
        _time.Advance(TimeSpan.FromHours(3));
        var again = await scope.GetAsync(request);

        Assert.Equal((FetchStatus.Failed, FetchError.HttpStatus), (first.Status, first.Error));
        Assert.Equal((FetchStatus.Failed, FetchError.HttpStatus), (again.Status, again.Error));
        Assert.Equal(2, _server.Count);
    }

    [Fact]
    public async Task ATimeout_FailsWithTimeout_AndBacksOff()
    {
        _server.Respond = async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Ok("never");
        };
        var http = Create().For(Capabilities);

        var pending = http.GetAsync(new FetchRequest(Url, MaxAge));
        await _server.WaitForAsync(1);
        _time.Advance(TimeSpan.FromSeconds(15));
        var result = await pending.WaitAsync(Wait);

        Assert.Equal((FetchStatus.Failed, FetchError.Timeout), (result.Status, result.Error));
        Assert.Equal(FetchError.BackingOff, (await http.GetAsync(new FetchRequest(Url, MaxAge))).Error);
    }

    [Fact]
    public async Task ConnectionThatBreaksMidBody_FailsWithNetwork()
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent() });

        var result = await Create().For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));

        Assert.Equal(FetchStatus.Failed, result.Status);
        Assert.Equal(FetchError.Network, result.Error);
    }

    [Fact]
    public async Task Attempts_NameTheSite_WithoutThePathOrQuery()
    {
        _server.Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
        var service = Create();
        var attempts = new List<FetchAttempt>();
        var two = new TaskCompletionSource();
        service.Attempted += (_, a) =>
        {
            lock (attempts)
            {
                attempts.Add(a);
                if (attempts.Count == 2)
                    two.TrySetResult();
            }
        };

        await service.For(Capabilities).GetAsync(new FetchRequest(Url, MaxAge));
        await service.For(UserUrls).GetAsync(new FetchRequest(new Uri("https://my-home.example.net/api?token=abc123"), MaxAge));
        await two.Task.WaitAsync(Wait);

        Assert.Contains(new FetchAttempt("example.com", 502, FetchStatus.Failed, FetchError.HttpStatus), attempts);
        Assert.Single(attempts, a => Regex.IsMatch(a.Site, "^site-[0-9a-f]{8}$"));
        foreach (var text in attempts.Select(a => a.ToString()))
        {
            foreach (var part in new[] { "data.json", "city", "kuala", "my-home", "token", "abc123", "/api" })
                Assert.DoesNotContain(part, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ClearCache_FreesTheSavedCopies_AndTheNextAskFetches()
    {
        var service = Create(disk: true);
        var http = service.For(Capabilities);
        await http.GetAsync(new FetchRequest(Url, MaxAge));

        var cleared = service.ClearCache();
        _time.Advance(TimeSpan.FromSeconds(1));
        var next = await http.GetAsync(new FetchRequest(Url, MaxAge));

        Assert.True(cleared.FreedBytes > 0);
        Assert.Equal(0, cleared.FilesLeft);
        Assert.Equal(FetchStatus.Fresh, next.Status);
        Assert.Equal(2, _server.Count);
    }

    [Fact]
    public async Task CallerCancellation_Throws_ButDoesNotStopTheSharedFetch()
    {
        var release = new TaskCompletionSource();
        _server.Respond = async (_, _) =>
        {
            await release.Task;
            return Ok("shared");
        };
        var http = Create().For(Capabilities);
        using var cancel = new CancellationTokenSource();

        var leaving = http.GetAsync(new FetchRequest(Url, MaxAge), cancel.Token);
        var staying = http.GetAsync(new FetchRequest(Url, MaxAge));
        await _server.WaitForAsync(1);
        await cancel.CancelAsync();
        release.SetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leaving.WaitAsync(Wait));
        Assert.Equal(FetchStatus.Fresh, (await staying.WaitAsync(Wait)).Status);
    }

    private static string Text(FetchResult result) => Encoding.UTF8.GetString(result.Body.Span);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static HttpResponseMessage WithETag(HttpResponseMessage response, string etag)
    {
        response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location) =>
        new(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    [GeneratedRegex("^[0-9a-f]{64}\\.cache$")]
    private static partial Regex CacheFileName();

    private HttpDataService Create(bool disk = false)
    {
        var service = new HttpDataService("test", _network, () => new ServerHandler(_server), _time, () => 0.5);
        if (disk)
            service.UseDiskCache(_folder);
        _services.Add(service);
        return service;
    }

    internal sealed class FakeNetwork : INetworkState
    {
        public bool Online { get; set; } = true;

        public bool Metered { get; set; }

        public bool IsOnline => Online;

        public bool IsMetered => Metered;

        public string? WifiSsid => null;

        public SsidAvailability Ssid => SsidAvailability.NotRequested;

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public IDisposable WantSsid() => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>Answers "ok 1", "ok 2" and so on unless told otherwise, and keeps every request it got.</summary>
    internal sealed class FakeServer
    {
        private readonly List<HttpRequestMessage> _requests = [];

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Respond { get; set; }

        public int Count
        {
            get
            {
                lock (_requests)
                    return _requests.Count;
            }
        }

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get
            {
                lock (_requests)
                    return _requests.ToList();
            }
        }

        public async Task WaitForAsync(int count)
        {
            var until = DateTime.UtcNow + Wait;
            while (Count < count)
            {
                if (DateTime.UtcNow > until)
                    throw new TimeoutException($"The server got {Count} requests, not {count}.");
                await Task.Delay(5);
            }
        }

        public Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int number;
            lock (_requests)
            {
                _requests.Add(request);
                number = _requests.Count;
            }
            return Respond?.Invoke(request, cancellationToken) ?? Task.FromResult(Ok($"ok {number}"));
        }
    }

    private sealed class ServerHandler(FakeServer server) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            server.HandleAsync(request, cancellationToken);
    }

    /// <summary>A body sent without a Content-Length.</summary>
    private sealed class UnknownLengthContent(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(body).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>Headers arrive, then the connection resets while the body is read.</summary>
    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded, "The connection was reset.");

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new BrokenStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new HttpIOException(HttpRequestError.ResponseEnded, "The connection was reset.");
    }
}
