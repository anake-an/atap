using Atap.Core.Power;

namespace Atap.Core.Scheduling;

/// <summary>
/// One timer for every scheduled item. Items wait in a min-heap by due time, and a single one-shot timer
/// is armed for the next wake. Each wake runs every item due by then. With no items no timer exists.
/// </summary>
/// <remarks>
/// <para>
/// Coalescing: the wake is armed for the earliest due time plus slack, so an item with slack waits for a
/// wake that comes anyway, such as the minute tick. Items due within <see cref="CoalesceWindow"/> after that
/// join the same wake, so a reminder at 07:00:00 and the minute tick at 07:00:00.020 make one wake.
/// </para>
/// <para>
/// A one-shot item never runs before its due time. Repeating items and aligned ticks may run up to
/// <see cref="EarlyAllowance"/> early, because a system timer can fire a few milliseconds before the wall
/// clock reaches its time, and a second wake for those milliseconds would be wasted. Aligned ticks still
/// never run before their boundary.
/// </para>
/// <para>Callbacks are posted to <c>ui</c>, or run on the timer thread when it is null (tests).</para>
/// </remarks>
public sealed class Scheduler : IScheduler, ISchedulerControl, IDisposable
{
    public static readonly TimeSpan MinimumPeriod = TimeSpan.FromSeconds(1);

    /// <summary>Longer periods are cut to this, so period arithmetic can never overflow.</summary>
    public static readonly TimeSpan MaximumPeriod = TimeSpan.FromDays(366);

    /// <summary>
    /// The longest single timer. System timers refuse delays over about 49.7 days, so an item due later
    /// arms for this long, and the scheduler arms again when that timer fires.
    /// </summary>
    public static readonly TimeSpan MaximumTimerDelay = TimeSpan.FromDays(1);

    /// <summary>Aligned ticks land this long after the boundary, so they never run just before it.</summary>
    public static readonly TimeSpan TickOffset = TimeSpan.FromMilliseconds(20);

    /// <summary>Items due this soon after the planned wake join it. None runs more than this later than it would alone.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(20);

    /// <summary>How early repeating items and aligned ticks may run. Equal to <see cref="TickOffset"/>, so a tick never runs before its boundary.</summary>
    public static readonly TimeSpan EarlyAllowance = TickOffset;

    /// <summary>
    /// After sleep, missed <see cref="MissedRun.Skip"/> items run once this long after resume, plus up to
    /// <see cref="ResumeJitter"/>. Items with slack then join the next wake inside their slack, usually the minute
    /// tick, so in practice they run within about a minute of resume.
    /// </summary>
    public static readonly TimeSpan ResumeRefreshDelay = TimeSpan.FromSeconds(10);

    /// <summary>One random delay per resume, shared by every missed item so they still make one wake.</summary>
    public static readonly TimeSpan ResumeJitter = TimeSpan.FromSeconds(5);

