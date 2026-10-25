using System.Net;
using System.Net.Http.Headers;
using Atap.Core.Widgets;

namespace Atap.Core.Net;

/// <summary>
/// The one HTTP client in Atap. The client itself is created on the first request, so nothing
/// network-related exists until a widget fetches. <see cref="For"/> gives each widget a copy limited
/// to the hosts in its capabilities.
/// </summary>
/// <remarks>
/// <para>Policy: https to the hosts a widget declares, or to any host for widgets with
/// <see cref="WidgetPermissions.UserUrls"/>. Plain http only for those, and only to private addresses,
/// checked after DNS (<see cref="AddressPolicy"/>). Redirects are followed by hand, at most 3, same origin only.</para>
/// <para>Cache: memory plus disk files named by hash (<see cref="HttpCache"/>). A copy younger than
/// MaxAge is used without a request, so a restart never refetches early. Older copies are revalidated
/// with ETag and Last-Modified, and served as Stale while offline or after a failure.</para>
/// <para>Politeness: identical requests in flight are fetched once, requests to one host are spaced
/// (<see cref="HostSpacing"/>), failures back off (<see cref="Backoff"/>) and Retry-After is honoured.
/// 429 and 5xx count as failures. Other 4xx answers, oversized bodies and refused redirects are not
/// retried by the same widget copy until its settings change and it restarts.</para>
/// <para>No timers: waits happen only inside a request. Core never logs: <see cref="Attempted"/> reports
/// each network attempt by site and status, without the path or query, and the app logs it.</para>
/// </remarks>
public sealed class HttpDataService : IDisposable
{
    private const string UserAgentComment = "(+https://github.com/anake-an/atap)";
    private const int MaxRedirects = 3;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    // A cached copy dated this far in the future means the clock went back. It is treated as old.
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

    private readonly string _version;
    private readonly INetworkState _network;
    private readonly Func<HttpMessageHandler>? _createHandler;
    private readonly TimeProvider _time;
    private readonly Func<double> _random;
    private readonly HttpCache _cache = new();
    private readonly HostSpacing _spacing = new();
    private readonly Dictionary<string, Task<Outcome>> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BackoffState> _backoff = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposing = new();
    private CancellationTokenSource _offline = new();
    private HttpClient? _client;
    private bool _offlineMode;
    private volatile bool _saverActive;
    private bool _watchingNetwork;
    private bool _disposed;

    /// <param name="version">The app version for the User-Agent.</param>
    /// <param name="network">Requests fail at once while offline, and network backoff ends when the PC comes back online.</param>
    /// <param name="createHandler">Replaces the real network handler, for tests. Called once, on the first request.</param>
    /// <param name="time">The clock for the cache, backoff, spacing and timeouts. The system clock when null.</param>
    /// <param name="random">A number from 0 to 1 for the backoff jitter, for tests. Random when null.</param>
    public HttpDataService(
        string version,
        INetworkState network,
        Func<HttpMessageHandler>? createHandler = null,
        TimeProvider? time = null,
        Func<double>? random = null)
    {
        _version = version;
        _network = network;
        _createHandler = createHandler;
        _time = time ?? TimeProvider.System;
        _random = random ?? Random.Shared.NextDouble;
    }

    /// <summary>
    /// Each network attempt: the site, the status and what went wrong. Never the path or query. The site
    /// is the host only for hosts a widget declares, and a short hash for addresses the user typed or
    /// secret ones. Raised on a worker thread, after the callers have their result.
    /// </summary>
    public event EventHandler<FetchAttempt>? Attempted;

    /// <summary>
    /// The Privacy offline switch. While true nothing is sent: turning it on stops every request at once,
    /// including ones waiting their turn or in flight. Cached copies are still served. Set by the network
    /// privacy feature.
    /// </summary>
    public bool OfflineMode
    {
        get
        {
            lock (_gate)
                return _offlineMode;
        }
        set
        {
            CancellationTokenSource? stop = null;
            lock (_gate)
            {
                if (_offlineMode == value)
                    return;
                _offlineMode = value;
                if (value)
                {
                    stop = _offline;
                    _offline = new CancellationTokenSource();
                }
            }
            stop?.Cancel();
        }
    }

