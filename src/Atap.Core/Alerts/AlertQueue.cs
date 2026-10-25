namespace Atap.Core.Alerts;

/// <summary>
/// Holds the current alert, the waiting ones and the Missed list. One alert shows at a time, picked by
/// priority, for as long as its priority gives it. Not thread-safe: use it on the UI thread.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Overlay 1.5 s, Normal 4 s, High 8 s, Critical until dismissed and at most 60 s.
///   <see cref="Alert.Duration"/> overrides this, between 1 and 60 seconds.</item>
/// <item>A new alert with the same source and key replaces the old one, on screen or waiting.</item>
/// <item>A more urgent alert takes over the screen. The one it replaced waits for its turn.</item>
/// <item>An overlay never waits: it shows at once, or it is dropped when something more urgent is on
///   screen, because it would be out of date by its turn.</item>
/// <item>Quiet (a rule, Windows quiet hours): Normal and High alerts go to the Missed list, overlays
///   are dropped and Critical alerts still show. Windows quiet hours are read only when an alert is about to show.</item>
/// <item>Hidden (a full-screen app, a hide rule): the same, and Critical alerts wait until the notch is
///   visible again. Their sound is due when they arrive.</item>
/// <item>The push source gets 1 alert per 5 seconds, with a burst of 3. Alerts over the limit are dropped.</item>
/// </list>
/// The only timer is a one-shot for the end of the current alert. While nothing shows, there is none.
/// </remarks>
public sealed class AlertQueue : IAlertQueue
{
    public const int MissedLimit = 20;

    /// <summary>The push widget's type id. Its alerts are rate limited with <see cref="AlertRateLimit.Push"/>.</summary>
    public const string PushSource = "push";

    public static readonly TimeSpan MinDuration = TimeSpan.FromSeconds(1);

    /// <summary>The longest any alert shows, Critical included, unless it is kept with <see cref="Keep"/>.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    private readonly TimeProvider _time;
    private readonly SynchronizationContext? _ui;
    private readonly List<Entry> _waiting = [];
    private readonly List<ShownAlert> _missed = [];
    private readonly IReadOnlyList<ShownAlert> _missedView;
    private readonly HashSet<string> _quietOwners = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hiddenOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AlertRateLimit> _limits = new(StringComparer.Ordinal) { [PushSource] = AlertRateLimit.Push };
    private readonly Dictionary<string, AlertRateBucket> _buckets = new(StringComparer.Ordinal);
    private readonly List<Hold> _holds = [];
    private readonly List<ShownAlert> _soundsDue = [];
    private readonly List<ShownAlert> _dismissed = [];
    private Entry? _current;
    private ITimer? _timer;
    private long _timerGeneration;
    private int _depth;
    private bool _changed;
    private bool _missedChanged;

    /// <summary>Create it on the UI thread: the end of an alert is handled on the thread it was created on.</summary>
    public AlertQueue(TimeProvider time)
        : this(time, SynchronizationContext.Current)
    {
    }

