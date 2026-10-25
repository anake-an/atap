using Atap.Core.Scheduling;

namespace Atap.Core.Shared;

/// <summary>
/// A Windows source shared by everything that needs it, for example the media session manager.
/// It is created through <see cref="Create"/> on the first lease and disposed 30 seconds after the last one ends.
/// </summary>
public interface ISharedService<TSelf> : IDisposable
    where TSelf : class, ISharedService<TSelf>
{
    static abstract TSelf Create(SharedServiceContext context);
}

/// <param name="Ui">The UI thread.</param>
/// <param name="Time">The clock.</param>
/// <param name="Scheduler">For the source's own timers.</param>
/// <param name="Shared">For sources built on other sources.</param>
public sealed record SharedServiceContext(SynchronizationContext Ui, TimeProvider Time, IScheduler Scheduler, ISharedServices Shared);

/// <summary>Hands out leases on shared sources. Use it on the UI thread only.</summary>
public interface ISharedServices
{
    Lease<T> Acquire<T>()
        where T : class, ISharedService<T>;

    /// <summary>Sources that exist right now, including those waiting out their grace period.</summary>
    int ActiveCount { get; }
}

/// <summary>A hold on a shared source. Dispose it to let the source go. Disposing twice does nothing.</summary>
public sealed class Lease<T> : IDisposable
    where T : class
{
    private Action? _release;

    internal Lease(T value, Action release)
    {
        Value = value;
        _release = release;
    }

    public T Value { get; }

    public bool IsReleased => _release is null;

    public void Dispose()
    {
        var release = _release;
        _release = null;
        release?.Invoke();
    }
}
