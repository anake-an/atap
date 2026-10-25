using Atap.Core.Scheduling;

namespace Atap.Core.Tests.Scheduling;

/// <summary>Cancelling items and disposing the scheduler leave no items and no timer behind.</summary>
public sealed class SchedulerLifetimeTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 25, 9, 0, 30, TimeSpan.Zero);

    private readonly CountingTime _time = new(Start);
    private readonly Scheduler _scheduler;

    public SchedulerLifetimeTests() => _scheduler = new Scheduler(_time);

    public void Dispose() => _scheduler.Dispose();

    [Fact]
    public void DisposingEveryItem_ReturnsToZeroItemsAndNoTimer()
    {
        var handles = new List<IDisposable>();
        for (var i = 0; i < 10; i++)
        {
            handles.Add(_scheduler.At(Start.AddSeconds(i + 1), () => { }));
            handles.Add(_scheduler.Every(TimeSpan.FromSeconds(i + 1), () => { }, Scheduler.RefreshOptions(TimeSpan.FromSeconds(i + 1))));
            handles.Add(_scheduler.EveryMinute(() => { }));
            handles.Add(_scheduler.EverySecond(() => { }));
        }
        _time.Advance(TimeSpan.FromSeconds(5.5));
        Assert.Equal(35, _scheduler.Stats.ArmedItems);
        Assert.Equal(1, _time.Live);

        foreach (var handle in handles)
            handle.Dispose();

        Assert.Equal(0, _scheduler.Stats.ArmedItems);
        Assert.Equal(0, _time.Live);
        var fired = _time.Fired;
        _time.Advance(TimeSpan.FromHours(1));
        Assert.Equal(fired, _time.Fired);
    }

    [Fact]
    public void DisposingAnItem_RunsNothingAndKeepsTheOthers()
    {
        var runs = new List<string>();
        var first = _scheduler.At(Start.AddSeconds(1), () => runs.Add("first"));
        _scheduler.At(Start.AddSeconds(2), () => runs.Add("second"));

        first.Dispose();
        first.Dispose();
        _time.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(["second"], runs);
        Assert.Equal(0, _time.Live);
    }

    [Fact]
    public void RepeatingItem_DisposedInItsOwnCallback_StopsAtOnce()
    {
        var runs = 0;
        IDisposable? handle = null;
        handle = _scheduler.Every(TimeSpan.FromSeconds(1), () =>
        {
            runs++;
            if (runs == 3)
                handle!.Dispose();
        });

        _time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(3, runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
        Assert.Equal(0, _time.Live);
    }

    [Fact]
    public void ItemCancelledByAnEarlierCallbackInTheSameWake_DoesNotRun()
    {
        var due = Start.AddSeconds(1);
        var runs = new List<string>();
        IDisposable? second = null;
        _scheduler.At(due, () =>
        {
            runs.Add("first");
            second!.Dispose();
        });
        second = _scheduler.At(due, () => runs.Add("second"));
        var tick = _scheduler.EverySecond(() => runs.Add("tick"));
        _scheduler.At(due, () => tick.Dispose());

        _time.Advance(TimeSpan.FromSeconds(3));

        // The tick at 09:00:30.020 ran. The one at 09:00:31.020 shared the wake at 09:00:31 and was cancelled first.
        Assert.Equal(["tick", "first"], runs);
        Assert.Equal(2, _time.Fired);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
    }

    [Fact]
    public void ItemAddedInACallbackForThePast_RunsOnTheNextTurn()
    {
        var runs = new List<string>();
        _scheduler.At(Start.AddSeconds(1), () =>
        {
            runs.Add("outer");
            _scheduler.At(Start, () => runs.Add("inner"));
        });

        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(["outer", "inner"], runs);
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
        Assert.Equal(0, _time.Live);
    }

    [Fact]
    public void DisposedScheduler_DropsItemsAndTimer_AndRefusesNewItems()
    {
        var runs = 0;
        var item = _scheduler.Every(TimeSpan.FromSeconds(1), () => runs++);
        _scheduler.EveryMinute(() => runs++);

        _scheduler.Dispose();
        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(0, runs);
        Assert.Equal(new SchedulerStats(0, 0), _scheduler.Stats);
        Assert.Equal(0, _time.Live);
        Assert.Throws<ObjectDisposedException>(() => _scheduler.At(Start, () => { }));
        // Late calls from handles and the app are harmless.
        item.Dispose();
        _scheduler.NotifyClockChanged();
        _scheduler.NotifyResumed();
        _scheduler.SaverActive = true;
        _scheduler.Dispose();
        Assert.Equal(0, _time.Live);
    }

    [Fact]
    public void SchedulerDisposedByACallback_RunsNoMoreCallbacks()
    {
        var runs = new List<string>();
        var due = Start.AddSeconds(1);
        _scheduler.At(due, () =>
        {
            runs.Add("first");
            _scheduler.Dispose();
        });
        _scheduler.At(due, () => runs.Add("second"));

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(["first"], runs);
        Assert.Equal(0, _time.Live);
    }

    [Fact]
    public void NullCallback_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => _scheduler.At(Start, null!));
        Assert.Throws<ArgumentNullException>(() => _scheduler.EveryMinute(null!));
        Assert.Equal(0, _scheduler.Stats.ArmedItems);
        Assert.Equal(0, _time.Created);
    }
}
