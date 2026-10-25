using Atap.Core.Settings;

namespace Atap.Composition;

/// <summary>Builds the context for one feature. The host owns the lifetime.</summary>
internal delegate FeatureContext FeatureContextFactory(IFeatureDescriptor descriptor, OwnerLifetime lifetime);

/// <summary>
/// Runs the app features that are turned on. A feature exists only while its descriptor's
/// <see cref="IFeatureDescriptor.IsEnabled"/> is true, and it is re-checked only when one of its
/// <see cref="IFeatureDescriptor.EnabledBy"/> keys changes. Use it on the UI thread.
/// </summary>
/// <remarks>
/// Starts, stops and disposing run one at a time, in the order they were asked for, so a feature turned
/// off while it is still starting stops only after its start has finished. A start or stop gets
/// <see cref="StartTimeout"/> or <see cref="StopTimeout"/>, and disposing cancels a start that is still
/// running, so one stuck feature cannot block the others or Quit.
/// </remarks>
internal sealed class FeatureHost : IAsyncDisposable
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly IReadOnlyList<IFeatureDescriptor> _descriptors;
    private readonly IAppSettings _settings;
    private readonly AppMode _mode;
    private readonly FeatureContextFactory _createContext;
    private readonly Action<string> _onStopped;
    private readonly Action<string, Exception?> _log;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Running> _running = new(StringComparer.Ordinal);
    // The dictionary reuses freed slots, so the start order is kept apart for disposing last started first.
    private readonly List<string> _startOrder = [];
    private OwnerLifetime? _starting;
    private bool _started;
    private bool _disposed;

    /// <param name="descriptors">Every registered feature.</param>
    /// <param name="settings">Decides which are on.</param>
    /// <param name="mode">Safe mode turns most features off.</param>
    /// <param name="createContext">Builds each feature's services.</param>
    /// <param name="onStopped">Called with the feature id after it stopped, so notch flags it set can be cleared.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    /// <param name="time">Measures the start and stop time limits. The system clock when null.</param>
    public FeatureHost(
        IReadOnlyList<IFeatureDescriptor> descriptors,
        IAppSettings settings,
        AppMode mode,
        FeatureContextFactory createContext,
        Action<string> onStopped,
        Action<string, Exception?> log,
        TimeProvider? time = null)
    {
        _descriptors = descriptors;
        _settings = settings;
        _mode = mode;
        _createContext = createContext;
        _onStopped = onStopped;
        _log = log;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Ids of the features that have started and run right now.</summary>
    public IReadOnlyCollection<string> RunningIds => _running.Keys;

    /// <summary>Starts every feature that is on, then follows setting changes.</summary>
    public async Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
            return;
        _started = true;
        _settings.Changed += OnSettingsChanged;
        await ReconcileAsync(_descriptors);
    }

    /// <summary>Stops every feature, last started first, after whatever is running now.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        // A start still running gives up first, so Quit does not wait on it.
        _starting?.CancelStopping();
        await RunExclusiveAsync(async () =>
        {
            for (var i = _startOrder.Count - 1; i >= 0; i--)
                await StopAsync(_startOrder[i]);
        });
    }

    private async void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        try
        {
            var affected = _descriptors.Where(d => d.EnabledBy.Any(e.Keys.Contains)).ToList();
            if (affected.Count > 0)
                await ReconcileAsync(affected);
        }
        catch (Exception ex)
        {
            _log("A feature could not follow a settings change.", ex);
        }
    }

    /// <summary>Queued behind earlier work. Each check reads the settings as they are when it runs.</summary>
    private Task ReconcileAsync(IReadOnlyList<IFeatureDescriptor> descriptors) => RunExclusiveAsync(async () =>
    {
        foreach (var descriptor in descriptors)
            await ReconcileAsync(descriptor);
    });

    /// <summary>Runs <paramref name="operation"/> once every operation asked for earlier has finished.</summary>
    private async Task RunExclusiveAsync(Func<Task> operation)
    {
        await _queue.WaitAsync();
        try
        {
            await operation();
        }
        finally
        {
            _queue.Release();
        }
    }

    private async Task ReconcileAsync(IFeatureDescriptor descriptor)
    {
        // Asked for before the host was disposed: disposing runs next and stops what is left.
        if (_disposed)
            return;

        bool enabled;
        try
        {
            enabled = descriptor.IsEnabled(_settings.Values, _mode);
        }
        catch (Exception ex)
        {
            _log($"Feature '{descriptor.Id}' could not tell whether it is on; it stays off.", ex);
            enabled = false;
        }

        var isRunning = _running.ContainsKey(descriptor.Id);
        if (enabled && !isRunning)
            await StartAsync(descriptor);
        else if (!enabled && isRunning)
            await StopAsync(descriptor.Id);
    }

    private async Task StartAsync(IFeatureDescriptor descriptor)
    {
        var lifetime = new OwnerLifetime(descriptor.Id, ex => _log($"Feature '{descriptor.Id}' reported an error.", ex));
        lifetime.DisposeFailed += (_, ex) => _log($"Feature '{descriptor.Id}' failed to release a resource.", ex);
        Running? running = null;
        _starting = lifetime;
        try
        {
            running = new Running(descriptor.Create(_createContext(descriptor, lifetime)), lifetime);
            await running.Feature.StartAsync().WaitAsync(StartTimeout, _time);
            // Counted as running only once its start has finished.
            _running[descriptor.Id] = running;
            _startOrder.Add(descriptor.Id);
        }
        catch (Exception ex)
        {
            // A start cancelled by disposing is expected, not a failure.
            if (ex is not OperationCanceledException || !lifetime.Stopping.IsCancellationRequested)
            {
                var message = ex is TimeoutException
                    ? $"Feature '{descriptor.Id}' did not start within {StartTimeout.TotalSeconds:0} seconds."
                    : $"Feature '{descriptor.Id}' failed to start.";
                _log(message, ex);
            }
            if (running is not null)
                await StopAsync(descriptor.Id, running);
            else
                lifetime.Dispose();
        }
        finally
        {
            _starting = null;
        }
    }

    private async Task StopAsync(string id)
    {
        if (_running.Remove(id, out var running))
        {
            _startOrder.Remove(id);
            await StopAsync(id, running);
        }
    }

    private async Task StopAsync(string id, Running running)
    {
        // Cancel first: a start that timed out may still be running, and the feature's stop may wait for it.
        running.Lifetime.CancelStopping();
        try
        {
            await running.Feature.DisposeAsync().AsTask().WaitAsync(StopTimeout, _time);
        }
        catch (Exception ex)
        {
            _log($"Feature '{id}' failed to stop.", ex);
        }
        running.Lifetime.Dispose();
        _onStopped(id);
    }

    private sealed record Running(IAppFeature Feature, OwnerLifetime Lifetime);
}