    /// <param name="time">The clock and the one-shot timer.</param>
    /// <param name="ui">Where the timer's work runs. Null runs it on the timer's own thread, which only tests should do.</param>
    public AlertQueue(TimeProvider time, SynchronizationContext? ui)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _ui = ui;
        _missedView = _missed.AsReadOnly();
    }

    public ShownAlert? Current => _current?.Shown;

    /// <summary>When the current alert goes. Null while nothing shows.</summary>
    public DateTimeOffset? CurrentEndsAt => _current?.EndsAt;

    public IReadOnlyList<ShownAlert> Missed => _missedView;

    /// <summary>Alerts waiting for their turn, most urgent first.</summary>
    public IReadOnlyList<ShownAlert> Waiting => [.. Ordered(_waiting).Select(e => e.Shown)];

    /// <summary>True while a rule or a feature asks for quiet. Windows quiet hours are not included: see <see cref="SystemQuiet"/>.</summary>
    public bool IsQuiet => _quietOwners.Count > 0;

    /// <summary>True while the notch is hidden, for example by a full-screen app.</summary>
    public bool IsHidden => _hiddenOwners.Count > 0;

    /// <summary>True while the one-shot timer for the current alert exists.</summary>
    public bool HasTimer => _timer is not null;

    /// <summary>
    /// Asks Windows whether its quiet hours (Do not disturb, Focus) are on. Called only when a Normal,
    /// High or Overlay alert is about to show, never on a timer. An exception counts as not quiet.
    /// </summary>
    public Func<bool>? SystemQuiet { get; set; }

    public event EventHandler? Changed;

    /// <summary>Raised on the UI thread when <see cref="Missed"/> changes.</summary>
    public event EventHandler? MissedChanged;

    /// <summary>
    /// Raised on the UI thread when an alert's sound should play: when a Normal, High or Overlay alert
    /// first shows, and when a Critical alert arrives, even while it waits for a hidden notch. Once per alert.
    /// </summary>
    public event EventHandler<ShownAlert>? SoundDue;

    /// <summary>
    /// Raised on the UI thread when an alert on screen or waiting is dismissed by its source or by the
    /// user. Not raised when it times out, is replaced or goes to Missed.
    /// </summary>
    public event EventHandler<ShownAlert>? Dismissed;

    /// <summary>How long an alert of <paramref name="priority"/> shows when it sets no <see cref="Alert.Duration"/>.</summary>
    public static TimeSpan DefaultDuration(AlertPriority priority) => priority switch
    {
        AlertPriority.Overlay => TimeSpan.FromSeconds(1.5),
        AlertPriority.Normal => TimeSpan.FromSeconds(4),
        AlertPriority.High => TimeSpan.FromSeconds(8),
        _ => MaxDuration,
    };

    /// <summary>How long <paramref name="alert"/> shows: its own duration between 1 and 60 seconds, or the default.</summary>
    public static TimeSpan DurationOf(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        if (alert.Duration is not { } duration)
            return DefaultDuration(alert.Priority);
        return duration < MinDuration ? MinDuration : duration > MaxDuration ? MaxDuration : duration;
    }

    public IAlertSink For(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new Sink(this, source);
    }

    public void SetQuiet(string ownerId, bool quiet)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var changed = quiet ? _quietOwners.Add(ownerId) : _quietOwners.Remove(ownerId);
        if (!changed || IsQuiet)
            return;
        Run(Promote);
    }

    /// <summary>
    /// Hidden while any owner says so. When the notch hides, the alert on screen and the waiting ones go
    /// to Missed (overlays are dropped), and Critical alerts wait. When it shows again, they show.
    /// </summary>
    public void SetHidden(string ownerId, bool hidden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        var wasHidden = IsHidden;
        var changed = hidden ? _hiddenOwners.Add(ownerId) : _hiddenOwners.Remove(ownerId);
        if (!changed || IsHidden == wasHidden)
            return;
        Run(() =>
        {
            if (!IsHidden)
            {
                Promote();
                return;
            }
            if (_current is not null)
                _waiting.Add(TakeCurrent());
            DivertAllButCritical();
        });
    }

    public void ClearMissed()
    {
        if (_missed.Count == 0)
            return;
        Run(() =>
        {
            _missed.Clear();
            _missedChanged = true;
        });
    }

    /// <summary>The user closed the alert on screen. The next one shows.</summary>
    public void DismissCurrent()
    {
        if (_current is null)
            return;
        Run(() =>
        {
            _dismissed.Add(TakeCurrent().Shown);
            Promote();
        });
    }

    /// <summary>True while the alert is on screen or waiting.</summary>
    public bool Contains(string source, string key) =>
        _current?.Is(source, key) == true || _waiting.Exists(w => w.Is(source, key));

    /// <summary>
    /// Keeps the alert with this source and key from timing out, for example while its sound plays so the
    /// Stop button stays. Dispose to let it go. If its time is up by then, it goes at once.
    /// </summary>
    public IDisposable Keep(string source, string key)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        var hold = new Hold(this, source, key);
        _holds.Add(hold);
        if (_current?.Is(source, key) == true)
            CancelTimer();
        return hold;
    }

    /// <summary>
    /// Limits every source of a type (the part of the source before "#"). Null removes the limit.
    /// The push source is limited from the start.
    /// </summary>
    public void SetRateLimit(string sourceType, AlertRateLimit? limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceType);
        if (limit is null)
        {
            _limits.Remove(sourceType);
        }
        else
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(limit.Burst, 1, nameof(limit));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit.Interval, TimeSpan.Zero, nameof(limit));
            _limits[sourceType] = limit;
        }
        foreach (var source in _buckets.Keys.Where(s => TypeOf(s) == sourceType).ToList())
            _buckets.Remove(source);
    }

    private void Show(string source, Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);
        ArgumentNullException.ThrowIfNull(alert.Key);
        var now = _time.GetUtcNow();
        if (!TryTakeToken(source, now))
            return;

        Run(() =>
        {
            var entry = new Entry(new ShownAlert(source, alert, now));
            if (alert.Priority == AlertPriority.Critical)
                SoundNow(entry);

            // The same alert again: it takes the old one's place on screen, with a fresh time. In quiet hours
            // an update to a notice goes to Missed like any other notice.
            if (_current?.Is(source, alert.Key) == true)
            {
                if (alert.Priority != AlertPriority.Critical && QuietNow())
                {
                    TakeCurrent();
                    Divert(entry);
                    Promote();
                    return;
                }
                ShowNow(entry);
                Promote();
                return;
            }
            _waiting.RemoveAll(w => w.Is(source, alert.Key));

            if (alert.Priority == AlertPriority.Overlay)
            {
                if (IsHidden || _current is { Priority: > AlertPriority.Overlay } || QuietNow())
                    return;
                if (_current is not null)
                    TakeCurrent();
                ShowNow(entry);
                return;
            }

            _waiting.Add(entry);
            Promote();
        });
    }

    private void Dismiss(string source, string key)
    {
        if (!Contains(source, key))
            return;
        Run(() =>
        {
            foreach (var waiting in _waiting.Where(w => w.Is(source, key)).ToList())
            {
                _waiting.Remove(waiting);
                _dismissed.Add(waiting.Shown);
            }
            if (_current?.Is(source, key) == true)
            {
                _dismissed.Add(TakeCurrent().Shown);
                Promote();
            }
        });
    }

    /// <summary>Puts the most urgent waiting alert on screen when it outranks the current one.</summary>
    private void Promote()
    {
        bool? quiet = null;
        while (Best() is { } next)
        {
            if (_current is { } current && next.Priority <= current.Priority)
                break;
            if (IsHidden)
            {
                DivertAllButCritical();
                break;
            }
            _waiting.Remove(next);
            if (next.Priority != AlertPriority.Critical && (quiet ??= QuietNow()))
            {
                Divert(next);
                continue;
            }

            // The one it takes over from waits for its turn. An overlay would be out of date by then.
            if (_current is not null)
            {
                var replaced = TakeCurrent();
                if (replaced.Priority != AlertPriority.Overlay)
                    _waiting.Add(replaced);
            }
            ShowNow(next);
        }
    }

    private void ShowNow(Entry entry)
    {
        _current = entry;
        entry.Duration = DurationOf(entry.Shown.Alert);
        entry.ShownTimestamp = _time.GetTimestamp();
        entry.EndsAt = _time.GetUtcNow() + entry.Duration;
        SoundNow(entry);
        _changed = true;
        ArmTimer();
    }

    /// <summary>Takes the alert off the screen without deciding where it goes.</summary>
    private Entry TakeCurrent()
    {
        var current = _current!;
        _current = null;
        current.EndsAt = null;
        CancelTimer();
        _changed = true;
        return current;
    }

    private void SoundNow(Entry entry)
    {
        if (entry.Sounded || string.IsNullOrWhiteSpace(entry.Shown.Alert.SoundKey))
            return;
        entry.Sounded = true;
        _soundsDue.Add(entry.Shown);
    }

    /// <summary>While hidden nothing shows: Critical alerts wait, Normal and High go to Missed, overlays are dropped.</summary>
    private void DivertAllButCritical()
    {
        var blocked = _waiting.Where(w => w.Priority != AlertPriority.Critical).OrderBy(w => w.Shown.ReceivedAt).ToList();
        foreach (var entry in blocked)
        {
            _waiting.Remove(entry);
            Divert(entry);
        }
    }

    private void Divert(Entry entry)
    {
        if (entry.Priority is AlertPriority.Overlay or AlertPriority.Critical)
            return;

        var shown = entry.Shown;
        _missed.RemoveAll(m => string.Equals(m.Source, shown.Source, StringComparison.Ordinal)
            && string.Equals(m.Alert.Key, shown.Alert.Key, StringComparison.Ordinal));
        var index = _missed.FindIndex(m => m.ReceivedAt <= shown.ReceivedAt);
        _missed.Insert(index < 0 ? _missed.Count : index, shown);
        if (_missed.Count > MissedLimit)
            _missed.RemoveRange(MissedLimit, _missed.Count - MissedLimit);
        _missedChanged = true;
    }

    private bool QuietNow()
    {
        if (IsQuiet)
            return true;
        if (SystemQuiet is not { } probe)
            return false;
        try
        {
            return probe();
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Entry? Best()
    {
        Entry? best = null;
        foreach (var entry in _waiting)
        {
            if (best is null
                || entry.Priority > best.Priority
                || (entry.Priority == best.Priority && entry.Shown.ReceivedAt < best.Shown.ReceivedAt))
            {
                best = entry;
            }
        }
        return best;
    }

    private static IEnumerable<Entry> Ordered(IEnumerable<Entry> entries) =>
        entries.OrderByDescending(e => e.Priority).ThenBy(e => e.Shown.ReceivedAt);

    private bool IsHeld(Entry entry) => _holds.Exists(h => entry.Is(h.Source, h.Key));

    private void Release(Hold hold)
    {
        if (!_holds.Remove(hold) || _current is not { } current || !current.Is(hold.Source, hold.Key) || IsHeld(current))
            return;
        if (Remaining(current) > TimeSpan.Zero)
        {
            ArmTimer();
            return;
        }
        Run(() =>
        {
            TakeCurrent();
            Promote();
        });
    }

    private TimeSpan Remaining(Entry entry) => entry.Duration - _time.GetElapsedTime(entry.ShownTimestamp);

    private void ArmTimer()
    {
        CancelTimer();
        if (_current is not { EndsAt: not null } current || IsHeld(current))
            return;
        var due = Remaining(current);
        var generation = ++_timerGeneration;
        _timer = _time.CreateTimer(
            static state =>
            {
                var (queue, generation) = ((AlertQueue, long))state!;
                queue.OnTimer(generation);
            },
            (this, generation),
            due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1),
            Timeout.InfiniteTimeSpan);
    }

    private void CancelTimer()
    {
        if (_timer is null)
            return;
        _timer.Dispose();
        _timer = null;
        _timerGeneration++;
    }

    // Runs on the timer's thread.
    private void OnTimer(long generation)
    {
        if (_ui is null)
        {
            Expire(generation);
            return;
        }
        _ui.Post(
            static state =>
            {
                var (queue, generation) = ((AlertQueue, long))state!;
                queue.Expire(generation);
            },
            (this, generation));
    }

    private void Expire(long generation)
    {
        // A timer that was cancelled or re-armed after it fired.
        if (generation != _timerGeneration)
            return;
        Run(() =>
        {
            CancelTimer();
            if (_current is not { } current || IsHeld(current))
                return;
            if (Remaining(current) > TimeSpan.Zero)
            {
                ArmTimer();
                return;
            }
            TakeCurrent();
            Promote();
        });
    }

    private bool TryTakeToken(string source, DateTimeOffset now)
    {
        if (!_limits.TryGetValue(TypeOf(source), out var limit))
            return true;
        if (!_buckets.TryGetValue(source, out var bucket) || bucket.Limit != limit)
        {
            bucket = new AlertRateBucket(limit, now);
            _buckets[source] = bucket;
        }
        return bucket.TryTake(now);
    }

    /// <summary>The type of a source id: the part before '#', so "push#2" is a "push" source.</summary>
    public static string TypeOf(string source)
    {
        var separator = source.IndexOf('#', StringComparison.Ordinal);
        return separator < 0 ? source : source[..separator];
    }

    /// <summary>
    /// Runs a change, then raises the events once it is complete: Dismissed, MissedChanged, Changed and
    /// SoundDue, in that order. A handler that changes the queue again gets its own events.
    /// </summary>
    private void Run(Action change)
    {
        _depth++;
        try
        {
            change();
        }
        finally
        {
            _depth--;
        }
        if (_depth == 0)
            RaiseEvents();
    }

    private void RaiseEvents()
    {
        while (_changed || _missedChanged || _soundsDue.Count > 0 || _dismissed.Count > 0)
        {
            var missedChanged = _missedChanged;
            var changed = _changed || missedChanged;
            _changed = false;
            _missedChanged = false;
            var dismissed = _dismissed.ToArray();
            _dismissed.Clear();
            var sounds = _soundsDue.ToArray();
            _soundsDue.Clear();

            foreach (var alert in dismissed)
                Dismissed?.Invoke(this, alert);
            if (missedChanged)
                MissedChanged?.Invoke(this, EventArgs.Empty);
            if (changed)
                Changed?.Invoke(this, EventArgs.Empty);
            foreach (var alert in sounds)
                SoundDue?.Invoke(this, alert);
        }
    }

    private sealed class Entry(ShownAlert shown)
    {
        public ShownAlert Shown { get; } = shown;

        public AlertPriority Priority => Shown.Alert.Priority;

        /// <summary>Its sound was due already, so it never plays twice.</summary>
        public bool Sounded { get; set; }

        /// <summary>When it goes, on the wall clock, for display. Set while it is on screen.</summary>
        public DateTimeOffset? EndsAt { get; set; }

        /// <summary>When it was shown, on the monotonic clock, so setting the wall clock back cannot keep it up.</summary>
        public long ShownTimestamp { get; set; }

        /// <summary>How long it stays.</summary>
        public TimeSpan Duration { get; set; }

        public bool Is(string source, string key) =>
            string.Equals(Shown.Source, source, StringComparison.Ordinal)
            && string.Equals(Shown.Alert.Key, key, StringComparison.Ordinal);
    }

    private sealed class Hold(AlertQueue queue, string source, string key) : IDisposable
    {
        public string Source { get; } = source;

        public string Key { get; } = key;

        public void Dispose() => queue.Release(this);
    }

    private sealed class Sink(AlertQueue queue, string source) : IAlertSink, IMissedAlerts
    {
        public IReadOnlyList<ShownAlert> Missed => queue.Missed;

        public event EventHandler? MissedChanged
        {
            add => queue.MissedChanged += value;
            remove => queue.MissedChanged -= value;
        }

        public void Show(Alert alert) => queue.Show(source, alert);

        public void Dismiss(string key) => queue.Dismiss(source, key);

        public void ClearMissed() => queue.ClearMissed();
    }
}
