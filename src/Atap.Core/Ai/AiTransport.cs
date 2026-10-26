using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Atap.Core.Net;

namespace Atap.Core.Ai;

/// <summary>
/// Sends Ask AI questions and key checks. Its own client, made on the first request, with the same address
/// policy, User-Agent and offline switch as <see cref="HttpDataService"/>: plain http reaches only private
/// addresses, checked after DNS. No cache, no cookies and no redirects, and each provider has one fixed host.
/// </summary>
/// <remarks>
/// <para>Limits: 10 seconds to connect, 30 seconds to the first piece of the answer (90 for your own server, which
/// may need to load its model), 30 seconds without anything in the stream, 3 minutes in all, and 64 KB of answer.
/// It never tries again by itself.</para>
/// <para>Never throws for network or service problems. They come back as a Failed event. Core never logs:
/// <see cref="Attempted"/> reports each request by provider, status and kind of error only, never the
/// question, the answer or the key.</para>
/// </remarks>
public sealed class AiTransport : IDisposable
{
    public static readonly TimeSpan ConnectLimit = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FirstTokenLimit = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OwnServerFirstTokenLimit = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan StallLimit = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan TotalLimit = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan CheckLimit = TimeSpan.FromSeconds(30);

    /// <summary>The answer stops here, as UTF-8.</summary>
    public const int MaxAnswerBytes = 64 * 1024;

    /// <summary>All the stream may hold, with its JSON around each piece. Far more than 64 KB of answer needs.</summary>
    internal const long MaxStreamBytes = 8L * 1024 * 1024;

    internal const int MaxLineBytes = 256 * 1024;

    /// <summary>Only the start of an error body is read, to tell kinds of error apart. It is never kept.</summary>
    private const int MaxErrorBytes = 16 * 1024;

    private readonly string _version;
    private readonly INetworkState _network;
    private readonly Func<HttpMessageHandler>? _createHandler;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _disposing = new();
    private CancellationTokenSource _offlineSwitch = new();
    private CancellationTokenSource _networkLost = new();
    private HttpClient? _client;
    private bool _offlineMode;
    private bool _disposed;

    /// <param name="version">The app version for the User-Agent.</param>
    /// <param name="network">Questions fail at once while offline, and stop when the PC goes offline. Your own server on this PC or network still works.</param>
    /// <param name="createHandler">Replaces the real network handler, for tests. Called once, on the first request.</param>
    /// <param name="time">The clock for the time limits. The system clock when null.</param>
    public AiTransport(string version, INetworkState network, Func<HttpMessageHandler>? createHandler = null, TimeProvider? time = null)
    {
        _version = version;
        _network = network;
        _createHandler = createHandler;
        _time = time ?? TimeProvider.System;
        _network.Changed += OnNetworkChanged;
    }

    /// <summary>Each request: provider, status and kind of error. Raised on a worker thread after the caller has its result.</summary>
    public event EventHandler<AiAttempt>? Attempted;

