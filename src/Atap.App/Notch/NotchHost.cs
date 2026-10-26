using System.Windows.Threading;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Notch;
using Atap.Core.Placement;
using Atap.Diagnostics;
using Atap.Interop;
using Microsoft.Win32;

namespace Atap.Notch;

/// <summary>
/// Owns the notch windows: one per target monitor (one in Primary and Specific mode, one per
/// monitor in All mode), placed from layout.json. Re-enumerates monitors only when Windows
/// reports a display change, and saves placement choices from the menu right away. A placement
/// changed through <see cref="ILayoutService"/> by anyone else (a settings card, onboarding, a
/// reload from disk) moves the windows too.
/// </summary>
internal sealed class NotchHost : IDisposable
{
    // Plugging in a monitor raises several display changes in a row; act once they settle.
    private static readonly TimeSpan DisplayChangeSettleTime = TimeSpan.FromMilliseconds(250);

    private readonly ILayoutService _layout;
    private readonly Func<IReadOnlyList<DisplayMonitor>> _getMonitors;
    private readonly Func<NotchMenuViewModel, INotchWindow> _createWindow;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _displayChangeTimer;
    private readonly List<INotchWindow> _windows = [];
    private NotchMenuViewModel? _menu;
    private IReadOnlyList<DisplayMonitor> _monitors = [];
    private PlacementSnapshot? _applied;
    private bool _disposed;

    /// <summary>Must be created on the UI thread.</summary>
    public NotchHost(ILayoutService layout, WidgetHost widgets, NotchControl control, IForegroundState foreground, Dispatcher dispatcher)
        : this(layout, dispatcher, MonitorEnumerator.GetMonitors, WindowFactory(layout, widgets, control, foreground))
    {
    }

