namespace Atap.Core.Placement;

/// <summary>Turns a <see cref="MonitorSelection"/> into the monitors that should show a notch.</summary>
public static class MonitorResolver
{
    /// <summary>
    /// Returns every monitor for <see cref="MonitorMode.All"/>, otherwise exactly one: the chosen
    /// monitor, or the primary when the chosen one is not connected. Empty only when no monitors are given.
    /// </summary>
    public static IReadOnlyList<DisplayMonitor> Resolve(MonitorSelection? selection, IReadOnlyList<DisplayMonitor> monitors)
    {
        if (monitors.Count == 0)
            return [];

        return selection?.Mode switch
        {
            MonitorMode.All => monitors,
            MonitorMode.Specific => [Find(selection.DeviceId, monitors) ?? PrimaryOf(monitors)],
            _ => [PrimaryOf(monitors)],
        };
    }

    /// <summary>The monitor with <paramref name="deviceId"/>, or null when it is not connected.</summary>
    public static DisplayMonitor? Find(string? deviceId, IReadOnlyList<DisplayMonitor> monitors) =>
        string.IsNullOrEmpty(deviceId)
            ? null
            : monitors.FirstOrDefault(m => string.Equals(m.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The primary monitor; the first one if none is flagged primary.</summary>
    public static DisplayMonitor PrimaryOf(IReadOnlyList<DisplayMonitor> monitors) =>
        monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
}
