using Atap.Core.Power;
using Atap.Core.Scheduling;

namespace Atap.Core.Tests.Scheduling;

/// <summary>Battery saver, clock changes and sleep. A wall shift with no elapsed time is how a sleep looks to the scheduler.</summary>
public sealed class SchedulerPowerAndClockTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 30, TimeSpan.Zero);
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private readonly CountingTime _time = new(Start);
    private readonly Scheduler _scheduler;

    public SchedulerPowerAndClockTests() => _scheduler = new Scheduler(_time);

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public void Saver_StretchesDeferrablePeriodsFourTimes_AndNothingElse()
    {
        _scheduler.SaverActive = true;
        var deferrable = new List<DateTimeOffset>();
        var normal = 0;
        var ticks = 0;
        _scheduler.Every(TenMinutes, () => deferrable.Add(_time.GetUtcNow()), new ScheduleOptions(Deferrable: true));
        _scheduler.Every(TenMinutes, () => normal++);
        _scheduler.EveryMinute(() => ticks++);

        _time.Advance(TimeSpan.FromMinutes(80));

        // Four times longer: every 40 minutes, not every 10.
        Assert.Equal(4, BatterySaverPolicy.Multiplier);
        Assert.Equal([Start.AddMinutes(40), Start.AddMinutes(80)], deferrable);
        Assert.Equal(8, normal);
        Assert.Equal(80, ticks);
    }

    [Fact]
    public void SaverTurningOn_StretchesTheWaitAlreadyUnderway()
    {
        var runs = new List<DateTimeOffset>();
        _scheduler.Every(TenMinutes, () => runs.Add(_time.GetUtcNow()), new ScheduleOptions(Deferrable: true));

        _time.Advance(TimeSpan.FromMinutes(5));
        _scheduler.SaverActive = true;
        _time.Advance(TimeSpan.FromMinutes(75));

        Assert.Equal([Start.AddMinutes(40), Start.AddMinutes(80)], runs);
    }

    [Fact]
    public void SaverTurningOff_RunsAnOverdueItemAtOnce_ThenTheNormalPeriod()
    {
        _scheduler.SaverActive = true;
        var runs = new List<DateTimeOffset>();
        _scheduler.Every(TenMinutes, () => runs.Add(_time.GetUtcNow()), new ScheduleOptions(Deferrable: true));

        _time.Advance(TimeSpan.FromMinutes(15));
        Assert.Empty(runs);
        _scheduler.SaverActive = false;
        Assert.Equal([Start.AddMinutes(15)], runs);

        _time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal([Start.AddMinutes(15), Start.AddMinutes(25)], runs);
    }

    [Fact]
    public void Saver_NeverDefersOneShotItems()
    {
        _scheduler.SaverActive = true;
        var runs = 0;
        _scheduler.At(Start.AddMinutes(10), () => runs++, new ScheduleOptions(Deferrable: true));

        _time.Advance(TenMinutes);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void ClockChange_RunsAlignedTicksAtOnce_AndArmsForTheNewBoundary()
    {
        var ticks = new List<DateTimeOffset>();
        _scheduler.EveryMinute(() => ticks.Add(_time.GetUtcNow()));
        var created = _time.Created;

        // The clock jumps 15 seconds ahead: 09:00:45 on the wall.
        _time.WallShift = TimeSpan.FromSeconds(15);
        _scheduler.NotifyClockChanged();

        Assert.Equal([Start.AddSeconds(15)], ticks);
        Assert.True(_time.Created > created);

        // Next tick at 09:01:00.020 on the new wall clock, 15.020 seconds of elapsed time later.
        _time.Advance(TimeSpan.FromSeconds(15));
        Assert.Single(ticks);
        _time.Advance(TimeSpan.FromMilliseconds(20));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 9, 1, 0, 20, TimeSpan.Zero), ticks[^1]);
        Assert.Equal(1, _time.Live);
    }

    [Fact]
    public void ClockChange_AtItemPassedByTheJump_RunsAtOnce()
    {
        var runs = 0;
        _scheduler.At(Start.AddMinutes(30), () => runs++);

        _time.WallShift = TimeSpan.FromHours(1);
        _scheduler.NotifyClockChanged();

        Assert.Equal(1, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void ClockChange_AtItemAhead_ArmsForItsNewDistance()
    {
        var runs = 0;
        _scheduler.At(Start.AddMinutes(30), () => runs++);

        // Ten minutes forward: the item is now 20 minutes away.
        _time.WallShift = TenMinutes;
        _scheduler.NotifyClockChanged();
        _time.Advance(TimeSpan.FromMinutes(19));
        Assert.Equal(0, runs);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ClockChange_WallClockItem_RunsAtOnce_SoItsOwnerPlansAgain()
    {
        var runs = new List<DateTimeOffset>();
        _scheduler.At(Start.AddHours(2), () => runs.Add(_time.GetUtcNow()), new ScheduleOptions(WallClock: true));
        _scheduler.At(Start.AddHours(2), () => runs.Add(DateTimeOffset.MaxValue));

        _scheduler.NotifyClockChanged();
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal([Start], runs);
        Assert.Equal(1, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void Resume_WallClockItemNotYetDue_RunsAtOnce()
    {
        var runs = 0;
        _scheduler.At(Start.AddHours(2), () => runs++, new ScheduleOptions(WallClock: true));

        Sleep(TimeSpan.FromHours(1));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, runs);
    }

    [Fact]
    public void Resume_MissedRunOnceItem_RunsAtOnce()
    {
        var runs = 0;
        _scheduler.At(Start.AddMinutes(1), () => runs++, new ScheduleOptions(Missed: MissedRun.RunOnce));

        Sleep(TimeSpan.FromHours(1));

        Assert.Equal(1, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void Resume_MissedRepeatingSkipItem_RunsOnceTenToFifteenSecondsLater()
    {
        var runs = new List<DateTimeOffset>();
        _scheduler.Every(TimeSpan.FromMinutes(5), () => runs.Add(_time.GetUtcNow()));

        Sleep(TimeSpan.FromHours(1));
        var resumed = _time.GetUtcNow();
        Assert.Empty(runs);

        _time.Advance(TimeSpan.FromSeconds(20));
        var ranAt = Assert.Single(runs);
        Assert.InRange(ranAt - resumed, Scheduler.ResumeRefreshDelay, Scheduler.ResumeRefreshDelay + Scheduler.ResumeJitter);
    }

    [Fact]
    public void Resume_MissedOneTimeSkipItem_RunsOnceInTheResumeRefresh()
    {
        // A profile switch, a save or the end of a live state that fell during sleep must still happen.
        var runs = new List<DateTimeOffset>();
        _scheduler.At(Start.AddMinutes(1), () => runs.Add(_time.GetUtcNow()));

        Sleep(TimeSpan.FromHours(1));
        var resumed = _time.GetUtcNow();
        Assert.Empty(runs);

        _time.Advance(TimeSpan.FromMinutes(5));

        var ranAt = Assert.Single(runs);
        Assert.InRange(ranAt - resumed, Scheduler.ResumeRefreshDelay, Scheduler.ResumeRefreshDelay + Scheduler.ResumeJitter);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void Resume_MissedOneTimeSkipItem_SharesTheRefreshWakeWithRepeatingItems()
    {
        var runs = 0;
        _scheduler.At(Start.AddMinutes(1), () => runs++);
        _scheduler.Every(TimeSpan.FromMinutes(5), () => runs++);

        Sleep(TimeSpan.FromHours(1));
        var fired = _time.Fired;
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(2, runs);
        Assert.Equal(1, _time.Fired - fired);
    }

    [Fact]
    public void Resume_MissedOneTimeRunOnceItem_StillRuns()
    {
        var ran = false;
        _scheduler.At(Start.AddMinutes(1), () => ran = true, new ScheduleOptions(Missed: MissedRun.RunOnce));

        Sleep(TimeSpan.FromHours(1));
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.True(ran);
    }

    [Fact]
    public void Resume_MissedSkipItems_ShareOneRefreshWake()
    {
        var runs = 0;
        _scheduler.Every(TimeSpan.FromMinutes(5), () => runs++);
        _scheduler.Every(TimeSpan.FromMinutes(7), () => runs++);

        Sleep(TimeSpan.FromHours(1));
        var fired = _time.Fired;
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal(2, runs);
        Assert.Equal(1, _time.Fired - fired);
    }

    [Fact]
    public void Resume_MissedRunOnceRepeatingItem_RunsAtOnceThenKeepsItsPeriod()
    {
        var runs = new List<DateTimeOffset>();
        _scheduler.Every(TimeSpan.FromMinutes(5), () => runs.Add(_time.GetUtcNow()), new ScheduleOptions(Missed: MissedRun.RunOnce));

        Sleep(TimeSpan.FromHours(1));
        var resumed = _time.GetUtcNow();
        _time.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal([resumed, resumed.AddMinutes(5)], runs);
    }

    [Fact]
    public void Resume_AlignedTicksRunAtOnce_ThenOnTheMinute()
    {
        var ticks = new List<DateTimeOffset>();
        _scheduler.EveryMinute(() => ticks.Add(_time.GetUtcNow()));

        Sleep(TimeSpan.FromHours(8).Add(TimeSpan.FromSeconds(5)));
        _time.Advance(TimeSpan.FromSeconds(30));

        Assert.Equal(2, ticks.Count);
        Assert.Equal(Start.AddHours(8).AddSeconds(5), ticks[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 17, 1, 0, 20, TimeSpan.Zero), ticks[1]);
    }

    [Fact]
    public void Resume_ItemNotYetDue_KeepsItsTime()
    {
        var runs = 0;
        _scheduler.At(Start.AddHours(2), () => runs++);

        Sleep(TimeSpan.FromHours(1));
        _time.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, runs);

        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ThrowingRepeatingItem_KeepsItsScheduleAndOthersStillRun()
    {
        var faults = 0;
        _scheduler.JobFailed += (_, _) => faults++;
        var runs = 0;
        _scheduler.Every(TimeSpan.FromMinutes(1), () => throw new InvalidOperationException("boom"));
        _scheduler.Every(TimeSpan.FromMinutes(1), () => runs++);

        _time.Advance(TimeSpan.FromMinutes(3));

        Assert.Equal(3, faults);
        Assert.Equal(3, runs);
        Assert.Equal(2, _scheduler.Stats.ArmedItems);
    }

    /// <summary>The PC sleeps: the wall clock moves on, no timer fires, then resume is reported.</summary>
    private void Sleep(TimeSpan duration)
    {
        _time.WallShift += duration;
        _scheduler.NotifyResumed();
    }
}