    /// <summary>The Privacy offline switch. Turning it on stops every request at once, your own server too.</summary>
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
                    stop = _offlineSwitch;
                    _offlineSwitch = new CancellationTokenSource();
                }
            }
            stop?.Cancel();
        }
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

    /// <summary>
    /// Asks the question in the last turn, with the turns before it. Pieces of the answer go to
    /// <paramref name="onDelta"/> on a worker thread as they arrive. Returns Done or Failed.
    /// </summary>
    public async Task<AiEvent> AskAsync(AiAccount account, IReadOnlyList<AiTurn> turns, Action<AiEvent> onDelta, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(onDelta);
        if (turns.Count == 0 || turns[^1].Role != AiRole.User)
            throw new ArgumentException("The last turn must be the user's question.", nameof(turns));

        var dialect = AiDialects.For(account.Provider);
        var firstToken = account.Provider == AiProvider.Custom ? OwnServerFirstTokenLimit : FirstTokenLimit;
        var (result, code) = await SendAsync(account, dialect.BuildAsk(account, turns), TotalLimit, firstToken,
            (response, token, waiting) => ReadStreamAsync(dialect, response, onDelta, waiting, token), cancellationToken).ConfigureAwait(false);
        Report(account, code, result.Kind == AiEventKind.Failed ? result.Error : AiError.None);
        return result;
    }

    /// <summary>Checks the key with a request that costs nothing, such as listing models. None when it works.</summary>
    public async Task<AiError> CheckAsync(AiAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        var dialect = AiDialects.For(account.Provider);
        var (result, code) = await SendAsync(account, dialect.BuildCheck(account), CheckLimit, CheckLimit,
            (_, _, _) => Task.FromResult(AiEvent.Done), cancellationToken).ConfigureAwait(false);
        var error = result.Kind == AiEventKind.Failed ? result.Error : AiError.None;
        Report(account, code, error);
        return error;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _client?.Dispose();
            _client = null;
        }
        _network.Changed -= OnNetworkChanged;
        _disposing.Cancel();
    }

    /// <summary>
    /// The policy check, without sending anything. Null when the request may go out. Each service has one fixed
    /// https host. Your own server may use https anywhere, and plain http only to this PC or a private network.
    /// </summary>
    public static AiError? Check(Uri url, AiAccount account)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(account);
        if (!url.IsAbsoluteUri || url.UserInfo.Length > 0)
            return AiError.NotAllowed;
        var expected = account.BaseUrl;
        if (!string.Equals(url.IdnHost, expected.IdnHost, StringComparison.OrdinalIgnoreCase) || url.Port != expected.Port
            || !string.Equals(url.Scheme, expected.Scheme, StringComparison.OrdinalIgnoreCase))
            return AiError.NotAllowed;
        if (url.Scheme == Uri.UriSchemeHttps)
            return null;
        if (url.Scheme != Uri.UriSchemeHttp || account.Provider != AiProvider.Custom)
            return AiError.NotAllowed;
        // A name is judged after DNS, when the connection is made. An address can be judged now.
        if (IPAddress.TryParse(url.DnsSafeHost.Trim('[', ']'), out var address) && !AddressPolicy.IsPrivate(address))
            return AiError.NotAllowed;
        return null;
    }

    /// <summary>True when a server address can be used for your own server: http or https, with no name and password, query or fragment.</summary>
    public static bool IsValidServer(Uri? server) =>
        server is { IsAbsoluteUri: true } url
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
        && url.UserInfo.Length == 0
        && url.Query.Length == 0
        && url.Fragment.Length == 0
        && url.IdnHost.Length > 0;

    /// <summary>The real handler: no redirects, no cookies, 10 seconds to connect, and the plain http check on every connection.</summary>
    internal static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
        ConnectTimeout = ConnectLimit,
        ConnectCallback = AddressPolicy.ConnectAsync,
    };

    /// <param name="read">Reads a successful response. Gets the response, the token and a callback that sets the wait for the next piece.</param>
    private async Task<(AiEvent Result, int? StatusCode)> SendAsync(
        AiAccount account,
        HttpRequestMessage request,
        TimeSpan total,
        TimeSpan firstWait,
        Func<HttpResponseMessage, CancellationToken, Action<TimeSpan>, Task<AiEvent>> read,
        CancellationToken cancellationToken)
    {
        using var message = request;
        var url = message.RequestUri!;
        if (Check(url, account) is { } refused)
            return (AiEvent.Failed(refused), null);

        var local = AddressPolicy.IsLocalTarget(url);
        CancellationToken offlineSwitch;
        CancellationToken networkLost;
        lock (_gate)
        {
            if (_disposed)
                return (AiEvent.Failed(AiError.Stopped), null);
            if (_offlineMode)
                return (AiEvent.Failed(AiError.Offline), null);
            offlineSwitch = _offlineSwitch.Token;
            networkLost = _networkLost.Token;
        }
        if (!local && !_network.IsOnline)
            return (AiEvent.Failed(AiError.Offline), null);

        using var totalTimer = new CancellationTokenSource(total, _time);
        using var waitTimer = new CancellationTokenSource(firstWait, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            [cancellationToken, offlineSwitch, local ? CancellationToken.None : networkLost, _disposing.Token, totalTimer.Token, waitTimer.Token]);
        int? code = null;
        HttpResponseMessage? response = null;
        try
        {
            response = await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            code = (int)response.StatusCode;
            // Redirects are never followed: a key must not travel to another address.
            if (code is >= 300 and < 400)
                return (AiEvent.Failed(AiError.NotAllowed), code);
            if (code is not (>= 200 and < 300))
            {
                var body = await ReadErrorAsync(response.Content, linked.Token).ConfigureAwait(false);
                return (AiEvent.Failed(AiDialects.For(account.Provider).MapStatus(code.Value, body)), code);
            }
            return (await read(response, linked.Token, wait => waitTimer.CancelAfter(wait)).ConfigureAwait(false), code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposing.IsCancellationRequested)
        {
            return (AiEvent.Failed(AiError.Stopped), code);
        }
        catch (OperationCanceledException) when (offlineSwitch.IsCancellationRequested || networkLost.IsCancellationRequested)
        {
            return (AiEvent.Failed(AiError.Offline), code);
        }
        catch (OperationCanceledException) when (totalTimer.IsCancellationRequested || waitTimer.IsCancellationRequested)
        {
            return (AiEvent.Failed(AiError.NoAnswerInTime), code);
        }
        catch (OperationCanceledException)
        {
            // The handler's own connect limit.
            return (AiEvent.Failed(AiError.CannotReachServer), code);
        }
        catch (HttpRequestException ex) when (HttpDataService.IsPlainHttpRefused(ex))
        {
            return (AiEvent.Failed(AiError.NotAllowed), code);
        }
        catch (ObjectDisposedException)
        {
            return (AiEvent.Failed(AiError.Stopped), code);
        }
        catch (InvalidDataException)
        {
            return (AiEvent.Failed(AiError.Unexpected), code);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException: the connection broke while the answer was read.
            return (AiEvent.Failed(AiError.CannotReachServer), code);
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// Reads the stream until the dialect says Done or Failed, or the body ends. Before the first piece the
    /// first-token limit runs. After it, every line read starts the stall limit again.
    /// </summary>
    private static async Task<AiEvent> ReadStreamAsync(
        IAiDialect dialect, HttpResponseMessage response, Action<AiEvent> onDelta, Action<TimeSpan> wait, CancellationToken cancellationToken)
    {
        var answerBytes = 0;
        var started = false;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var reader = new SseReader(stream, MaxLineBytes, () =>
        {
            if (started)
                wait(StallLimit);
        });
        while (true)
        {
            if (reader.BytesRead > MaxStreamBytes)
                return started ? AiEvent.Done with { Capped = true } : AiEvent.Failed(AiError.Unexpected);
            var message = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (message is not { } next)
                return started ? AiEvent.Done : AiEvent.Failed(AiError.Unexpected);
            var result = dialect.Read(next);
            switch (result?.Kind)
            {
                case AiEventKind.Delta:
                    if (!started)
                    {
                        started = true;
                        wait(StallLimit);
                    }
                    var text = result.Text;
                    var bytes = Encoding.UTF8.GetByteCount(text);
                    if (answerBytes + bytes > MaxAnswerBytes)
                    {
                        var fits = Fit(text, MaxAnswerBytes - answerBytes);
                        if (fits.Length > 0)
                            onDelta(AiEvent.Delta(fits));
                        return AiEvent.Done with { Capped = true };
                    }
                    answerBytes += bytes;
                    onDelta(result);
                    break;
                case AiEventKind.Done:
                    return started ? AiEvent.Done : AiEvent.Failed(AiError.Unexpected);
                case AiEventKind.Failed:
                    return result;
            }
        }
    }

    /// <summary>The start of <paramref name="text"/> that fits in this many UTF-8 bytes, never cutting a letter in half.</summary>
    private static string Fit(string text, int room)
    {
        var used = 0;
        var index = 0;
        while (index < text.Length)
        {
            var length = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
            var size = Encoding.UTF8.GetByteCount(text.AsSpan(index, length));
            if (used + size > room)
                break;
            used += size;
            index += length;
        }
        return text[..index];
    }

    private static async Task<string> ReadErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaxErrorBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            filled += read;
        }
        return Encoding.UTF8.GetString(buffer, 0, filled);
    }

    private void Report(AiAccount account, int? code, AiError error)
    {
        if (error == AiError.Stopped)
            return;
        Attempted?.Invoke(this, new AiAttempt(account.Provider, code, error));
    }

    private HttpClient Client()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_client is not null)
                return _client;
            _client = new HttpClient(_createHandler?.Invoke() ?? CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan };
            // "Atap/<version> (+https://github.com/anake-an/atap)", as for every other request.
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Atap", _version));
            _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(HttpDataService.UserAgentComment));
            _client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
            return _client;
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_network.IsOnline)
            return;
        CancellationTokenSource stop;
        lock (_gate)
        {
            stop = _networkLost;
            _networkLost = new CancellationTokenSource();
        }
        stop.Cancel();
    }
}
