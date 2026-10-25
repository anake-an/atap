using Atap.Core;
using Atap.Core.Widgets;

namespace Atap.Core.Tests;

/// <summary>Minimal descriptor for tests. <see cref="Create"/> is not used by the Core tests.</summary>
internal sealed class FakeDescriptor(string typeId, bool supportsCompact = true, bool allowsMultipleInstances = false) : IWidgetDescriptor
{
    public string TypeId { get; } = typeId;

    public string NameKey => $"{TypeId}.name";

    public string DescriptionKey => $"{TypeId}.description";

    public WidgetGroup Group => WidgetGroup.Custom;

    public IReadOnlyList<WidgetSize> SupportedSizes => [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } =
        supportsCompact ? [NotchState.Compact, NotchState.Expanded] : [NotchState.Expanded];

    public IReadOnlyList<SettingDefinition> Settings => [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances { get; } = allowsMultipleInstances;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource => new($"/Tests;component/{TypeId}.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => throw new NotSupportedException();
}

/// <summary>Minimal widget for tests; liveness is set directly.</summary>
internal sealed class FakeWidget(string id, bool supportsCompact = true) : IWidget
{
    private bool _isLive;

    public string Id { get; } = id;

    public IWidgetDescriptor Descriptor { get; } = new FakeDescriptor(id.Split(WidgetInstanceId.Separator)[0], supportsCompact);

    public bool IsLive => _isLive;

    public event EventHandler? IsLiveChanged;

    public object ViewModel { get; } = new();

    public void SetLive(bool value)
    {
        if (_isLive == value)
            return;
        _isLive = value;
        IsLiveChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public static FakeWidget Live(string id) => Create(id, live: true);

    public static FakeWidget Create(string id, bool live = false, bool supportsCompact = true)
    {
        var widget = new FakeWidget(id, supportsCompact);
        widget.SetLive(live);
        return widget;
    }
}
