namespace Atap.Core.Placement;

/// <summary>A rectangle in physical screen pixels (virtual-screen coordinates, may be negative).</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

/// <summary>A connected monitor, as the placement logic sees it.</summary>
/// <param name="DeviceId">Stable id (the display device path). Survives restarts and display changes.</param>
/// <param name="FriendlyName">Name shown in menus, for example "DELL U2720Q".</param>
/// <param name="Bounds">Full monitor area in physical pixels, including any taskbar.</param>
/// <param name="IsPrimary">True for the Windows primary monitor.</param>
public sealed record DisplayMonitor(string DeviceId, string FriendlyName, PixelRect Bounds, bool IsPrimary);
