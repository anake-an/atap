using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Atap.Core.Placement;

namespace Atap.Interop;

/// <summary>
/// Keeps a window that WPF centres fully on the screen it opens on. WPF centres it at the scale of the screen it
/// was made on, so on a second screen with another scale it can end up too tall, with its title bar off screen.
/// </summary>
internal static class ScreenFit
{
    // Space kept from the screen edges, in DIPs.
    private const double Margin = 16;

    /// <summary>Checks the window once it has opened and has its final scale, and moves it inside if it is not.</summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        // The size from the XAML. Width and Height change when the scale changes while it opens.
        var size = new Size(window.Width, window.Height);
        var rendered = false;
        window.DpiChanged += (_, _) =>
        {
            if (!rendered)
                window.Dispatcher.InvokeAsync(() => Fit(window, size), DispatcherPriority.Loaded);
        };
        window.ContentRendered += (_, _) =>
        {
            rendered = true;
            Fit(window, size);
        };
    }

    private static void Fit(Window window, Size size)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || window.WindowState != WindowState.Normal || !NativeMethods.GetWindowRect(hwnd, out var rect))
            return;

        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        var w = info.rcWork;
        var work = new PixelRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
        if (rect.Left >= w.Left && rect.Top >= w.Top && rect.Right <= w.Right && rect.Bottom <= w.Bottom)
            return;

        var fitted = EditorWindowPlacement.Compute(work, null, MonitorEnumerator.GetScale(work), size.Width, size.Height, Margin);
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, fitted.Left, fitted.Top, fitted.Width, fitted.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOOWNERZORDER | NativeMethods.SWP_NOACTIVATE);
    }
}
