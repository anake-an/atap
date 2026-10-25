using Atap.Core.Commands;
using Atap.Core.Scheduling;
using Atap.Core.Shared;

namespace Atap.Composition;

/// <summary>
/// A scheduler for one owner. Its items end with the owner's lifetime, and a callback that throws is
/// reported to the owner (counting toward Failed) instead of the shared scheduler.
/// </summary>
internal sealed class ScopedScheduler : IScheduler, IDisposable
{
    private readonly IScheduler _inner;
    private readonly OwnerLifetime _lifetime;
    private readonly HashSet<Registration> _items = [];
    private bool _disposed;

    public ScopedScheduler(IScheduler inner, OwnerLifetime lifetime)
    {
        _inner = inner;
        _lifetime = lifetime;
        lifetime.Add(this);
    }

    /// <summary>Items still scheduled by this owner.</summary>
    public int Count => _items.Count;

    public IDisposable At(DateTimeOffset due, Action callback, ScheduleOptions options = default) =>
        Track(registration => _inner.At(due, () =>
        {
            // A one-shot item is done once it ran, so the scope lets go of it.
            _items.Remove(registration);
            Run(callback);
        }, options));

    public IDisposable Every(TimeSpan period, Action callback, ScheduleOptions options = default) =>
        Track(_ => _inner.Every(period, () => Run(callback), options));

    public IDisposable EveryMinute(Action callback) => Track(_ => _inner.EveryMinute(() => Run(callback)));

    public IDisposable EverySecond(Action callback) => Track(_ => _inner.EverySecond(() => Run(callback)));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var item in _items.ToList())
            item.Dispose();
        _items.Clear();
    }

    private Registration Track(Func<Registration, IDisposable> schedule)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var registration = new Registration(this);
        _items.Add(registration);
        registration.Attach(schedule(registration));
        return registration;
    }

    private void Run(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception ex)
        {
            _lifetime.ReportError(ex);
        }
    }

    private sealed class Registration(ScopedScheduler owner) : IDisposable
    {
        private IDisposable? _inner;
        private bool _disposed;

        public void Attach(IDisposable inner)
        {
            // Disposed while it was being scheduled (a callback that cancels itself at once).
            if (_disposed)
                inner.Dispose();
            else
                _inner = inner;
        }

        public void Dispose()
        {
            _disposed = true;
            owner._items.Remove(this);
            _inner?.Dispose();
            _inner = null;
        }
    }
}

/// <summary>
/// A command registry for one owner. Its commands and its <see cref="Changed"/> handlers are removed
/// when the owner stops, so the shared registry never calls or holds a stopped owner.
/// </summary>
internal sealed class ScopedCommands : ICommandRegistry, IDisposable
{
    private readonly ICommandRegistry _inner;
    private readonly HashSet<Registration> _registrations = [];
    private readonly List<EventHandler> _handlers = [];
    private bool _disposed;

    public ScopedCommands(ICommandRegistry inner, OwnerLifetime lifetime)
    {
        _inner = inner;
        lifetime.Add(this);
    }

    public IReadOnlyCollection<AtapCommand> All => _inner.All;

    /// <summary>Handlers added after the owner stopped are ignored.</summary>
    public event EventHandler? Changed
    {
        add
        {
            if (value is null || _disposed)
                return;
            _handlers.Add(value);
            _inner.Changed += value;
        }
        remove
        {
            if (value is null || !_handlers.Remove(value))
                return;
            _inner.Changed -= value;
        }
    }

    public IDisposable Register(AtapCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var registration = new Registration(this, _inner.Register(command));
        _registrations.Add(registration);
        return registration;
    }

    public bool Contains(string id) => _inner.Contains(id);

    public Task<bool> TryRunAsync(string id, string? argument = null) => _inner.TryRunAsync(id, argument);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var handler in _handlers)
            _inner.Changed -= handler;
        _handlers.Clear();
        foreach (var registration in _registrations.ToList())
            registration.Dispose();
        _registrations.Clear();
    }

    private sealed class Registration(ScopedCommands owner, IDisposable inner) : IDisposable
    {
        public void Dispose()
        {
            owner._registrations.Remove(this);
            inner.Dispose();
        }
    }
}

/// <summary>Shared sources for one owner. Its leases end when the owner stops.</summary>
internal sealed class ScopedShared : ISharedServices, IDisposable
{
    private readonly ISharedServices _inner;
    private readonly List<(IDisposable Lease, Func<bool> IsReleased)> _leases = [];
    private bool _disposed;

    public ScopedShared(ISharedServices inner, OwnerLifetime lifetime)
    {
        _inner = inner;
        lifetime.Add(this);
    }

    public int ActiveCount => _inner.ActiveCount;

    /// <summary>Leases this owner still holds.</summary>
    public int LeaseCount => _leases.Count(l => !l.IsReleased());

    public Lease<T> Acquire<T>()
        where T : class, ISharedService<T>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Leases the owner already let go of need no tracking.
        _leases.RemoveAll(l => l.IsReleased());
        var lease = _inner.Acquire<T>();
        _leases.Add((lease, () => lease.IsReleased));
        return lease;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var (lease, _) in _leases)
            lease.Dispose();
        _leases.Clear();
    }
}
