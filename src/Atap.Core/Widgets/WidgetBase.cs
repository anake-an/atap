namespace Atap.Core.Widgets;

/// <summary>Convenience base class that takes the id and descriptor from the context and handles <see cref="IWidget.IsLive"/>.</summary>
public abstract class WidgetBase(WidgetContext context) : IWidget
{
    private bool _isLive;

    protected WidgetContext Context { get; } = context;

    public string Id => Context.InstanceId;

    public IWidgetDescriptor Descriptor => Context.Descriptor;

    public bool IsLive => _isLive;

    public event EventHandler? IsLiveChanged;

    public abstract object ViewModel { get; }

    public abstract Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default);

    public abstract Task StopAsync();

    /// <summary>Called after <see cref="StopAsync"/> and after the lifetime is disposed. Override to free what is left.</summary>
    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>Updates <see cref="IsLive"/> and raises the event only when it changes.</summary>
    protected void SetIsLive(bool value)
    {
        if (_isLive == value)
            return;
        _isLive = value;
        IsLiveChanged?.Invoke(this, EventArgs.Empty);
    }
}
