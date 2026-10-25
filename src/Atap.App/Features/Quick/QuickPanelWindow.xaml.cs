using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Atap.Core.Placement;
using Atap.Interop;

namespace Atap.Features.Quick;

/// <summary>
/// The quick panel: a small window next to the pointer, by the tray icon or under the notch. It takes the
/// keyboard while open and closes when it loses it, on Esc, or after a button that leaves it.
/// </summary>
public partial class QuickPanelWindow : Window
{
    // Space from the pointer and the screen edges, in DIPs. The shadow margin around the card counts towards it.
    private const double Gap = 4;
    private const double ShadowMargin = 12;
    private const uint MonitorDefaultToNearest = 2;

    private readonly NativeMethods.POINT _pointer;
    private bool _closing;

    internal QuickPanelWindow(QuickPanelViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        NativeMethods.GetCursorPos(out _pointer);
        viewModel.Done += (_, _) => CloseOnce();
        Closed += (_, _) => viewModel.Dispose();
        Loaded += (_, _) => PlaceNearPointer();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CloseOnce();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.Escape)
            return;
        e.Handled = true;
        CloseOnce();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // Moving to a monitor with another scale resizes the window. Place it again once that is done.
        Dispatcher.InvokeAsync(PlaceNearPointer);
    }

    private void CloseOnce()
    {
        if (_closing)
            return;
        _closing = true;
        Close();
    }

    /// <summary>Puts the card next to the pointer, inside the work area of the monitor under it.</summary>
    private void PlaceNearPointer()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (_closing || hwnd == IntPtr.Zero)
            return;

        var rect = new NativeMethods.RECT { Left = _pointer.X, Top = _pointer.Y, Right = _pointer.X + 1, Bottom = _pointer.Y + 1 };
        var monitor = NativeMethods.MonitorFromRect(ref rect, MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        var work = info.rcWork;
        var width = (int)Math.Ceiling(ActualWidth * scale);
        var height = (int)Math.Ceiling(ActualHeight * scale);
        var corner = PopupPlacement.Near(
            new PixelPoint(_pointer.X, _pointer.Y),
            new PixelRect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top),
            width,
            height,
            (int)Math.Round((Gap - ShadowMargin) * scale));

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, corner.X, corner.Y, 0, 0,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOOWNERZORDER);
    }
}
