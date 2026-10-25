using System.Runtime.InteropServices;
using Atap.Core.Placement;

namespace Atap.Interop;

/// <summary>
/// Lists connected monitors with a stable id (the display device path) and a friendly name.
/// Only called at startup and after display changes, never on a timer.
/// </summary>
internal static class MonitorEnumerator
{
    private const double DefaultDpi = 96;

    public static IReadOnlyList<DisplayMonitor> GetMonitors()
    {
        var targets = QueryTargets();
        var found = new List<(PixelRect Bounds, bool IsPrimary, string GdiName)>();

        // Held in a local so the delegate stays alive for the duration of the native call.
        NativeMethods.MonitorEnumProc callback = (handle, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                var r = info.rcMonitor;
                found.Add((
                    new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                    (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    info.szDevice ?? string.Empty));
            }
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // Left to right, then top to bottom, so menus list monitors the way they are arranged.
        found.Sort((a, b) => a.Bounds.Left != b.Bounds.Left
            ? a.Bounds.Left.CompareTo(b.Bounds.Left)
            : a.Bounds.Top.CompareTo(b.Bounds.Top));

        var monitors = new List<DisplayMonitor>(found.Count);
        for (var i = 0; i < found.Count; i++)
        {
            var (bounds, isPrimary, gdiName) = found[i];
            targets.TryGetValue(gdiName, out var target);
            var deviceId = string.IsNullOrWhiteSpace(target.DevicePath)
                ? FirstNonEmpty(QueryDeviceInterfacePath(gdiName), gdiName, $"monitor-{i + 1}")
                : target.DevicePath;
            var name = FirstNonEmpty(target.FriendlyName, target.IsInternal ? "Built-in display" : null, $"Display {i + 1}");
            monitors.Add(new DisplayMonitor(deviceId, name, bounds, isPrimary));
        }

        return Disambiguate(monitors);
    }

    /// <summary>Effective scale of the monitor covering <paramref name="bounds"/> (1.0 at 100%).</summary>
    public static double GetScale(PixelRect bounds)
    {
        var rect = new NativeMethods.RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        var monitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / DefaultDpi;
        }
        return 1.0;
    }

    /// <summary>
    /// Maps GDI device names ("\\.\DISPLAY1") to the monitor's EDID name and device path,
    /// using the display configuration API. Empty when the API fails.
    /// </summary>
    private static Dictionary<string, TargetInfo> QueryTargets()
    {
        var targets = new Dictionary<string, TargetInfo>(StringComparer.OrdinalIgnoreCase);
        var paths = QueryActivePaths();

        foreach (var path in paths)
        {
            var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME();
            source.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
            source.header.size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
            source.header.adapterId = path.sourceInfo.adapterId;
            source.header.id = path.sourceInfo.id;
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != NativeMethods.ERROR_SUCCESS
                || string.IsNullOrEmpty(source.viewGdiDeviceName)
                || targets.ContainsKey(source.viewGdiDeviceName))
            {
                // Mirrored displays share one source; the first target names it.
                continue;
            }

            var target = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME();
            target.header.type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
            target.header.size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>();
            target.header.adapterId = path.targetInfo.adapterId;
            target.header.id = path.targetInfo.id;
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) != NativeMethods.ERROR_SUCCESS)
                continue;

            targets[source.viewGdiDeviceName] = new TargetInfo(
                target.monitorFriendlyDeviceName,
                target.monitorDevicePath,
                IsInternal(target.outputTechnology));
        }

        return targets;
    }

    private static NativeMethods.DISPLAYCONFIG_PATH_INFO[] QueryActivePaths()
    {
        // The configuration can change between the size query and the read; retry a few times.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount)
                != NativeMethods.ERROR_SUCCESS)
            {
                return [];
            }

            var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            var result = NativeMethods.QueryDisplayConfig(
                NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);

            if (result == NativeMethods.ERROR_SUCCESS)
                return paths.AsSpan(0, (int)pathCount).ToArray();
            if (result != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                return [];
        }
        return [];
    }

    /// <summary>Fallback id when the display configuration API gave nothing for this monitor.</summary>
    private static string? QueryDeviceInterfacePath(string gdiName)
    {
        if (string.IsNullOrEmpty(gdiName))
            return null;
        var device = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
        return NativeMethods.EnumDisplayDevices(gdiName, 0, ref device, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME)
            ? device.DeviceID
            : null;
    }

    private static bool IsInternal(uint outputTechnology) => outputTechnology
        is NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
        or NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
        or NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
        or NativeMethods.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED;

    /// <summary>Two identical monitors get "(2)", "(3)" so the menu can tell them apart.</summary>
    private static List<DisplayMonitor> Disambiguate(List<DisplayMonitor> monitors)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicated = monitors
            .GroupBy(m => m.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicated.Count == 0)
            return monitors;

        return monitors.Select(m =>
        {
            if (!duplicated.Contains(m.FriendlyName))
                return m;
            var count = seen[m.FriendlyName] = seen.GetValueOrDefault(m.FriendlyName) + 1;
            return m with { FriendlyName = $"{m.FriendlyName} ({count})" };
        }).ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private readonly record struct TargetInfo(string? FriendlyName, string? DevicePath, bool IsInternal);
}
