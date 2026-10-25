using Atap.Core.Scheduling;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Scheduling;

/// <summary>The basics: one-shot and repeating items, errors, extreme times and the clock set back.</summary>
public sealed class SchedulerTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 30, TimeSpan.Zero));
    private readonly Scheduler _scheduler;

    public SchedulerTests() => _scheduler = new Scheduler(_time);

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public void NoItems_NoWakes()
    {
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(new SchedulerStats(0, 0), _scheduler.Stats);
    }

    [Fact]
    public void At_RunsOnceAtItsTime()
    {
        var runs = 0;
        _scheduler.At(_time.GetUtcNow().AddSeconds(10), () => runs++);

        _time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, runs);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, runs);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void DisposedItem_NeverRuns()
    {
        var runs = 0;
        var item = _scheduler.At(_time.GetUtcNow().AddSeconds(10), () => runs++);

        item.Dispose();
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void EveryMinute_TicksJustAfterEachMinute()
    {
        var ticks = new List<DateTimeOffset>();
        using var tick = _scheduler.EveryMinute(() => ticks.Add(_time.GetUtcNow()));

        // The fake clock reports the target time inside callbacks, so step it one boundary at a time.
        _time.Advance(TimeSpan.FromMilliseconds(30_020));
        _time.Advance(TimeSpan.FromMinutes(1));
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(3, ticks.Count);
        Assert.All(ticks, t => Assert.Equal(20, t.Millisecond + (t.Second * 1000)));
    }

    [Fact]
    public void Every_RaisesShortPeriodsToOneSecond()
    {
        var runs = 0;
        using var item = _scheduler.Every(TimeSpan.FromMilliseconds(10), () => runs++);

        for (var i = 0; i < 3; i++)
            _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(3, runs);
    }

    [Fact]
    public void ItemsDueTogether_ShareOneWake()
    {
        var due = _time.GetUtcNow().AddSeconds(5);
        var runs = 0;
        _scheduler.At(due, () => runs++);
        _scheduler.At(due.AddMilliseconds(500), () => runs++, new ScheduleOptions(Slack: TimeSpan.Zero));
        _scheduler.At(due.AddMilliseconds(-500), () => runs++, new ScheduleOptions(Slack: TimeSpan.FromSeconds(1)));

        _time.Advance(TimeSpan.FromSeconds(5));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(3, runs);
        Assert.Equal(2, _scheduler.Stats.WakesLastHour);
    }

    [Fact]
    public void ThrowingItem_IsReportedAndOthersStillRun()
    {
        var faults = new List<SchedulerFault>();
        _scheduler.JobFailed += (_, fault) => faults.Add(fault);
        var runs = 0;
        var due = _time.GetUtcNow().AddSeconds(1);
        _scheduler.At(due, () => throw new InvalidOperationException("boom"));
        _scheduler.At(due, () => runs++);

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, runs);
        Assert.IsType<InvalidOperationException>(Assert.Single(faults).Exception);
    }

    [Fact]
    public void At_FarAhead_ArmsAgainUntilItIsDue()
    {
        // System timers refuse delays over about 49.7 days.
        var runs = 0;
        _scheduler.At(_time.GetUtcNow().AddDays(60), () => runs++);

        for (var day = 1; day < 60; day++)
            _time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, runs);
        Assert.Equal(1, _scheduler.Stats.ArmedItems);

        _time.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void ExtremeTimesAndPeriods_DoNotThrow()
    {
        var runs = 0;
        _scheduler.At(DateTimeOffset.MaxValue, () => runs++, new ScheduleOptions(Slack: TimeSpan.FromMinutes(5)));
        _scheduler.Every(TimeSpan.MaxValue, () => runs++, new ScheduleOptions(Deferrable: true));
        _scheduler.SaverActive = true;

        _time.Advance(TimeSpan.FromDays(2));

        Assert.Equal(0, runs);
        Assert.Equal(2, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void Every_AfterTheClockIsSetBack_RunsOnePeriodFromNow()
    {
        var wall = new WallClock(_time);
        using var scheduler = new Scheduler(wall);
        var runs = 0;
        scheduler.Every(TimeSpan.FromMinutes(5), () => runs++);

        wall.Shift = TimeSpan.FromHours(-1);
        scheduler.NotifyClockChanged();
        _time.Advance(TimeSpan.FromMinutes(5));

        // Not 65 minutes later.
        Assert.Equal(1, runs);
    }

    [Fact]
    public void At_AfterTheClockIsSetBack_KeepsItsWallClockTime()
    {
        var wall = new WallClock(_time);
        using var scheduler = new Scheduler(wall);
        var runs = 0;
        scheduler.At(wall.GetUtcNow().AddMinutes(5), () => runs++);

        wall.Shift = TimeSpan.FromHours(-1);
        scheduler.NotifyClockChanged();
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, runs);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void Resume_AfterTheClockWentBackDuringSleep_RunsRepeatingItemsOnePeriodFromNow()
    {
        var wall = new WallClock(_time);
        using var scheduler = new Scheduler(wall);
        var runs = 0;
        scheduler.Every(TimeSpan.FromMinutes(5), () => runs++);

        wall.Shift = TimeSpan.FromHours(-1);
        scheduler.NotifyResumed();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Resume_MissedRepeatingItem_RefreshesOnceSoonAfterThenKeepsItsPeriod()
    {
        var wall = new WallClock(_time);
        using var scheduler = new Scheduler(wall);
        var runs = 0;
        scheduler.Every(TimeSpan.FromMinutes(5), () => runs++);

        // The PC slept for an hour, so the timer never fired. Skip runs once 10 to 15 seconds after resume.
        wall.Shift = TimeSpan.FromHours(1);
        scheduler.NotifyResumed();
        _time.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, runs);

        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(1, runs);

        // Then every 5 minutes from that refresh, not from the missed slot.
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(1, runs);
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, runs);
    }

    /// <summary>A wall clock that can jump. Timers still count elapsed time, as system timers do.</summary>
    private sealed class WallClock(FakeTimeProvider elapsed) : TimeProvider
    {
        public TimeSpan Shift { get; set; }

        public override long TimestampFrequency => elapsed.TimestampFrequency;

        public override DateTimeOffset GetUtcNow() => elapsed.GetUtcNow() + Shift;

        public override long GetTimestamp() => elapsed.GetTimestamp();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            elapsed.CreateTimer(callback, state, dueTime, period);
    }
}