    /// <summary>Must be created on the UI thread. Tests pass their own monitors and windows.</summary>
    /// <param name="layout">Where the placement lives.</param>
    /// <param name="dispatcher">The UI thread.</param>
    /// <param name="getMonitors">Lists the monitors at start and after each display change.</param>
    /// <param name="createWindow">Makes a window for one more target monitor.</param>
    internal NotchHost(
        ILayoutService layout, Dispatcher dispatcher, Func<IReadOnlyList<DisplayMonitor>> getMonitors, Func<NotchMenuViewModel, INotchWindow> createWindow)
    {
        _layout = layout;
        _getMonitors = getMonitors;
        _createWindow = createWindow;
        _dispatcher = dispatcher;

        _displayChangeTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = DisplayChangeSettleTime };
        _displayChangeTimer.Tick += OnDisplayChangeSettled;
    }

    /// <summary>Raised on the UI thread when the monitors or the placement change.</summary>
    public event EventHandler? Changed;

    public NotchPlacement Placement => _layout.Document.Placement;

    public IReadOnlyList<DisplayMonitor> Monitors => _monitors;

    /// <summary>Finds the monitors and shows the notch windows.</summary>
    public void Start(NotchMenuViewModel menu)
    {
        _menu = menu;
        _monitors = _getMonitors();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _layout.Changed += OnLayoutChanged;
        ApplyPlacement();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetAnchor(NotchAnchor anchor)
    {
        if (Placement.Anchor == anchor)
            return;
        // The offset runs along the old edge and means nothing on the new one.
        SaveAndApply(layout =>
        {
            layout.Placement.Anchor = anchor;
            layout.Placement.Offset = 0;
        });
    }

    public void SetMonitorSelection(MonitorSelection selection)
    {
        var current = Placement.Monitor;
        if (current.Mode == selection.Mode
            && string.Equals(current.DeviceId, selection.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        SaveAndApply(layout => layout.Placement.Monitor = selection);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _layout.Changed -= OnLayoutChanged;
        _displayChangeTimer.Stop();
        foreach (var window in _windows)
            window.Close();
        _windows.Clear();
    }

    private void SaveAndApply(Action<LayoutDocument> change)
    {
        // Only the placement is written; hand edits made to the file since startup are kept.
        // When layout.json could not be read at startup, nothing is saved, so the file stays as the user left it.
        switch (_layout.Update(change))
        {
            case LayoutWriteResult.ReadOnly:
                Log.Warn("Placement not saved: layout.json could not be read at startup, or is from a newer Atap.");
                break;
            case LayoutWriteResult.Failed:
                Log.Warn("Placement not saved: layout.json has errors or could not be written. Fix the file or delete it.");
                break;
        }

        // Update raised Changed, which already moved the windows. This covers a store that did not.
        ApplyIfPlacementChanged();
    }

    // Any layout change: an edit through ILayoutService, a profile switch or a reload. Only a new placement moves windows.
    private void OnLayoutChanged(object? sender, EventArgs e) => ApplyIfPlacementChanged();

    private void ApplyIfPlacementChanged()
    {
        if (_disposed || _menu is null || PlacementSnapshot.Of(Placement) == _applied)
            return;
        ApplyPlacement();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Matches windows to target monitors: a window already on a target stays there, other
    /// windows are moved, missing ones are created and leftovers are closed.
    /// </summary>
    private void ApplyPlacement()
    {
        if (_disposed || _menu is null)
            return;

        var placement = Placement;
        _applied = PlacementSnapshot.Of(placement);
        var targets = MonitorResolver.Resolve(placement.Monitor, _monitors);

        var spare = new List<INotchWindow>(_windows);
        var assigned = new INotchWindow?[targets.Count];
        for (var i = 0; i < targets.Count; i++)
        {
            var match = spare.Find(w => string.Equals(w.TargetMonitor?.DeviceId, targets[i].DeviceId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                assigned[i] = match;
                spare.Remove(match);
            }
        }

        _windows.Clear();
        for (var i = 0; i < targets.Count; i++)
        {
            var window = assigned[i];
            var isNew = false;
            if (window is null && spare.Count > 0)
            {
                window = spare[0];
                spare.RemoveAt(0);
            }
            if (window is null)
            {
                window = _createWindow(_menu);
                isNew = true;
            }

            window.Place(targets[i], placement.Anchor, placement.Offset);
            _windows.Add(window);
            if (isNew)
                window.UpdateVisibility();
        }

        foreach (var window in spare)
            window.Close();
    }

    // SystemEvents raise this on their own thread.
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _dispatcher.InvokeAsync(() =>
        {
            if (_disposed)
                return;
            _displayChangeTimer.Stop();
            _displayChangeTimer.Start();
        });

    private void OnDisplayChangeSettled(object? sender, EventArgs e)
    {
        _displayChangeTimer.Stop();
        if (_disposed)
            return;

        var monitors = _getMonitors();
        if (monitors.Count == 0)
            return; // Mid-change (for example, all displays asleep); keep the last known layout.

        _monitors = monitors;
        Atap.Diagnostics.Log.Info($"Screens changed. {monitors.Count} connected.");
        ApplyPlacement();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // The real windows. They share one input router, so the handlers see input from every notch, and one
    // keyboard owner, so only one open panel takes the keyboard.
    private static Func<NotchMenuViewModel, INotchWindow> WindowFactory(
        ILayoutService layout, WidgetHost widgets, NotchControl control, IForegroundState foreground)
    {
        var input = new NotchInputRouter(control);
        var focus = new NotchFocus();
        return menu => new NotchWindow(new NotchViewModel(layout, widgets, control, foreground), menu, input, focus);
    }

    /// <summary>
    /// The placement values the windows were last placed with. <see cref="NotchPlacement"/> is changed in
    /// place by <see cref="ILayoutService.Update"/>, so a copy is kept to compare against.
    /// </summary>
    private readonly record struct PlacementSnapshot(NotchAnchor Anchor, int Offset, MonitorMode Mode, string? DeviceId, string? FriendlyName)
    {
        public static PlacementSnapshot Of(NotchPlacement placement) => new(
            placement.Anchor,
            placement.Offset,
            placement.Monitor.Mode,
            placement.Monitor.DeviceId?.ToUpperInvariant(),
            placement.Monitor.FriendlyName);
    }
}
