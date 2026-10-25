namespace Atap.Core.Alerts;

/// <summary>
/// How many alerts a source may send: <paramref name="Burst"/> at once, then one more every
/// <paramref name="Interval"/>. Alerts over the limit are dropped.
/// </summary>
/// <param name="Burst">Alerts allowed in a row before the limit applies. At least 1.</param>
/// <param name="Interval">Time to earn one more alert. Longer than zero.</param>
public sealed record AlertRateLimit(int Burst, TimeSpan Interval)
{
    /// <summary>The push source: 1 alert per 5 seconds, with a burst of 3.</summary>
    public static AlertRateLimit Push { get; } = new(3, TimeSpan.FromSeconds(5));
}

/// <summary>
/// A token bucket for one source. Refilled from the time between calls, so it needs no timer. The credit
/// is kept in clock ticks, where one alert costs one interval, so the sums are exact.
/// </summary>
internal sealed class AlertRateBucket(AlertRateLimit limit, DateTimeOffset now)
{
    private long _credit = limit.Burst * limit.Interval.Ticks;
    private DateTimeOffset _refilledAt = now;

    public AlertRateLimit Limit { get; } = limit;

    /// <summary>True, and one alert's credit used, when the source may send an alert now.</summary>
    public bool TryTake(DateTimeOffset now)
    {
        var cost = Limit.Interval.Ticks;
        var elapsed = (now - _refilledAt).Ticks;
        if (elapsed > 0)
            _credit = Math.Min(Limit.Burst * cost, _credit + elapsed);
        // A clock set back gives nothing back, and the next refill counts from here.
        _refilledAt = now;

        if (_credit < cost)
            return false;
        _credit -= cost;
        return true;
    }
}
