using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>
/// The lifetime of one widget instance, feature or settings card. Resources added here are disposed
/// newest first when it ends. Errors and status go to the owner's host through the callbacks.
/// </summary>
internal sealed class OwnerLifetime : IWidgetLifetime, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<IDisposable> _resources = [];
    private readonly Action<Exception>? _onError;
    private readonly Action<WidgetStatus, string?>? _onStatus;
    private bool _disposed;

    public OwnerLifetime(string ownerId, Action<Exception>? onError = null, Action<WidgetStatus, string?>? onStatus = null)
    {
        OwnerId = ownerId;
        _onError = onError;
        _onStatus = onStatus;
    }

    public string OwnerId { get; }

    public CancellationToken Stopping => _stopping.Token;

    public bool IsDisposed => _disposed;

    /// <summary>Resources still held, for the leak checks in tests and the resource meter.</summary>
    public int ResourceCount
    {
        get
        {
            lock (_resources)
                return _resources.Count;
        }
    }

    /// <summary>Raised when a resource fails to dispose, or a <see cref="Stopping"/> callback throws.</summary>
    public event EventHandler<Exception>? DisposeFailed;

    public void Add(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_resources)
        {
            if (!_disposed)
            {
                _resources.Add(resource);
                return;
            }
        }
        DisposeQuietly(resource);
    }

    /// <summary>Lets go of a resource without disposing it, for one the owner already disposed itself.</summary>
    public void Forget(IDisposable resource)
    {
        lock (_resources)
            _resources.Remove(resource);
    }

    public void ReportError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!_disposed)
            _onError?.Invoke(exception);
    }

    public void ReportStatus(WidgetStatus status, string? messageKey = null)
    {
        if (!_disposed)
            _onStatus?.Invoke(status, messageKey);
    }

    /// <summary>
    /// Cancels <see cref="Stopping"/> and keeps the resources. The host calls it before it awaits the owner's
    /// own stop, so a start that is still running gives up first. <see cref="Dispose"/> cancels too.
    /// </summary>
    public void CancelStopping()
    {
        if (_stopping.IsCancellationRequested)
            return;
        try
        {
            _stopping.Cancel();
        }
        catch (AggregateException ex)
        {
            // A Stopping callback threw. Every callback has run, and the resources must still be released.
            DisposeFailed?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        List<IDisposable> resources;
        lock (_resources)
        {
            if (_disposed)
                return;
            _disposed = true;
            resources = [.. _resources];
            _resources.Clear();
        }

        CancelStopping();
        for (var i = resources.Count - 1; i >= 0; i--)
            DisposeQuietly(resources[i]);
        // The source is not disposed: it has no timer, and Stopping must stay readable after the owner stopped.
    }

    private void DisposeQuietly(IDisposable resource)
    {
        try
        {
            resource.Dispose();
        }
        catch (Exception ex)
        {
            DisposeFailed?.Invoke(this, ex);
        }
    }
}
