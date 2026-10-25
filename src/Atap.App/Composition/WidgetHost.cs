using System.Globalization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;

namespace Atap.Composition;

/// <summary>Builds the context for one instance. The host owns the lifetime and the visibility.</summary>
internal delegate WidgetContext WidgetContextFactory(
    string instanceId, IWidgetDescriptor descriptor, OwnerLifetime lifetime, IObservableValue<WidgetVisibility> visibility);

/// <summary>
/// Runs the enabled widget instances. Applying a new list of instance ids is a diff: removed instances
/// stop, new ones start and the rest keep running. Every instance has its own lifetime, so what it
/// scheduled, registered or leased ends with it.
/// </summary>
/// <remarks>
/// Start and stop each have a 10 second cap. One start that throws or times out marks the instance Failed
/// at once. Errors from scheduled callbacks and from <see cref="IWidgetLifetime.ReportError"/> are counted
/// per instance: five within 60 seconds mark it Failed and stop it. A stop cancels
/// <see cref="IWidgetLifetime.Stopping"/> before it awaits the widget's own stop, so a start that timed out
/// gives up first. Applying, starting, restarting, stopping after errors and disposing run one at a
/// time, in the order they were asked for, so an instance never stops while its start is still running.
/// Use the host on the UI thread.
/// </remarks>
internal sealed class WidgetHost : IWidgetHostView, IAsyncDisposable
{
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ErrorWindow = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RestartDelay = TimeSpan.FromMilliseconds(500);
    public const int ErrorLimit = 5;

    /// <summary>Ids from layout.json are cut to this many characters in the log.</summary>
    public const int LoggedIdLength = 64;

    // Distinct bad ids logged per session. A hand-edited or synced file full of junk cannot fill the log.
    private const int LoggedInvalidLimit = 50;

    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly WidgetCatalog _catalog;
    private readonly WidgetContextFactory _createContext;
    private readonly IWidgetViews _views;
    private readonly Func<string, IWidgetDescriptor, SettingValues> _settingsFor;
    private readonly TimeProvider _time;
    private readonly IScheduler _scheduler;
    private readonly SynchronizationContext? _ui;
    private readonly Action<string, Exception?> _log;
    private readonly Dictionary<object, IReadOnlyDictionary<string, WidgetVisibility>> _reports = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _reportedInvalid = new(StringComparer.Ordinal);
    private List<Instance> _instances = [];
    private IReadOnlyList<IWidget> _widgets = [];
    private bool _started;
    private bool _disposed;

    /// <param name="catalog">Every known widget type.</param>
    /// <param name="createContext">Builds each instance's services.</param>
    /// <param name="views">Merges a type's views while it has instances.</param>
    /// <param name="settingsFor">Reads an instance's settings from the layout.</param>
    /// <param name="time">For the start and stop caps and the error window.</param>
    /// <param name="scheduler">For the restart debounce.</param>
    /// <param name="ui">Where errors reported from other threads are handled. Null runs them inline.</param>
    /// <param name="log">Warnings, with the exception if any.</param>
    public WidgetHost(
        WidgetCatalog catalog,
        WidgetContextFactory createContext,
        IWidgetViews views,
        Func<string, IWidgetDescriptor, SettingValues> settingsFor,
        TimeProvider time,
        IScheduler scheduler,
        SynchronizationContext? ui,
        Action<string, Exception?> log)
    {
        _catalog = catalog;
        _createContext = createContext;
        _views = views;
        _settingsFor = settingsFor;
        _time = time;
        _scheduler = scheduler;
        _ui = ui;
        _log = log;
    }

    public WidgetCatalog Catalog => _catalog;

    /// <summary>Instances that exist right now (created, started or starting), in layout order.</summary>
    public IReadOnlyList<IWidget> Widgets => _widgets;

    /// <summary>Every enabled instance id the host knows, including Failed ones.</summary>
    public IReadOnlyList<string> InstanceIds => _instances.Select(i => i.Id).ToList();

    public bool IsStarted => _started;