    /// <summary>The most slack <see cref="RefreshOptions"/> gives.</summary>
    public static readonly TimeSpan MaximumRefreshSlack = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan WakeWindow = TimeSpan.FromHours(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _ui;
    private readonly object _gate = new();
    private readonly IndexedHeap<Item> _heap = new(Comparer<Item>.Create(CompareDue));
    private readonly Queue<DateTimeOffset> _wakes = new();
    private readonly List<Item> _due = [];
    private readonly List<Item> _notYet = [];
    private ITimer? _timer;
    private DateTimeOffset? _armedFor;
    private long _generation;
    private long _sequence;
    private bool _saverActive;
    private bool _running;
    private bool _runAgain;
    private volatile bool _disposed;

    public Scheduler(TimeProvider time, SynchronizationContext? ui = null)
    {
        _time = time;
        _ui = ui;
    }

    public event EventHandler<SchedulerFault>? JobFailed;

    /// <summary>
    /// While true, <see cref="ScheduleOptions.Deferrable"/> periods are <see cref="BatterySaverPolicy.Multiplier"/>
    /// times longer. The app follows <c>IPowerState.SaverActive</c> here. A change applies at once: a waiting
    /// item is re-timed from the start of its current period, and one that is overdue once saver ends runs now.
    /// </summary>
    public bool SaverActive
    {
        get
        {
            lock (_gate)
                return _saverActive;
        }
        set
        {
            lock (_gate)
            {
                if (_saverActive == value || _disposed)
                {
                    _saverActive = value;
                    return;
                }
                _saverActive = value;
                var now = _time.GetUtcNow();
                for (var i = 0; i < _heap.Count; i++)
                {
                    var item = _heap[i];
                    if (item.Kind != ItemKind.Repeat || !item.Options.Deferrable || item.PeriodStart is not { } start)
                        continue;
                    var due = Later(start, EffectivePeriod(item));
                    item.Due = due < now ? now : due;
                }
                _heap.Rebuild();
            }
            Arm();
        }
    }

    public SchedulerStats Stats
    {
        get
        {
            lock (_gate)
            {
                PruneWakes(_time.GetUtcNow());
                return new SchedulerStats(_heap.Count, _wakes.Count);
            }
        }
    }

    /// <summary>
    /// Options for a background refresh every <paramref name="period"/>: slack of 10 % of the period, at most
    /// <see cref="MaximumRefreshSlack"/>, and stretched under battery saver. Never for times the user expects.
    /// </summary>
    public static ScheduleOptions RefreshOptions(TimeSpan period, MissedRun missed = MissedRun.Skip) =>
        new(Slack: RefreshSlack(period), Deferrable: true, Missed: missed);

    /// <summary>10 % of <paramref name="period"/>, at most <see cref="MaximumRefreshSlack"/>.</summary>
    public static TimeSpan RefreshSlack(TimeSpan period)
    {
        if (period <= TimeSpan.Zero)
            return TimeSpan.Zero;
        var slack = period / 10;
        return slack > MaximumRefreshSlack ? MaximumRefreshSlack : slack;
    }

    public IDisposable At(DateTimeOffset due, Action callback, ScheduleOptions options = default) =>
        Add(new Item(this, ItemKind.Once, callback, options) { Due = due });

    public IDisposable Every(TimeSpan period, Action callback, ScheduleOptions options = default)
    {
        var clamped = period < MinimumPeriod ? MinimumPeriod : period > MaximumPeriod ? MaximumPeriod : period;
        return Add(new Item(this, ItemKind.Repeat, callback, options) { Period = clamped });
    }

    public IDisposable EveryMinute(Action callback) => Add(new Item(this, ItemKind.Minute, callback, default));

    public IDisposable EverySecond(Action callback) => Add(new Item(this, ItemKind.Second, callback, default));

    /// <summary>
    /// Aligned ticks run at once, so wall-clock text catches up, and then return to their boundaries. A
    /// repeating item never waits longer than one period from now, so a clock set back does not hold it up.
    /// <see cref="At"/> items keep their wall-clock time, and the timer is armed again for it.
    /// </summary>
    public void NotifyClockChanged()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var now = _time.GetUtcNow();
            for (var i = 0; i < _heap.Count; i++)
            {
                var item = _heap[i];
                if (item.Kind == ItemKind.Repeat)
                    PullBack(item, now);
                else if (item.IsAligned)
                    item.Due = now;
            }
            _heap.Rebuild();
            _armedFor = null;
        }
        Arm();
    }

    /// <summary>
    /// Missed items follow their <see cref="MissedRun"/> policy. <see cref="MissedRun.RunOnce"/> items run on the
    /// next turn. Repeating <see cref="MissedRun.Skip"/> items run once <see cref="ResumeRefreshDelay"/> plus
    /// jitter after resume, all in one wake, then carry on one period after that. One-time Skip items are dropped,
    /// so a reminder for a time that passed during sleep never shows up late. Aligned ticks run at once.
    /// The clock may have changed during sleep too, so repeating items are pulled back as in
    /// <see cref="NotifyClockChanged"/>.
    /// </summary>
    public void NotifyResumed()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            var now = _time.GetUtcNow();
            var refreshAt = now + ResumeRefreshDelay + TimeSpan.FromTicks(Random.Shared.NextInt64(ResumeJitter.Ticks));
            List<Item>? dropped = null;
            for (var i = 0; i < _heap.Count; i++)
            {
                var item = _heap[i];
                var missed = item.Due <= now;
                if (item.IsAligned)
                {
                    item.Due = now;
                }
                else if (missed && item.Options.Missed == MissedRun.Skip && item.Kind == ItemKind.Once)
                {
                    (dropped ??= []).Add(item); // No next slot.
                }
                else if (missed && item.Options.Missed == MissedRun.Skip)
                {
                    item.Due = refreshAt;
                    item.PeriodStart = null; // A one-off time, so a saver change leaves it alone.
                }
                else if (!missed && item.Kind == ItemKind.Repeat)
                {
                    PullBack(item, now);
                }
                // Missed RunOnce items keep their past due time and run on the next turn.
            }
            if (dropped is not null)
            {
                foreach (var item in dropped)
                    _heap.Remove(item);
            }
            _heap.Rebuild();
            _armedFor = null;
        }
        Arm();
    }

    public void Dispose()
    {
        ITimer? timer;
        lock (_gate)
        {
            _disposed = true;
            _heap.Clear();
            _wakes.Clear();
            _armedFor = null;
            _generation++;
            timer = _timer;
            _timer = null;
        }
        timer?.Dispose();
    }

    private static int CompareDue(Item a, Item b)
    {
        var byDue = a.Due.CompareTo(b.Due);
        return byDue != 0 ? byDue : a.Sequence.CompareTo(b.Sequence);
    }

    private Item Add(Item item)
    {
        ArgumentNullException.ThrowIfNull(item.Callback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var now = _time.GetUtcNow();
            item.Sequence = ++_sequence;
            switch (item.Kind)
            {
                case ItemKind.Repeat:
                    item.PeriodStart = now;
                    item.Due = Later(now, EffectivePeriod(item));
                    break;
                case ItemKind.Minute or ItemKind.Second:
                    item.Due = NextBoundary(now, item.Unit);
                    break;
            }
            _heap.Add(item);
        }
        Arm();
        return item;
    }

    private void Remove(Item item)
    {
        bool removed;
        lock (_gate)
            removed = _heap.Remove(item);
        if (removed)
            Arm();
    }

    /// <summary>
    /// Arms the one timer for the next wake, or drops it when nothing is left. Each arm makes a new one-shot
    /// timer and a newer generation retires the older one, so a timer that fires while it is being replaced
    /// never runs twice. The same wake keeps the timer it has.
    /// </summary>
    private void Arm()
    {
        ITimer? old;
        TimeSpan delay;
        long generation;

        lock (_gate)
        {
            if (_disposed || _running)
                return; // RunDue arms once it is done.

            old = _timer;
            _timer = null;
            if (_heap.Count == 0)
            {
                _armedFor = null;
                _generation++;
                delay = Timeout.InfiniteTimeSpan;
                generation = -1;
            }
            else
            {
                var wake = NextWake();
                if (old is not null && _armedFor == wake)
                {
                    _timer = old;
                    return;
                }
                _armedFor = wake;
                generation = ++_generation;
                delay = wake - _time.GetUtcNow();
                if (delay < TimeSpan.Zero)
                    delay = TimeSpan.Zero;
                else if (delay > MaximumTimerDelay)
                    delay = MaximumTimerDelay; // Fires early with nothing due, then arms again.
            }
        }

        // Timer calls happen outside the lock: a fake clock may fire the callback right here.
        old?.Dispose();
        if (generation < 0)
            return;

        var timer = _time.CreateTimer(_ => OnTimer(generation), null, delay, Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            if (generation == _generation && !_disposed && _timer is null)
            {
                _timer = timer;
                return;
            }
        }
        // Already fired and re-armed, or replaced.
        timer.Dispose();
    }

    /// <summary>
    /// The earliest due time plus slack, moved later to the last due time within <see cref="CoalesceWindow"/>
    /// after it, so items that close together share the wake. Called under the lock with items waiting.
    /// </summary>
    private DateTimeOffset NextWake()
    {
        var latest = EarliestFireTime(0, FireTime(_heap[0]));
        return LatestDueBy(0, Later(latest, CoalesceWindow), latest);
    }

    /// <summary>The smallest due time plus slack. Only an item due before the best so far can beat it, so the walk stops at later slots.</summary>
    private DateTimeOffset EarliestFireTime(int index, DateTimeOffset best)
    {
        if (index >= _heap.Count)
            return best;
        var item = _heap[index];
        if (item.Due >= best)
            return best;
        var fire = FireTime(item);
        if (fire < best)
            best = fire;
        best = EarliestFireTime((2 * index) + 1, best);
        return EarliestFireTime((2 * index) + 2, best);
    }

    /// <summary>The latest due time no later than <paramref name="limit"/>, or <paramref name="best"/> when none is later.</summary>
    private DateTimeOffset LatestDueBy(int index, DateTimeOffset limit, DateTimeOffset best)
    {
        if (index >= _heap.Count)
            return best;
        var item = _heap[index];
        if (item.Due > limit)
            return best;
        if (item.Due > best)
            best = item.Due;
        best = LatestDueBy((2 * index) + 1, limit, best);
        return LatestDueBy((2 * index) + 2, limit, best);
    }

    private void OnTimer(long generation)
    {
        lock (_gate)
        {
            if (generation != _generation)
                return;
        }

        if (_ui is null)
            RunDue();
        else
            _ui.Post(_ => RunDue(), null);
    }

    private void RunDue()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_running)
            {
                _runAgain = true;
                return;
            }
            _running = true;
            _armedFor = null;
        }

        try
        {
            var countedWake = false;
            while (true)
            {
                lock (_gate)
                {
                    _runAgain = false;
                    var now = _time.GetUtcNow();
                    if (!countedWake)
                    {
                        _wakes.Enqueue(now);
                        PruneWakes(now);
                        countedWake = true;
                    }
                    TakeDue(now);
                }

                foreach (var item in _due)
                {
                    if (item.IsCancelled || _disposed)
                        continue;
                    try
                    {
                        item.Callback();
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            JobFailed?.Invoke(this, new SchedulerFault(ex));
                        }
                        catch
                        {
                            // A failing error handler must not stop the items still due in this wake.
                        }
                    }
                }
                _due.Clear();

                lock (_gate)
                {
                    if (!_runAgain || _disposed)
                        break;
                }
            }
        }
        finally
        {
            _due.Clear();
            lock (_gate)
                _running = false;
            Arm();
        }
    }

    /// <summary>
    /// Moves every item that may run at <paramref name="now"/> into the due list, in due order. Repeating items
    /// and ticks go back into the heap for their next time before any callback runs, so a callback can still
    /// cancel them. Called under the lock.
    /// </summary>
    private void TakeDue(DateTimeOffset now)
    {
        var horizon = Later(now, EarlyAllowance);
        while (_heap.Peek() is { } top && top.Due <= horizon)
        {
            _heap.Pop();
            (top.Due - top.Early <= now ? _due : _notYet).Add(top);
        }

        foreach (var item in _notYet)
            _heap.Add(item);
        _notYet.Clear();

        foreach (var item in _due)
        {
            if (item.Kind == ItemKind.Once)
                continue;
            Reschedule(item, now);
            _heap.Add(item);
        }
    }

    private void Reschedule(Item item, DateTimeOffset now)
    {
        if (item.Kind == ItemKind.Repeat)
        {
            var period = EffectivePeriod(item);
            var next = Later(item.Due, period);
            if (next <= now)
                next = Later(now, period); // Fell behind: skip the slots that were missed.
            item.Due = next;
            item.PeriodStart = next - period;
        }
        else
        {
            // From the slot just served, plus the tick offset, so a tick that ran a little early, or at once after a
            // clock change just past a boundary, does not run again for the same boundary.
            item.Due = NextBoundary(Later(item.Due > now ? item.Due : now, TickOffset), item.Unit);
        }
    }

    /// <summary>A repeating item due more than one period from now (the clock went back) runs one period from now.</summary>
    private void PullBack(Item item, DateTimeOffset now)
    {
        var next = Later(now, EffectivePeriod(item));
        if (item.Due > next)
        {
            item.Due = next;
            item.PeriodStart = now;
        }
    }

    /// <summary>Due plus slack, stopping at the end of the calendar instead of overflowing.</summary>
    private static DateTimeOffset FireTime(Item item) => Later(item.Due, item.Slack);

    private static DateTimeOffset Later(DateTimeOffset time, TimeSpan by) =>
        time > DateTimeOffset.MaxValue - by ? DateTimeOffset.MaxValue : time + by;

    private TimeSpan EffectivePeriod(Item item) =>
        item.Options.Deferrable && _saverActive ? item.Period * BatterySaverPolicy.Multiplier : item.Period;

    /// <summary>
    /// The first boundary after <paramref name="now"/>, plus <see cref="TickOffset"/>. Every time zone is a
    /// whole number of minutes from UTC, so a UTC minute boundary is a local one too.
    /// </summary>
    private static DateTimeOffset NextBoundary(DateTimeOffset now, TimeSpan unit)
    {
        var utc = now.ToUniversalTime();
        var start = new DateTimeOffset(utc.Ticks - (utc.Ticks % unit.Ticks), TimeSpan.Zero);
        var next = start + TickOffset;
        return next > utc ? next : Later(start, unit + TickOffset);
    }

    private void PruneWakes(DateTimeOffset now)
    {
        while (_wakes.Count > 0 && now - _wakes.Peek() > WakeWindow)
            _wakes.Dequeue();
    }

    private enum ItemKind
    {
        Once,
        Repeat,
        Minute,
        Second,
    }

    private sealed class Item(Scheduler owner, ItemKind kind, Action callback, ScheduleOptions options) : IDisposable, IHeapItem
    {
        private volatile bool _cancelled;

        public ItemKind Kind { get; } = kind;

        public Action Callback { get; } = callback;

        public ScheduleOptions Options { get; } = options;

        /// <summary>How late the item may run. Negative slack counts as none.</summary>
        public TimeSpan Slack { get; } = options.Slack > TimeSpan.Zero ? options.Slack : TimeSpan.Zero;

        public bool IsAligned => Kind is ItemKind.Minute or ItemKind.Second;

        /// <summary>The boundary an aligned tick follows.</summary>
        public TimeSpan Unit => Kind == ItemKind.Minute ? Minute : Second;

        /// <summary>How early the item may run. One-shot items never run early.</summary>
        public TimeSpan Early => Kind == ItemKind.Once ? TimeSpan.Zero : EarlyAllowance;

        public DateTimeOffset Due { get; set; }

        /// <summary>The order items were added in, for items due at the same moment.</summary>
        public long Sequence { get; set; }

        public TimeSpan Period { get; init; }

        /// <summary>
        /// Where the current wait of a repeating item started, so a saver change can re-time it. Null while the
        /// due time is a one-off, such as the refresh after resume.
        /// </summary>
        public DateTimeOffset? PeriodStart { get; set; }

        public int HeapIndex { get; set; } = -1;

        public bool IsCancelled => _cancelled;

        public void Dispose()
        {
            if (_cancelled)
                return;
            _cancelled = true;
            owner.Remove(this);
        }
    }
}
