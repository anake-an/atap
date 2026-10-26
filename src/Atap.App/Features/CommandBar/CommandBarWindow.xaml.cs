using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Atap.Interop;

namespace Atap.Features.CommandBar;

/// <summary>
/// The command bar window, near the top of the screen the pointer is on. Typing filters, Up and Down choose, Enter
/// runs and Esc closes and gives the keyboard back to the window that had it. A click elsewhere closes it too.
/// </summary>
public partial class CommandBarWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;

    private readonly CommandBarViewModel _viewModel;
    private readonly IntPtr _previous;
    private bool _closing;

    internal CommandBarWindow(CommandBarViewModel viewModel, IntPtr previous)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _previous = previous;
        DataContext = viewModel;
        viewModel.Done += (_, _) => CloseOnce(restoreFocus: false);
        // The answer shows in the notch, which never takes the keyboard, so the app before the bar gets it back.
        viewModel.Asked += (_, _) => CloseOnce(restoreFocus: true);
        Loaded += (_, _) =>
        {
            Place();
            Box.Focus();
        };
        Results.MouseLeftButtonUp += OnResultClicked;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Esc closes from anywhere. The other keys belong to the box, so the tick box keeps its own.
        if (e.Key != Key.Escape && !Box.IsKeyboardFocusWithin)
            return;
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.Move(1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.Move(-1);
                Results.ScrollIntoView(Results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = _viewModel.RunSelected();
                break;
            case Key.Escape:
                e.Handled = true;
                CloseOnce(restoreFocus: true);
                break;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Moving to a screen with another scale resizes the window. Place it again once that is done.
        Dispatcher.InvokeAsync(Place);
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // A click on another window already moved the keyboard there. An answer shows in the notch, so it stays.
        CloseOnce(restoreFocus: false);
    }

    internal void CloseOnce(bool restoreFocus)
    {
        if (_closing)
            return;
        _closing = true;
        Close();
        if (restoreFocus && _previous != IntPtr.Zero && NativeMethods.IsWindow(_previous))
            NativeMethods.SetForegroundWindow(_previous);
    }

    private void OnResultClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject) is { } source
            && ItemsControl.ContainerFromElement(Results, source) is ListBoxItem { DataContext: CommandBarRow row })
            _viewModel.Run(row);
    }

    /// <summary>Centred on the screen under the pointer, a fifth of the way down its work area.</summary>
    private void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeMethods.GetCursorPos(out var pointer))
            return;
        var rect = new NativeMethods.RECT { Left = pointer.X, Top = pointer.Y, Right = pointer.X + 1, Bottom = pointer.Y + 1 };
        var monitor = NativeMethods.MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return;
        // The scale of the screen it goes to, not the one it opened on, so it is centred there too.
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0
            ? dpi / 96.0
            : System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        var work = info.rcWork;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var x = work.Left + (work.Right - work.Left - width) / 2;
        var y = work.Top + (work.Bottom - work.Top) / 5;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
    }
}

/// <summary>The real window. One at a time, made on open and dropped on close.</summary>
internal sealed class CommandBarShell : ICommandBarShell
{
    private CommandBarWindow? _window;

    public bool IsOpen => _window is not null;

    public void Show(CommandBarViewModel viewModel)
    {
        // Noted before the command bar takes the keyboard, so Esc can give it back.
        var previous = NativeMethods.GetForegroundWindow();
        var window = new CommandBarWindow(viewModel, previous);
        window.Closed += (_, _) =>
        {
            _window = null;
            // Forgets the copied text. An answer in the notch carries on.
            viewModel.Dispose();
        };
        _window = window;
        window.Show();
        window.Activate();
    }

    public void Close() => _window?.CloseOnce(restoreFocus: true);

    public void Dispose() => _window?.CloseOnce(restoreFocus: false);
}
