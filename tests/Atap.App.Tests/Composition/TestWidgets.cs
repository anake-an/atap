using Atap.Composition;
using Atap.Core;
using Atap.Core.Widgets;

namespace Atap.Tests.Composition;

/// <summary>A widget type for host tests. Every instance it creates is kept, so tests can inspect them.</summary>
internal sealed class TestDescriptor(string typeId, bool multi = false, NotchState[]? states = null) : IWidgetDescriptor
{
    public List<TestWidget> Created { get; } = [];

    /// <summary>Runs inside StartAsync. Return a task that never ends to test the start timeout.</summary>
    public Func<TestWidget, Task>? OnStart { get; set; }

    /// <summary>Runs inside StopAsync.</summary>
    public Func<TestWidget, Task>? OnStop { get; set; }

    /// <summary>What every instance did, in order, for example "alpha start" and "alpha stop".</summary>
    public List<string> Events { get; } = [];

    public bool ThrowOnCreate { get; set; }

    public string TypeId { get; } = typeId;

    public string NameKey => $"{TypeId}.name";

    public string DescriptionKey => $"{TypeId}.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes => [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = states ?? [NotchState.Idle, NotchState.Compact];

    public IReadOnlyList<SettingDefinition> Settings => [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances { get; } = multi;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource => new($"/Tests;component/{TypeId}.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context)
    {
        if (ThrowOnCreate)
            throw new InvalidOperationException("Cannot create.");
        var widget = new TestWidget(context, this);
        Created.Add(widget);
        return widget;
    }
}

internal sealed class TestWidget(WidgetContext context, TestDescriptor descriptor) : WidgetBase(context)
{
    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public int Disposals { get; private set; }

    public WidgetContext WidgetContext => Context;

    public override object ViewModel { get; } = new();

    public void SetLive(bool live) => SetIsLive(live);

    public override async Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Starts++;
        descriptor.Events.Add($"{Id} start");
        await (descriptor.OnStart?.Invoke(this) ?? Task.CompletedTask);
        descriptor.Events.Add($"{Id} started");
    }

    public override Task StopAsync()
    {
        Stops++;
        descriptor.Events.Add($"{Id} stop");
        return descriptor.OnStop?.Invoke(this) ?? Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        Disposals++;
        descriptor.Events.Add($"{Id} dispose");
        return base.DisposeAsync();
    }
}

/// <summary>Counts view acquisitions per type instead of merging XAML.</summary>
internal sealed class CountingViews : IWidgetViews
{
    public Dictionary<string, int> Loaded { get; } = new(StringComparer.Ordinal);

    public int TotalLoads { get; private set; }

    public IDisposable Acquire(IWidgetDescriptor descriptor)
    {
        if (Loaded.TryGetValue(descriptor.TypeId, out var count) && count > 0)
        {
            Loaded[descriptor.TypeId] = count + 1;
        }
        else
        {
            Loaded[descriptor.TypeId] = 1;
            TotalLoads++;
        }
        return new Release(this, descriptor.TypeId);
    }

    private sealed class Release(CountingViews owner, string typeId) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            owner.Loaded[typeId]--;
        }
    }
}

internal sealed class TrackedResource : IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}