    /// <summary>Raised when <see cref="Widgets"/> changes.</summary>
    public event EventHandler? WidgetsChanged;

    /// <summary>Raised with the instance id when its status changes.</summary>
    public event EventHandler<string>? StatusChanged;

    public WidgetStatus StatusOf(string instanceId) => Find(instanceId)?.Status ?? WidgetStatus.Stopped;

    public string? StatusMessageKeyOf(string instanceId) => Find(instanceId)?.StatusMessageKey;

    public WidgetVisibility VisibilityOf(string instanceId) => Merged(instanceId);

    /// <summary>
    /// Makes the running set match <paramref name="enabledIds"/>. New instances are created at once;
    /// they start now if the host has started, otherwise on <see cref="StartAsync"/>.
    /// Unknown, malformed and duplicate ids are skipped. Waits for any earlier apply, start or restart.
    /// </summary>
    public Task ApplyAsync(IEnumerable<string> enabledIds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var ids = enabledIds.ToList();
        return RunExclusiveAsync(() => ApplyCoreAsync(ids));
    }

    /// <summary>Starts every created instance, one after another. Later instances start as soon as they are added.</summary>
    public Task StartAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return RunExclusiveAsync(StartCoreAsync);
    }

    /// <summary>Stops an instance and starts a fresh one with the same id, for example after Failed.</summary>
    public Task RestartAsync(string instanceId) =>
        _disposed ? Task.CompletedTask : RunExclusiveAsync(() => RestartCoreAsync(instanceId));

    /// <summary>Restarts an instance 500 ms after the last call, for settings changes.</summary>
    public void RestartSoon(string instanceId)
    {
        if (_disposed || Find(instanceId) is not { } instance)
            return;
        instance.PendingRestart?.Dispose();
        // A restart missed during sleep still happens after wake.
        instance.PendingRestart = _scheduler.At(_time.GetUtcNow() + RestartDelay, () =>
        {
            instance.PendingRestart = null;
            _ = RestartAsync(instanceId);
        }, new ScheduleOptions(Missed: MissedRun.RunOnce));
    }

    /// <summary>One notch window reports which instances it shows and how. Each instance gets the highest over all windows.</summary>
    public void ReportVisibility(object reporter, IReadOnlyDictionary<string, WidgetVisibility> shown)
    {
        _reports[reporter] = shown;
        RefreshVisibility();
    }

    /// <summary>A window closed; what it showed no longer counts.</summary>
    public void RemoveReporter(object reporter)
    {
        if (_reports.Remove(reporter))
            RefreshVisibility();
    }

    /// <summary>Stops and disposes every instance, newest first, after whatever is running now.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await RunExclusiveAsync(async () =>
        {
            var instances = _instances.ToList();
            _instances = [];
            PublishWidgets();
            for (var i = instances.Count - 1; i >= 0; i--)
                await StopInstanceAsync(instances[i], WidgetStatus.Stopped);
        });
    }

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

    private async Task ApplyCoreAsync(IReadOnlyList<string> enabledIds)
    {
        // Asked for before the host was disposed: the instances it would make are stopped right after.
        if (_disposed)
            return;

        var desired = Validate(enabledIds);
        var existing = _instances.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var next = new List<Instance>();
        var added = new List<Instance>();
        foreach (var (id, descriptor) in desired)
        {
            if (existing.Remove(id, out var kept))
            {
                next.Add(kept);
                continue;
            }
            var instance = new Instance(id, descriptor);
            Create(instance);
            next.Add(instance);
            added.Add(instance);
        }

        _instances = next;
        PublishWidgets();

        foreach (var removed in existing.Values)
            await StopInstanceAsync(removed, WidgetStatus.Stopped);
        if (_started)
        {
            foreach (var instance in added)
                await StartInstanceAsync(instance);
        }
    }

    private async Task StartCoreAsync()
    {
        if (_disposed)
            return;
        _started = true;
        foreach (var instance in _instances.ToList())
        {
            if (_disposed)
                return;
            await StartInstanceAsync(instance);
        }
    }

    private async Task RestartCoreAsync(string instanceId)
    {
        if (_disposed || Find(instanceId) is not { } instance)
            return;
        await StopInstanceAsync(instance, WidgetStatus.Stopped);
        if (_disposed || !_instances.Contains(instance))
            return;
        instance.Errors.Clear();
        Create(instance);
        PublishWidgets();
        if (_started)
            await StartInstanceAsync(instance);
    }

    private List<(string Id, IWidgetDescriptor Descriptor)> Validate(IEnumerable<string> ids)
    {
        var result = new List<(string, IWidgetDescriptor)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id))
                continue;
            if (!_catalog.TryResolve(id, out var descriptor))
            {
                if (_reportedInvalid.Count < LoggedInvalidLimit && _reportedInvalid.Add(id))
                    _log($"Widget id '{ForLog(id)}' is unknown or not valid; it was skipped.", null);
                continue;
            }
            result.Add((id, descriptor));
        }
        return result;
    }

    private void Create(Instance instance)
    {
        instance.Lifetime = new OwnerLifetime(instance.Id, ex => OnError(instance, ex), (status, key) => OnStatus(instance, status, key));
        instance.Lifetime.DisposeFailed += (_, ex) => _log($"Widget '{instance.Id}' failed to release a resource.", ex);
        instance.Visibility = new ObservableValue<WidgetVisibility>(Merged(instance.Id));
        instance.Started = false;
        try
        {
            instance.Views = _views.Acquire(instance.Descriptor);
            var context = _createContext(instance.Id, instance.Descriptor, instance.Lifetime, instance.Visibility);
            var widget = instance.Descriptor.Create(context);
            if (!string.Equals(widget.Id, instance.Id, StringComparison.Ordinal))
                throw new InvalidOperationException($"The descriptor for '{instance.Id}' created a widget with id '{widget.Id}'.");
            instance.Widget = widget;
            SetStatus(instance, WidgetStatus.Stopped, null);
        }
        catch (Exception ex)
        {
            // One broken widget should not take the notch down.
            _log($"Widget '{instance.Id}' could not be created.", ex);
            instance.Lifetime.Dispose();
            instance.Views?.Dispose();
            instance.Views = null;
            SetStatus(instance, WidgetStatus.Failed, null);
        }
    }

    private async Task StartInstanceAsync(Instance instance)
    {
        if (instance.Widget is not { } widget || instance.Started)
            return;
        instance.Started = true;

        SettingValues settings;
        try
        {
            settings = _settingsFor(instance.Id, instance.Descriptor);
        }
        catch (Exception ex)
        {
            _log($"Settings for widget '{instance.Id}' could not be read; defaults are used.", ex);
            settings = new SettingValues(instance.Descriptor.Settings, null);
        }

        try
        {
            await widget.StartAsync(settings, instance.Lifetime.Stopping).WaitAsync(StartTimeout, _time);
            if (instance.Widget == widget && instance.Status == WidgetStatus.Stopped)
                SetStatus(instance, WidgetStatus.Running, null);
        }
        catch (Exception ex)
        {
            var message = ex is TimeoutException
                ? $"Widget '{instance.Id}' did not start within {StartTimeout.TotalSeconds:0} seconds."
                : $"Widget '{instance.Id}' failed to start.";
            _log(message, ex);
            CountError(instance);
            if (instance.Widget == widget)
                await StopInstanceAsync(instance, WidgetStatus.Failed);
        }
    }

    private async Task StopInstanceAsync(Instance instance, WidgetStatus finalStatus)
    {
        instance.PendingRestart?.Dispose();
        instance.PendingRestart = null;

        var widget = instance.Widget;
        if (widget is null)
        {
            SetStatus(instance, finalStatus == WidgetStatus.Failed ? WidgetStatus.Failed : instance.Status, null);
            return;
        }

        instance.Widget = null;
        PublishWidgets();

        // Cancel first: a start that timed out may still be running, and the widget's stop may wait for it.
        instance.Lifetime.CancelStopping();
        if (instance.Started)
        {
            try
            {
                await widget.StopAsync().WaitAsync(StopTimeout, _time);
            }
            catch (Exception ex)
            {
                _log($"Widget '{instance.Id}' failed to stop.", ex);
            }
        }
        instance.Started = false;
        instance.Lifetime.Dispose();

        try
        {
            await widget.DisposeAsync();
        }
        catch (Exception ex)
        {
            _log($"Widget '{instance.Id}' failed to dispose.", ex);
        }

        instance.Views?.Dispose();
        instance.Views = null;
        SetStatus(instance, finalStatus, null);
    }

    private void OnError(Instance instance, Exception exception)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            HandleError(instance, exception);
        else
            _ui.Post(_ => HandleError(instance, exception), null);
    }

    private void HandleError(Instance instance, Exception exception)
    {
        if (_disposed || instance.Widget is not { } widget)
            return;
        _log($"Widget '{instance.Id}' reported an error.", exception);
        if (!CountError(instance))
            return;

        // Queued, so a start or stop already running finishes first. By then the instance may have been
        // restarted or removed, and only the widget that failed is stopped.
        _ = RunExclusiveAsync(() => instance.Widget == widget && !_disposed
            ? StopInstanceAsync(instance, WidgetStatus.Failed)
            : Task.CompletedTask);
    }

    /// <summary>Records an error. True when the instance has reached the limit and should stop.</summary>
    private bool CountError(Instance instance)
    {
        var now = _time.GetUtcNow();
        instance.Errors.Enqueue(now);
        while (instance.Errors.Count > 0 && now - instance.Errors.Peek() > ErrorWindow)
            instance.Errors.Dequeue();
        return instance.Errors.Count >= ErrorLimit;
    }

    private void OnStatus(Instance instance, WidgetStatus status, string? messageKey)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            SetStatus(instance, status, messageKey);
        else
            _ui.Post(_ => SetStatus(instance, status, messageKey), null);
    }

    private void SetStatus(Instance instance, WidgetStatus status, string? messageKey)
    {
        if (instance.Status == status && instance.StatusMessageKey == messageKey)
            return;
        instance.Status = status;
        instance.StatusMessageKey = messageKey;
        StatusChanged?.Invoke(this, instance.Id);
    }

    private void PublishWidgets()
    {
        _widgets = _instances.Where(i => i.Widget is not null).Select(i => i.Widget!).ToList();
        WidgetsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshVisibility()
    {
        foreach (var instance in _instances)
            instance.Visibility.Value = Merged(instance.Id);
    }

    private WidgetVisibility Merged(string instanceId)
    {
        var merged = WidgetVisibility.Hidden;
        foreach (var report in _reports.Values)
        {
            if (report.TryGetValue(instanceId, out var visibility) && visibility > merged)
                merged = visibility;
        }
        return merged;
    }

    private Instance? Find(string instanceId) =>
        _instances.Find(i => string.Equals(i.Id, instanceId, StringComparison.Ordinal));

    /// <summary>An id from layout.json as it may be logged: no control or format characters, at most <see cref="LoggedIdLength"/> long.</summary>
    internal static string ForLog(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return string.Empty;
        var clean = new string(id.Where(c => char.GetUnicodeCategory(c) is not (
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate)).ToArray());
        return clean.Length <= LoggedIdLength ? clean : string.Concat(clean.AsSpan(0, LoggedIdLength), "...");
    }

    private sealed class Instance(string id, IWidgetDescriptor descriptor)
    {
        public string Id { get; } = id;

        public IWidgetDescriptor Descriptor { get; } = descriptor;

        public IWidget? Widget { get; set; }

        public OwnerLifetime Lifetime { get; set; } = null!;

        public ObservableValue<WidgetVisibility> Visibility { get; set; } = new(WidgetVisibility.Hidden);

        public IDisposable? Views { get; set; }

        public bool Started { get; set; }

        public WidgetStatus Status { get; set; } = WidgetStatus.Stopped;

        public string? StatusMessageKey { get; set; }

        public Queue<DateTimeOffset> Errors { get; } = new();

        public IDisposable? PendingRestart { get; set; }
    }
}