    /// <summary>Battery saver, set by the app from <c>IPowerState.SaverActive</c>. MaxAge is four times longer while it is true.</summary>
    public bool SaverActive
    {
        get => _saverActive;
        set => _saverActive = value;
    }

    /// <summary>True once the first request has created the client.</summary>
    public bool IsCreated
    {
        get
        {
            lock (_gate)
                return _client is not null;
        }
    }

    /// <summary>The disk cache folder, or null while responses are kept in memory only.</summary>
    public string? DiskCacheFolder => _cache.Folder;

    /// <summary>Keeps responses in <paramref name="folder"/> too, so a restart can use them. Set by the network privacy feature from AtapPaths.</summary>
    public void UseDiskCache(string? folder) => _cache.Folder = folder;

    /// <summary>A copy limited to <paramref name="capabilities"/>.</summary>
    public IHttpDataService For(WidgetCapabilities capabilities) => new Scope(this, capabilities);

    /// <summary>Forgets every cached response, in memory and on disk. Widgets fetch again when they next ask.</summary>
    public ClearResult ClearCache() => _cache.Clear();

    public void Dispose()
    {
        bool unsubscribe;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            unsubscribe = _watchingNetwork;
            _watchingNetwork = false;
            _client?.Dispose();
            _client = null;
        }
        if (unsubscribe)
            _network.Changed -= OnNetworkChanged;
        _disposing.Cancel();
    }

    /// <summary>The policy check, without sending anything. Null when the request may go out.</summary>
    public static FetchError? Check(Uri url, WidgetCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!url.IsAbsoluteUri)
            return FetchError.HostNotAllowed;
        if ((capabilities.Permissions & WidgetPermissions.Network) == 0)
            return FetchError.HostNotAllowed;
        var plainHttp = url.Scheme == Uri.UriSchemeHttp;
        if (!plainHttp && url.Scheme != Uri.UriSchemeHttps)
            return FetchError.HostNotAllowed;

        var userUrls = (capabilities.Permissions & WidgetPermissions.UserUrls) != 0;
        if (!userUrls && !IsDeclared(url, capabilities))
            return FetchError.HostNotAllowed;

        if (plainHttp)
        {
            // Built-in sources are https only. Plain http is for things on the user's own network.
            if (!userUrls)
                return FetchError.InsecureHttp;
            if (IPAddress.TryParse(url.DnsSafeHost.Trim('[', ']'), out var address) && !AddressPolicy.IsPrivate(address))
                return FetchError.InsecureHttp;
        }
        return null;
    }

    /// <summary>
    /// How a request is named in <see cref="Attempted"/>: the host when the widget declares it, else a short hash.
    /// The hash is salted per run, so a host in a log someone shares cannot be guessed by hashing names.
    /// </summary>
    internal static string SiteFor(Uri url, WidgetCapabilities capabilities, bool fromSecret) =>
        !fromSecret && IsDeclared(url, capabilities)
            ? url.IdnHost.ToLowerInvariant()
            : "site-" + HttpCache.Hash(SiteSalt + url.IdnHost.ToLowerInvariant())[..8];

    private static readonly string SiteSalt = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)) + ":";

    private static bool IsDeclared(Uri url, WidgetCapabilities capabilities) =>
        capabilities.NetworkHosts.Any(h => string.Equals(h, url.IdnHost, StringComparison.OrdinalIgnoreCase));

    private async Task<FetchResult> GetAsync(FetchRequest request, Scope scope, CancellationToken cancellationToken)
    {
        if (Check(request.Url, scope.Capabilities) is { } refused)
            return FetchResult.Fail(FetchStatus.Blocked, refused);

        var key = HttpCache.KeyFor(request);
        if (scope.Refused(key) is { } earlier)
        {
            // Not retried by this copy of the widget. The last good copy is still shown.
            var entry = await _cache.GetAsync(key, HttpCache.MayUseDisk(request), cancellationToken).ConfigureAwait(false);
            return FromCacheOr(entry, earlier == FetchError.InsecureHttp ? FetchStatus.Blocked : FetchStatus.Failed, earlier);
        }

        // Identical requests share one fetch. Each caller waits with its own cancellation.
        var flightKey = $"{key}|{request.MaxAge.Ticks}|{request.MaxBytes}";
        TaskCompletionSource<Outcome>? mine = null;
        Task<Outcome> shared;
        lock (_gate)
        {
            if (_inFlight.TryGetValue(flightKey, out var running))
            {
                shared = running;
            }
            else
            {
                mine = new TaskCompletionSource<Outcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                _inFlight[flightKey] = shared = mine.Task;
            }
        }
        if (mine is not null)
            _ = RunSharedAsync(mine, flightKey, request, key, SiteFor(request.Url, scope.Capabilities, request.FromSecret));

        var outcome = await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (outcome.NotRetried)
            scope.Refuse(key, outcome.Result.Error);
        return outcome.Result;
    }

    private async Task RunSharedAsync(TaskCompletionSource<Outcome> completion, string flightKey, FetchRequest request, string key, string site)
    {
        Outcome outcome;
        int? statusCode = null;
        var attempted = false;
        try
        {
            (outcome, attempted, statusCode) = await RunAsync(request, key).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Never leave the callers waiting. Anything unexpected is a failed fetch.
            outcome = Done(FetchResult.Fail(FetchStatus.Failed, FetchError.Network));
        }
        lock (_gate)
            _inFlight.Remove(flightKey);
        completion.SetResult(outcome);

        if (attempted)
            Attempted?.Invoke(this, new FetchAttempt(site, statusCode, outcome.Result.Status, outcome.Result.Error));
    }

    /// <returns>The outcome, whether a request went out, and its last HTTP status.</returns>
    private async Task<(Outcome Outcome, bool Attempted, int? StatusCode)> RunAsync(FetchRequest request, string key)
    {
        var disk = HttpCache.MayUseDisk(request);
        var entry = await _cache.GetAsync(key, disk, _disposing.Token).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        if (entry is not null && IsFresh(entry, now, EffectiveMaxAge(request.MaxAge)))
            return (Done(new FetchResult(FetchStatus.Cached, entry.Body, entry.FetchedAt, FetchError.None)), false, null);

        bool offlineMode;
        CancellationToken offlineToken;
        lock (_gate)
        {
            offlineMode = _offlineMode;
            offlineToken = _offline.Token;
        }
        if (offlineMode)
            return (Done(FromCacheOr(entry, FetchStatus.Blocked, FetchError.OfflineMode)), false, null);

        WatchNetwork();
        if (!_network.IsOnline && !AddressPolicy.IsLocalTarget(request.Url))
            return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.Offline)), false, null);
        if (IsBackingOff(key, now))
            return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.BackingOff)), false, null);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(offlineToken, _disposing.Token);
        var wait = _spacing.Reserve(request.Url.IdnHost, now);
        if (wait > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(wait, _time, stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return (Done(_disposing.IsCancellationRequested
                    ? FromCacheOr(entry, FetchStatus.Failed, FetchError.Network)
                    : FromCacheOr(entry, FetchStatus.Blocked, FetchError.OfflineMode)), false, null);
            }
        }

        var (outcome, statusCode) = await SendAsync(request, key, entry, disk, stop.Token, offlineToken).ConfigureAwait(false);
        return (outcome, true, statusCode);
    }

    private async Task<(Outcome Outcome, int? StatusCode)> SendAsync(
        FetchRequest request, string key, CacheEntry? entry, bool disk, CancellationToken stop, CancellationToken offlineToken)
    {
        var timeoutAfter = request.Timeout > TimeSpan.Zero ? request.Timeout : DefaultTimeout;
        using var timeout = new CancellationTokenSource(timeoutAfter, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, timeout.Token);
        var url = request.Url;
        HttpResponseMessage? response = null;
        int? code = null;
        try
        {
            for (var hop = 0; ; hop++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaders(message, request, entry);
                response = await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                code = (int)response.StatusCode;
                if (code is not (301 or 302 or 303 or 307 or 308))
                    break;

                var location = response.Headers.Location;
                response.Dispose();
                response = null;
                var next = location is null ? null : location.IsAbsoluteUri ? location : new Uri(url, location);
                // Headers and conditional values never go to another origin.
                if (next is null || hop >= MaxRedirects || !SameOrigin(url, next))
                    return (NotRetried(entry, FetchStatus.Failed, FetchError.RedirectRefused), code);
                url = next;
            }

            var now = _time.GetUtcNow();
            if (code == 304 && entry is not null)
            {
                var confirmed = entry with { FetchedAt = now };
                await _cache.PutAsync(key, confirmed, disk, _disposing.Token).ConfigureAwait(false);
                Succeeded(key);
                return (Done(new FetchResult(FetchStatus.Fresh, confirmed.Body, now, FetchError.None)), code);
            }
            if (code is >= 200 and < 300)
            {
                if (response.Content.Headers.ContentLength > request.MaxBytes)
                    return (NotRetried(entry, FetchStatus.Failed, FetchError.TooLarge), code);
                var body = await ReadCappedAsync(response.Content, request.MaxBytes, linked.Token).ConfigureAwait(false);
                if (body is null)
                    return (NotRetried(entry, FetchStatus.Failed, FetchError.TooLarge), code);

                var fresh = new CacheEntry(body, now, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified?.ToString("R"));
                await _cache.PutAsync(key, fresh, disk, _disposing.Token).ConfigureAwait(false);
                Succeeded(key);
                return (Done(new FetchResult(FetchStatus.Fresh, body, now, FetchError.None)), code);
            }
            if (code is 429 or >= 500 or 304)
            {
                Failed(key, now, Backoff.RetryAfter(response.Headers.RetryAfter, now), fromServer: true);
                return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.HttpStatus)), code);
            }
            return (NotRetried(entry, FetchStatus.Failed, FetchError.HttpStatus), code);
        }
        catch (OperationCanceledException) when (offlineToken.IsCancellationRequested)
        {
            return (Done(FromCacheOr(entry, FetchStatus.Blocked, FetchError.OfflineMode)), code);
        }
        catch (OperationCanceledException) when (_disposing.IsCancellationRequested)
        {
            return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.Network)), code);
        }
        catch (OperationCanceledException)
        {
            Failed(key, _time.GetUtcNow(), null, fromServer: false);
            return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.Timeout)), code);
        }
        catch (HttpRequestException ex) when (IsPlainHttpRefused(ex))
        {
            return (NotRetried(entry, FetchStatus.Blocked, FetchError.InsecureHttp), code);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException: the connection broke while the body was read (HttpIOException and socket errors).
            Failed(key, _time.GetUtcNow(), null, fromServer: false);
            return (Done(FromCacheOr(entry, FetchStatus.Failed, FetchError.Network)), code);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private static void AddHeaders(HttpRequestMessage message, FetchRequest request, CacheEntry? entry)
    {
        if (request.Headers is not null)
        {
            foreach (var (name, value) in request.Headers)
                message.Headers.TryAddWithoutValidation(name, value);
        }
        if (entry?.ETag is { } etag)
            message.Headers.TryAddWithoutValidation("If-None-Match", etag);
        if (entry?.LastModified is { } lastModified)
            message.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
    }

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.IdnHost, b.IdnHost, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    private static bool IsPlainHttpRefused(Exception ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PlainHttpRefusedException)
                return true;
        }
        return false;
    }

    private static bool IsFresh(CacheEntry entry, DateTimeOffset now, TimeSpan maxAge) =>
        entry.FetchedAt <= now + ClockSkew && now - entry.FetchedAt < maxAge;

    private static FetchResult FromCacheOr(CacheEntry? entry, FetchStatus status, FetchError error) =>
        entry is null ? FetchResult.Fail(status, error) : new FetchResult(FetchStatus.Stale, entry.Body, entry.FetchedAt, error);

    private static Outcome Done(FetchResult result) => new(result, NotRetried: false);

    private static Outcome NotRetried(CacheEntry? entry, FetchStatus status, FetchError error) => new(FromCacheOr(entry, status, error), NotRetried: true);

    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>MaxAge, four times longer on a metered network or under battery saver.</summary>
    private TimeSpan EffectiveMaxAge(TimeSpan maxAge)
    {
        if (maxAge <= TimeSpan.Zero)
            return TimeSpan.Zero;
        if (!_saverActive && !_network.IsMetered)
            return maxAge;
        return maxAge > TimeSpan.MaxValue / 4 ? TimeSpan.MaxValue : maxAge * 4;
    }

    private bool IsBackingOff(string key, DateTimeOffset now)
    {
        lock (_gate)
            return _backoff.TryGetValue(key, out var state) && now < state.Until;
    }

    private void Succeeded(string key)
    {
        lock (_gate)
            _backoff.Remove(key);
    }

    private void Failed(string key, DateTimeOffset now, TimeSpan? retryAfter, bool fromServer)
    {
        lock (_gate)
        {
            var failures = _backoff.TryGetValue(key, out var state) ? state.Failures + 1 : 1;
            var wait = Backoff.Delay(failures, _random());
            if (retryAfter > wait)
                wait = retryAfter.Value;
            _backoff[key] = new BackoffState(failures, now + wait, fromServer);
        }
    }

    /// <summary>Starts following the network on the first request, so coming back online ends network backoff.</summary>
    private void WatchNetwork()
    {
        lock (_gate)
        {
            if (_watchingNetwork || _disposed)
                return;
            _watchingNetwork = true;
        }
        _network.Changed += OnNetworkChanged;
    }

    /// <summary>
    /// A network change while online (back online, or a new network): failures caused by the network are
    /// forgotten, so stale copies refresh once, on the next ask. A wait a server asked for still holds.
    /// Every change counts, because Windows can report online all along, for example with a virtual adapter.
    /// </summary>
    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (!_network.IsOnline)
            return;
        lock (_gate)
        {
            foreach (var key in _backoff.Where(b => !b.Value.FromServer).Select(b => b.Key).ToList())
                _backoff.Remove(key);
        }
    }

    private HttpClient Client()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null)
                return _client;

            var handler = _createHandler?.Invoke() ?? new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                ConnectTimeout = TimeSpan.FromSeconds(10),
                ConnectCallback = AddressPolicy.ConnectAsync,
            };
            _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            // "Atap/<version> (+https://github.com/anake-an/atap)"
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Atap", _version));
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentComment));
            return _client;
        }
    }

    /// <param name="Result">What the callers get.</param>
    /// <param name="NotRetried">The same widget copy must not ask again: a 4xx answer, a body over the cap, a refused redirect or public http.</param>
    private readonly record struct Outcome(FetchResult Result, bool NotRetried);

    /// <param name="Failures">Failures in a row.</param>
    /// <param name="Until">No request before this.</param>
    /// <param name="FromServer">The server answered with 429 or 5xx, so coming back online does not end the wait.</param>
    private readonly record struct BackoffState(int Failures, DateTimeOffset Until, bool FromServer);

    private sealed class Scope(HttpDataService owner, WidgetCapabilities capabilities) : IHttpDataService
    {
        private readonly Dictionary<string, FetchError> _refused = new(StringComparer.Ordinal);

        public WidgetCapabilities Capabilities => capabilities;

        public Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return owner.GetAsync(request, this, cancellationToken);
        }

        public FetchError? Refused(string key)
        {
            lock (_refused)
                return _refused.TryGetValue(key, out var error) ? error : null;
        }

        public void Refuse(string key, FetchError error)
        {
            lock (_refused)
                _refused[key] = error;
        }
    }
}

/// <summary>One network attempt, for the app's log. Holds no path, query or secret.</summary>
/// <param name="Site">The host for hosts a widget declares, else "site-" and a short hash of the host.</param>
/// <param name="StatusCode">The HTTP status, or null when no answer came.</param>
/// <param name="Status">What the callers got.</param>
/// <param name="Error">Why it failed, or None.</param>
public sealed record FetchAttempt(string Site, int? StatusCode, FetchStatus Status, FetchError Error);
