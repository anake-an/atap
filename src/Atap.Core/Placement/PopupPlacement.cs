namespace Atap.Core.Placement;

/// <summary>A point on the virtual screen in physical pixels.</summary>
public readonly record struct PixelPoint(int X, int Y);

/// <summary>
/// Where a small panel opens next to the pointer: centred on it, above it in the lower half of the screen (the
/// taskbar and its icons) and below it in the upper half (the notch). It always stays inside the work area, so a
/// taskbar on any edge never covers it.
/// </summary>
public static class PopupPlacement
{
    /// <summary>The panel's top left corner.</summary>
    /// <param name="pointer">Where the pointer is, which may be on the taskbar outside the work area.</param>
    /// <param name="workArea">The monitor's work area, without the taskbar.</param>
    /// <param name="width">The panel's width in pixels.</param>
    /// <param name="height">The panel's height in pixels.</param>
    /// <param name="gap">Space kept from the pointer and from the work area's edges.</param>
    public static PixelPoint Near(PixelPoint pointer, PixelRect workArea, int width, int height, int gap)
    {
        var x = Clamp(pointer.X - width / 2, workArea.Left + gap, workArea.Right - width - gap);
        var below = pointer.Y < workArea.Top + workArea.Height / 2;
        var y = Clamp(below ? pointer.Y + gap : pointer.Y - height - gap, workArea.Top + gap, workArea.Bottom - height - gap);
        return new PixelPoint(x, y);
    }

    // A panel larger than the work area keeps its top left corner on screen.
    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
}
