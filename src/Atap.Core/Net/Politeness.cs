using System.Net.Http.Headers;

namespace Atap.Core.Net;

/// <summary>How long to wait after failures: 1, 2, 4 seconds and so on up to 60 minutes, give or take 20 %.</summary>
public static class Backoff
{
    public static readonly TimeSpan First = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(60);

    /// <summary>The longest wait a server can ask for with Retry-After. Longer asks are cut to this.</summary>
    public static readonly TimeSpan LongestRetryAfter = TimeSpan.FromHours(24);

    /// <param name="failures">Failures in a row, 1 or more.</param>
    /// <param name="random">A number from 0 to 1. 0.5 gives the exact step.</param>
    public static TimeSpan Delay(int failures, double random)
    {
        var step = failures <= 1 ? First.TotalSeconds : First.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 30));
        step = Math.Min(step, Longest.TotalSeconds);
        var jitter = 1 + ((Math.Clamp(random, 0, 1) * 2) - 1) * 0.2;
        return TimeSpan.FromSeconds(step * jitter);
    }

    /// <summary>The wait a Retry-After header asks for, as seconds or a date. Null when there is none.</summary>
    public static TimeSpan? RetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now)
    {
        if (header is null)
            return null;
        TimeSpan? wait = header.Delta ?? (header.Date is { } date ? date - now : null);
        if (wait is not { } value)
            return null;
        if (value < TimeSpan.Zero)
            return TimeSpan.Zero;
        return value > LongestRetryAfter ? LongestRetryAfter : value;
    }
}

/// <summary>
/// Keeps requests to one host apart, so Atap never hammers a free public service. Requests reserve the
/// next free slot in the order they arrive. Holds no timer: the caller waits for its slot.
/// </summary>
public sealed class HostSpacing
{
    /// <summary>The gap for any host not listed.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    /// <summary>Hosts with their own gap. A listed name also covers its subdomains.</summary>
    public static IReadOnlyDictionary<string, TimeSpan> KnownIntervals { get; } = new Dictionary<string, TimeSpan>(StringComparer.OrdinalIgnoreCase)
    {
        ["e-solat.gov.my"] = TimeSpan.FromSeconds(60),
        ["data.gov.my"] = TimeSpan.FromSeconds(15),
        ["lrclib.net"] = TimeSpan.FromSeconds(2),
    };

    private readonly Dictionary<string, DateTimeOffset> _next = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <summary>The gap between two requests to <paramref name="host"/>.</summary>
    public static TimeSpan IntervalFor(string host)
    {
        foreach (var (name, interval) in KnownIntervals)
        {
            if (string.Equals(host, name, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith("." + name, StringComparison.OrdinalIgnoreCase))
                return interval;
        }
        return DefaultInterval;
    }

    /// <summary>Takes the next slot for <paramref name="host"/>. Returns how long to wait before sending, zero when it may go now.</summary>
    public TimeSpan Reserve(string host, DateTimeOffset now)
    {
        lock (_gate)
        {
            var slot = _next.TryGetValue(host, out var next) && next > now ? next : now;
            _next[host] = slot + IntervalFor(host);
            return slot - now;
        }
    }

    public void Clear()
    {
        lock (_gate)
            _next.Clear();
    }
}
