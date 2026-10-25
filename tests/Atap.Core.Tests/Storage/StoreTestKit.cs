using Atap.Core.Storage;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Storage;

/// <summary>A file watch the test fires by hand, so no real file event or timing is involved.</summary>
internal sealed class ManualFileWatch : IFileWatch
{
    private readonly List<Handle> _handles = [];

    /// <summary>Watches that are still open.</summary>
    public int Open => _handles.Count(h => !h.Disposed);

    /// <summary>How many times a watch was asked for.</summary>
    public int Requests { get; private set; }

    public IDisposable? Watch(string filePath, Action<FileChange> changed)
    {
        Requests++;
        var handle = new Handle(changed);
        _handles.Add(handle);
        return handle;
    }

    /// <summary>What the system would report for a change to the file.</summary>
    public void Fire(FileChange change = FileChange.Changed)
    {
        foreach (var handle in _handles.Where(h => !h.Disposed).ToList())
            handle.Changed(change);
    }

    private sealed class Handle(Action<FileChange> changed) : IDisposable
    {
        public Action<FileChange> Changed { get; } = changed;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}

/// <summary>Runs posted work at once on the calling thread, so fake-time tests stay on the test thread.</summary>
internal sealed class InlineContext : SynchronizationContext
{
    public static InlineContext Instance { get; } = new();

    public override void Post(SendOrPostCallback d, object? state) => d(state);
}

/// <summary>A fake clock that also says whether any timer is armed, to prove an idle store sleeps.</summary>
internal sealed class CountingTime(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly List<CountingTimer> _timers = [];

    public int Created => _timers.Count;

    public int Armed => _timers.Count(t => t.IsArmed);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new CountingTimer();
        timer.Set(dueTime, period);
        timer.Inner = base.CreateTimer(
            s =>
            {
                timer.Fired();
                callback(s);
            },
            state,
            dueTime,
            period);
        _timers.Add(timer);
        return timer;
    }

    private sealed class CountingTimer : ITimer
    {
        private bool _armed;
        private bool _repeats;
        private bool _disposed;

        public ITimer Inner { get; set; } = null!;

        public bool IsArmed => _armed && !_disposed;

        public void Set(TimeSpan dueTime, TimeSpan period)
        {
            _armed = dueTime != Timeout.InfiniteTimeSpan;
            _repeats = period != Timeout.InfiniteTimeSpan && period != TimeSpan.Zero;
        }

        public void Fired() => _armed = _repeats;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Set(dueTime, period);
            return Inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            _disposed = true;
            Inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            _disposed = true;
            return Inner.DisposeAsync();
        }
    }
}

/// <summary>A folder under %TEMP%\Atap.Tests, deleted afterwards. Tests never touch the real Atap folders.</summary>
internal sealed class TempFolder : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
