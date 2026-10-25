using System.Text.Json.Serialization;

namespace Atap.Core.Placement;

/// <summary>Where the notch sits: which edge, how far along it, and on which monitors.</summary>
public sealed class NotchPlacement
{
    /// <summary>Largest <see cref="Offset"/> either way. Far beyond any screen, small enough that the math cannot overflow.</summary>
    public const int MaxOffset = 100_000;

    public NotchAnchor Anchor { get; set; } = NotchAnchor.TopCenter;

    /// <summary>
    /// Shift along the anchored edge in pixels at 100% scale (scaled with the monitor's DPI).
    /// Positive moves right on the top edge and down on a side edge. The notch never leaves the monitor.
    /// </summary>
    public int Offset { get; set; }

    public MonitorSelection Monitor { get; set; } = new();

    /// <summary>Replaces nulls and out-of-range values left by a hand-edited file.</summary>
    public void Normalize()
    {
        if (!Enum.IsDefined(Anchor))
            Anchor = NotchAnchor.TopCenter;
        Offset = Math.Clamp(Offset, -MaxOffset, MaxOffset);
        Monitor ??= new();
        Monitor.Normalize();
    }
}

/// <summary>Which monitors show the notch.</summary>
public sealed class MonitorSelection
{
    public MonitorMode Mode { get; set; } = MonitorMode.Primary;

    /// <summary>Stable id of the chosen monitor (its display device path). Only used in <see cref="MonitorMode.Specific"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceId { get; set; }

    /// <summary>Name shown for the chosen monitor, kept so the menu can name it while it is disconnected.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FriendlyName { get; set; }

    public static MonitorSelection Primary() => new();

    public static MonitorSelection All() => new() { Mode = MonitorMode.All };

    public static MonitorSelection Specific(DisplayMonitor monitor) => new()
    {
        Mode = MonitorMode.Specific,
        DeviceId = monitor.DeviceId,
        FriendlyName = monitor.FriendlyName,
    };

    internal void Normalize()
    {
        if (!Enum.IsDefined(Mode) || (Mode == MonitorMode.Specific && string.IsNullOrWhiteSpace(DeviceId)))
            Mode = MonitorMode.Primary;
        if (Mode != MonitorMode.Specific)
        {
            DeviceId = null;
            FriendlyName = null;
        }
    }
}
