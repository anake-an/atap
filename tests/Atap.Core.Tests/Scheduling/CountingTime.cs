using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Scheduling;

/// <summary>
/// Fake time that counts the system timers a scheduler uses: how many it armed, how many fired and how many
/// are still alive. The wall clock can jump while timers keep counting elapsed time, as on Windows, and
/// timers can fire a little early, as system timers sometimes do.
/// </summary>
internal sealed class CountingTime : TimeProvider
{
    private readonly FakeTimeProvider _elapsed;
    private readonly List<CountingTimer> _live = [];

    public CountingTime(DateTimeOffset start) => _elapsed = new FakeTimeProvider(start);

    /// <summary>Added to the wall clock only. Timers do not see it, so a shift is a clock change or a sleep.</summary>
    public TimeSpan WallShift { get; set; }

    /// <summary>Every timer over a second armed from now on fires this much before its delay ends.</summary>
    public TimeSpan FireEarly { get; set; }

    public int Created { get; private set; }

    public int Fired { get; private set; }

    /// <summary>Timers created and not yet disposed.</summary>
    public int Live => _live.Count;

    public override long TimestampFrequency => _elapsed.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => _elapsed.GetUtcNow() + WallShift;

    public override long GetTimestamp() => _elapsed.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Created++;
        var timer = new CountingTimer(this);
        _live.Add(timer);
        var delay = Early(dueTime);
        timer.FiresAt = delay == Timeout.InfiniteTimeSpan ? null : _elapsed.GetUtcNow() + delay;
        // A zero delay fires inside CreateTimer, so the timer is set up before the fake one exists.
        timer.Inner = _elapsed.CreateTimer(
            s =>
            {
                Fired++;
                timer.FiresAt = null;
                callback(s);
            },
            state,
            delay,
            period);
        return timer;
    }

    /// <summary>Moves time on by <paramref name="by"/>, firing each timer at its own moment on the way.</summary>
    public void Advance(TimeSpan by)
    {
        var end = _elapsed.GetUtcNow() + by;
        while (NextFire() is { } next && next <= end)
            _elapsed.Advance(next - _elapsed.GetUtcNow());
        _elapsed.Advance(end - _elapsed.GetUtcNow());
    }

    /// <summary>Moves time on to the wall-clock moment <paramref name="wall"/>, firing timers on the way.</summary>
    public void AdvanceTo(DateTimeOffset wall) => Advance(wall - GetUtcNow());

    private DateTimeOffset? NextFire()
    {
        DateTimeOffset? next = null;
        foreach (var timer in _live)
        {
            if (timer.FiresAt is { } at && (next is null || at < next))
                next = at;
        }
        return next;
    }

    /// <summary>Only timers over a second fire early. Drift grows with the delay, so short timers fire on time.</summary>
    private TimeSpan Early(TimeSpan dueTime) =>
        dueTime == Timeout.InfiniteTimeSpan || dueTime <= TimeSpan.FromSeconds(1) ? dueTime : dueTime - FireEarly;

    private sealed class CountingTimer(CountingTime owner) : ITimer
    {
        public ITimer? Inner { get; set; }

        public DateTimeOffset? FiresAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var delay = owner.Early(dueTime);
            FiresAt = delay == Timeout.InfiniteTimeSpan ? null : owner._elapsed.GetUtcNow() + delay;
            return Inner?.Change(delay, period) ?? false;
        }

        public void Dispose()
        {
            FiresAt = null;
            Inner?.Dispose();
            owner._live.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
