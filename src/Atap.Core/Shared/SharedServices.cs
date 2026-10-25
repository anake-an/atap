using Atap.Core.Scheduling;

namespace Atap.Core.Shared;

/// <summary>
/// Reference-counted shared sources. A source starts with its first lease. After the last lease ends
/// it waits <see cref="GracePeriod"/> (one scheduler item) before it is disposed, so a quick
/// release and acquire does not restart it. Not thread-safe: use it on the UI thread.
/// </summary>
public sealed class SharedServices : ISharedServices, IDisposable
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    private readonly SynchronizationContext _ui;
    private readonly TimeProvider _time;
    private readonly IScheduler _scheduler;
    private readonly Dictionary<Type, Entry> _entries = [];
    private bool _disposed;

    public SharedServices(SynchronizationContext ui, TimeProvider time, IScheduler scheduler)
    {
        _ui = ui;
        _time = time;
        _scheduler = scheduler;
    }

    public int ActiveCount => _entries.Count;

    /// <summary>Leases held right now, over every source.</summary>
    public int LeaseCount => _entries.Values.Sum(e => e.Leases);

    /// <summary>Raised when a source fails to dispose. The app logs it.</summary>
    public event EventHandler<Exception>? DisposeFailed;

    public Lease<T> Acquire<T>()
        where T : class, ISharedService<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_entries.TryGetValue(typeof(T), out var entry))
        {
            // Created before it is stored, so a Create that throws leaves nothing behind.
            var created = T.Create(new SharedServiceContext(_ui, _time, _scheduler, this));
            entry = new Entry(created);
            _entries[typeof(T)] = entry;
        }

        entry.Grace?.Dispose();
        entry.Grace = null;
        entry.Leases++;
        return new Lease<T>((T)entry.Service, () => Release(typeof(T), entry));
    }

    /// <summary>Disposes every source at once, grace periods included. Used at shutdown.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var entry in _entries.Values.ToList())
        {
            entry.Grace?.Dispose();
            DisposeService(entry);
        }
        _entries.Clear();
    }

    private void Release(Type type, Entry entry)
    {
        if (_disposed || !_entries.TryGetValue(type, out var current) || current != entry)
            return;
        entry.Leases--;
        if (entry.Leases > 0)
            return;

        entry.Grace = _scheduler.At(_time.GetUtcNow() + GracePeriod, () =>
        {
            entry.Grace = null;
            if (entry.Leases > 0 || !_entries.TryGetValue(type, out var stillCurrent) || stillCurrent != entry)
                return;
            _entries.Remove(type);
            DisposeService(entry);
        }, ReleaseAfterSleep);
    }

    /// <summary>A release missed during sleep still happens after wake, or the service would stay open.</summary>
    private static readonly ScheduleOptions ReleaseAfterSleep = new(Missed: MissedRun.RunOnce);

    private void DisposeService(Entry entry)
    {
        try
        {
            entry.Service.Dispose();
        }
        catch (Exception ex)
        {
            DisposeFailed?.Invoke(this, ex);
        }
    }

    private sealed class Entry(IDisposable service)
    {
        public IDisposable Service { get; } = service;

        public int Leases { get; set; }

        public IDisposable? Grace { get; set; }
    }
}
