namespace Atap.Core.Net;

/// <summary>Fetches remote data. Each widget gets its own copy, limited to the hosts its descriptor lists.</summary>
public interface IHttpDataService
{
    /// <summary>Never throws for network or policy problems. They come back as <see cref="FetchStatus.Failed"/> or <see cref="FetchStatus.Blocked"/>.</summary>
    Task<FetchResult> GetAsync(FetchRequest request, CancellationToken cancellationToken = default);
}

/// <param name="Url">What to fetch.</param>
/// <param name="MaxAge">A cached copy younger than this is used without a request.</param>
public sealed record FetchRequest(Uri Url, TimeSpan MaxAge)
{
    /// <summary>Requests with headers are cached in memory only.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>The URL came from a secret: cached in memory only and never logged.</summary>
    public bool FromSecret { get; init; }

    public bool DiskCache { get; init; } = true;

    public int MaxBytes { get; init; } = 1_048_576;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);
}

/// <param name="Status">Where the body came from, or why there is none.</param>
/// <param name="Body">The response body. Empty when the fetch failed and nothing was cached.</param>
/// <param name="FetchedAt">When the body was fetched, for "as of" text.</param>
/// <param name="Error">Why a fetch failed or was blocked.</param>
public sealed record FetchResult(FetchStatus Status, ReadOnlyMemory<byte> Body, DateTimeOffset? FetchedAt, FetchError Error)
{
    public static FetchResult Fail(FetchStatus status, FetchError error) => new(status, ReadOnlyMemory<byte>.Empty, null, error);
}

public enum FetchStatus
{
    /// <summary>Fetched just now.</summary>
    Fresh,

    /// <summary>From the cache, still within MaxAge.</summary>
    Cached,

    /// <summary>From the cache, older than MaxAge, because the fetch failed or the PC is offline.</summary>
    Stale,

    /// <summary>Not allowed by policy. No request was sent.</summary>
    Blocked,
    Failed,
}

public enum FetchError
{
    None,
    Offline,

    /// <summary>The Privacy offline switch is on.</summary>
    OfflineMode,
    HostNotAllowed,
    InsecureHttp,
    BackingOff,
    Timeout,
    HttpStatus,
    TooLarge,
    RedirectRefused,
    Network,
}
