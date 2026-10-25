using Atap.Core.Scheduling;

namespace Atap.Core.Tests.Scheduling;

/// <summary>Wakes are counted as system timers that fired, measured by <see cref="CountingTime"/>.</summary>
public sealed class SchedulerCoalescingTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 30, TimeSpan.Zero);

    private readonly CountingTime _time = new(Start);
    private readonly Scheduler _scheduler;

    public SchedulerCoalescingTests() => _scheduler = new Scheduler(_time);

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public void NoItems_NoTimer()
    {
        _time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, _time.Created);
        Assert.Equal(0, _time.Fired);
        Assert.Equal(new SchedulerStats(0, 0), _scheduler.Stats);
    }

    [Fact]
    public void ThreeItemsInsideTheirSlack_ShareOneWake()
    {
        var ran = new List<DateTimeOffset>();
        _scheduler.At(Start.AddSeconds(10), () => ran.Add(_time.GetUtcNow()), new ScheduleOptions(Slack: TimeSpan.FromSeconds(30)));
        _scheduler.At(Start.AddSeconds(20), () => ran.Add(_time.GetUtcNow()), new ScheduleOptions(Slack: TimeSpan.FromSeconds(30)));
        _scheduler.At(Start.AddSeconds(25), () => ran.Add(_time.GetUtcNow()));

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(3, ran.Count);
        Assert.All(ran, at => Assert.Equal(Start.AddSeconds(25), at));
        Assert.Equal(1, _time.Fired);
        Assert.Equal(1, _scheduler.Stats.WakesLastHour);
    }

    [Fact]
    public void ItemsWithSlack_WaitForTheEarliestDeadline()
    {
        var ran = new List<DateTimeOffset>();
        _scheduler.At(Start.AddSeconds(10), () => ran.Add(_time.GetUtcNow()), new ScheduleOptions(Slack: TimeSpan.FromSeconds(30)));
        _scheduler.At(Start.AddSeconds(15), () => ran.Add(_time.GetUtcNow()), new ScheduleOptions(Slack: TimeSpan.FromSeconds(10)));

        _time.Advance(TimeSpan.FromMinutes(2));

        // The second deadline, 25 seconds in, comes first. Both are due by then.
        Assert.Equal([Start.AddSeconds(25), Start.AddSeconds(25)], ran);
        Assert.Equal(1, _time.Fired);
    }

    [Fact]
    public void ItemsWithinTheCoalesceWindow_ShareOneWakeAndNeverRunEarly()
    {
        var minute = new DateTimeOffset(2026, 10, 25, 9, 1, 0, TimeSpan.Zero);
        var ran = new List<(string Name, DateTimeOffset At)>();
        _scheduler.At(minute, () => ran.Add(("reminder", _time.GetUtcNow())));
        _scheduler.At(minute.AddMilliseconds(10), () => ran.Add(("later", _time.GetUtcNow())));
        _scheduler.EveryMinute(() => ran.Add(("tick", _time.GetUtcNow())));

        _time.AdvanceTo(minute.AddSeconds(1));

        Assert.Equal(["reminder", "later", "tick"], ran.Select(r => r.Name));
        Assert.All(ran, r => Assert.Equal(minute + Scheduler.TickOffset, r.At));
        Assert.Equal(1, _time.Fired);
    }

    [Fact]
    public void ItemsFurtherApart_GetTheirOwnWakes()
    {
        var runs = 0;
        _scheduler.At(Start.AddSeconds(1), () => runs++);
        _scheduler.At(Start.AddSeconds(1).AddMilliseconds(50), () => runs++);

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(2, runs);
        Assert.Equal(2, _time.Fired);
    }

    [Fact]
    public void EveryMinute_TicksAtMostFiftyMillisecondsAfterTheMinute()
    {
        var ticks = new List<DateTimeOffset>();
        _scheduler.EveryMinute(() => ticks.Add(_time.GetUtcNow()));
        // Something due just after each tick pulls the shared wake as late as it may go.
        var firstMinute = new DateTimeOffset(2026, 10, 25, 9, 1, 0, TimeSpan.Zero);
        for (var m = 0; m < 6; m++)
            _scheduler.At(firstMinute.AddMinutes(m).AddMilliseconds(39), () => { });

        _time.AdvanceTo(firstMinute.AddMinutes(6));

        Assert.Equal(6, ticks.Count);
        Assert.All(ticks, t =>
        {
            var intoMinute = TimeSpan.FromTicks(t.UtcTicks % TimeSpan.TicksPerMinute);
            Assert.InRange(intoMinute, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(50));
        });
        Assert.Equal(6, _time.Fired);
    }

    [Fact]
    public void EveryMinute_SubscribersShareOneWakeAMinute()
    {
        var ticks = 0;
        _scheduler.EveryMinute(() => ticks++);
        _scheduler.EveryMinute(() => ticks++);
        _scheduler.EveryMinute(() => ticks++);

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(30, ticks);
        Assert.Equal(10, _time.Fired);
    }

    [Fact]
    public void EverySecond_TicksJustAfterEachSecond_AndCarriesTheMinuteTick()
    {
        var seconds = new List<DateTimeOffset>();
        var minutes = 0;
        _scheduler.EverySecond(() => seconds.Add(_time.GetUtcNow()));
        _scheduler.EveryMinute(() => minutes++);

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(120, seconds.Count);
        Assert.All(seconds, t => Assert.Equal(20, t.Millisecond));
        Assert.Equal(2, minutes);
        Assert.Equal(120, _time.Fired);
    }

    [Fact]
    public void RefreshWithSlack_RidesTheMinuteTick()
    {
        var ticks = 0;
        var refreshes = new List<DateTimeOffset>();
        var period = TimeSpan.FromMinutes(20);
        _scheduler.EveryMinute(() => ticks++);
        _scheduler.Every(period, () => refreshes.Add(_time.GetUtcNow()), Scheduler.RefreshOptions(period));

        _time.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal(61, ticks);
        Assert.Equal(3, refreshes.Count);
        // No wake of its own: each refresh ran on a minute tick, within its 2 minutes of slack.
        Assert.Equal(61, _time.Fired);
        Assert.All(refreshes, r => Assert.Equal(20, r.Millisecond + (r.Second * 1000)));
    }

    [Fact]
    public void ClockOnlyForTenMinutes_GivesTenWakes()
    {
        _scheduler.EveryMinute(() => { });

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(10, _time.Fired);
        Assert.Equal(10, _scheduler.Stats.WakesLastHour);
        Assert.Equal(1, _time.Live);
    }

    [Fact]
    public void TimersFiringEarly_StillGiveOneWakeAMinute_AndTicksNeverBeforeTheMinute()
    {
        _time.FireEarly = TimeSpan.FromMilliseconds(5);
        var ticks = new List<DateTimeOffset>();
        _scheduler.EveryMinute(() => ticks.Add(_time.GetUtcNow()));

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(10, ticks.Count);
        Assert.Equal(10, _time.Fired);
        Assert.All(ticks, t => Assert.Equal(15, t.Millisecond + (t.Second * 1000)));
    }

    [Fact]
    public void TimersFiringEarly_NeverRunAOneShotItemBeforeItsTime()
    {
        _time.FireEarly = TimeSpan.FromMilliseconds(5);
        var due = Start.AddSeconds(10);
        DateTimeOffset? ranAt = null;
        _scheduler.At(due, () => ranAt = _time.GetUtcNow());

        _time.Advance(TimeSpan.FromSeconds(11));

        Assert.NotNull(ranAt);
        Assert.True(ranAt >= due, $"Ran at {ranAt:O}, before {due:O}.");
    }

    [Fact]
    public void ItemsDueTogether_RunInTheOrderTheyWereAdded()
    {
        var due = Start.AddSeconds(5);
        var order = new List<int>();
        for (var i = 0; i < 20; i++)
        {
            var n = i;
            _scheduler.At(due, () => order.Add(n));
        }

        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(Enumerable.Range(0, 20), order);
        Assert.Equal(1, _time.Fired);
    }

    [Fact]
    public void ManyItems_RunInDueOrder_EachOnce()
    {
        var random = new Random(25);
        var dues = Enumerable.Range(0, 200).Select(_ => Start.AddMilliseconds(random.Next(1, 600_000))).ToList();
        var ran = new List<DateTimeOffset>();
        var handles = dues.Select(due => _scheduler.At(due, () => ran.Add(due))).ToList();
        // Cancel a quarter of them, spread through the heap.
        var cancelled = new HashSet<int>();
        for (var i = 0; i < handles.Count; i += 4)
        {
            handles[i].Dispose();
            cancelled.Add(i);
        }

        _time.Advance(TimeSpan.FromMinutes(11));

        var expected = dues.Where((_, i) => !cancelled.Contains(i)).Order().ToList();
        Assert.Equal(expected, ran);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
        Assert.Equal(0, _time.Live);
    }
}
