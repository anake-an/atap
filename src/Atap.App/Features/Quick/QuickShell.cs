using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Atap.Interop;
using Atap.Platform.Tray;

namespace Atap.Features.Quick;

/// <summary>The real windows: the tray icon, its menu and the quick panel. Use it on the UI thread.</summary>
internal sealed class QuickShell : IQuickShell
{
    // A click on the tray icon while the panel is open takes the keyboard from the panel first, which closes it.
    // That same click must not open it again.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private TrayIcon? _tray;
    private QuickPanelWindow? _panel;
    private long _panelClosedAt = long.MinValue / 2;
    private Action? _open;
    private Action? _menu;

    public void SetTrayIcon(bool shown, Action open, Action menu)
    {
        _open = open;
        _menu = menu;
        if (shown && _tray is null)
        {
            _tray = new TrayIcon("Atap");
            _tray.Opened += (_, _) => _open?.Invoke();
            _tray.MenuRequested += (_, _) => _menu?.Invoke();
        }
        else if (!shown && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    public void ToggleQuickPanel(QuickPanelViewModel viewModel)
    {
        if (_panel is not null || Environment.TickCount64 - _panelClosedAt < ReopenGuard.TotalMilliseconds)
        {
            _panel?.Close();
            viewModel.Dispose();
            return;
        }

        var panel = new QuickPanelWindow(viewModel);
        panel.Closed += (_, _) =>
        {
            _panel = null;
            _panelClosedAt = Environment.TickCount64;
        };
        _panel = panel;
        panel.Show();
        panel.Activate();
    }

    public void ShowTrayMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        foreach (var item in items)
        {
            if (item.Header is null)
            {
                menu.Items.Add(new Separator());
                continue;
            }
            var entry = new MenuItem
            {
                Header = item.Header.Replace("_", "__", StringComparison.Ordinal),
                IsChecked = item.IsChecked,
                FontWeight = item.IsDefault ? FontWeights.SemiBold : FontWeights.Normal,
            };
            var run = item.Run;
            entry.Click += (_, _) => run?.Invoke();
            menu.Items.Add(entry);
        }

        // The menu belongs to the tray icon's hidden window. Bringing that to the front first lets a click
        // anywhere else close the menu, as Windows' own tray menus do.
        if (_tray is not null)
            NativeMethods.SetForegroundWindow(_tray.Handle);
        menu.IsOpen = true;
    }

    public void Dispose()
    {
        _panel?.Close();
        _panel = null;
        _tray?.Dispose();
        _tray = null;
    }
}
