namespace Atap.Core.Widgets;

/// <summary>
/// One running widget instance. Widgets own their data and expose a view model;
/// the app maps that view model to a view per notch state and size.
/// </summary>
/// <remarks>
/// Widgets must be event-driven: no polling loops, and timers only through
/// <see cref="WidgetContext.Scheduler"/>. An instance exists only while it is enabled.
/// </remarks>
public interface IWidget : IAsyncDisposable
{
    /// <summary>The instance id used in layout.json, for example "media" or "json-url#nas".</summary>
    string Id { get; }

    IWidgetDescriptor Descriptor { get; }

    /// <summary>True when the widget wants to own the compact notch (for example, music is playing).</summary>
    bool IsLive { get; }

    /// <summary>Raised on the UI thread when <see cref="IsLive"/> changes.</summary>
    event EventHandler? IsLiveChanged;

    /// <summary>The view model the app binds this widget's views to.</summary>
    object ViewModel { get; }

    /// <summary>Starts listening for data. Called once, on the UI thread. The host waits at most 10 seconds.</summary>
    Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default);

    /// <summary>Stops listening and releases system resources. The host waits at most 10 seconds.</summary>
    Task StopAsync();
}

/// <summary>
/// A widget that draws a small glyph on the notch edge while something is on,
/// for example the privacy dot or Caps Lock.
/// </summary>
public interface IIndicator
{
    bool IndicatorOn { get; }

    string IndicatorIconKey { get; }

    string IndicatorNameKey { get; }

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler? IndicatorChanged;
}
