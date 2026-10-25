using System.Runtime.InteropServices;
using Atap.Core.Placement;
using Atap.Interop;
using Atap.Notch;

namespace Atap.Features.LayoutEditor;

/// <summary>The monitor the editor opens on: its work area, its scale and where the notch with its badge sits.</summary>
/// <param name="WorkArea">The work area in physical pixels, without the taskbar.</param>
/// <param name="Notch">The notch showing the "Editing layout" badge, in physical pixels.</param>
/// <param name="Scale">The monitor's scale, 1.0 at 100%.</param>
internal sealed record EditorScreen(PixelRect WorkArea, PixelRect? Notch, double Scale)
{
    // The badge's room on the notch, in DIPs: wide enough for the longest text, deep enough for the pill and shoulders.
    private const double BadgeLength = 280;
    private const double BadgeDepth = 48;

    /// <summary>
    /// The monitor the notch is on. With a notch on every monitor, the one under the pointer. Null when the
    /// monitors are not known yet.
    /// </summary>
    public static EditorScreen? ForNotch(NotchHost? host)
    {
        if (host is null || host.Monitors.Count == 0)
            return null;
        var targets = MonitorResolver.Resolve(host.Placement.Monitor, host.Monitors);
        if (targets.Count == 0)
            return null;

        NativeMethods.GetCursorPos(out var pointer);
        var monitor = targets.Count == 1
            ? targets[0]
            : targets.FirstOrDefault(m => Contains(m.Bounds, pointer.X, pointer.Y)) ?? MonitorResolver.PrimaryOf(targets);

        var scale = MonitorEnumerator.GetScale(monitor.Bounds);
        var anchor = host.Placement.Anchor;
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        var notch = PlacementGeometry.Compute(
            monitor.Bounds,
            scale,
            anchor,
            vertical ? BadgeDepth : BadgeLength,
            vertical ? BadgeLength : BadgeDepth,
            host.Placement.Offset,
            NotchShape.ShoulderRoom).Bounds;
        return new EditorScreen(WorkAreaOf(monitor.Bounds), notch, scale);
    }

    /// <summary>The monitor under the pointer, for when the notch's monitor is not known.</summary>
    public static EditorScreen ForPointer()
    {
        NativeMethods.GetCursorPos(out var pointer);
        var area = WorkAreaOf(new PixelRect(pointer.X, pointer.Y, 1, 1));
        return new EditorScreen(area, null, MonitorEnumerator.GetScale(area));
    }

    private static bool Contains(PixelRect r, int x, int y) => x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;

    /// <summary>The work area of the monitor covering <paramref name="bounds"/>, or the bounds when Windows does not say.</summary>
    private static PixelRect WorkAreaOf(PixelRect bounds)
    {
        var rect = new NativeMethods.RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var handle = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
            return bounds;
        var w = info.rcWork;
        return new PixelRect(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
    }
}
