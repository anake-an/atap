namespace Atap.Core.Placement;

/// <summary>
/// Where a large window such as the layout editor opens: on the monitor the notch is on, fully inside its work
/// area, clear of the notch and its badge, and centred across the screen. A window bigger than the room left is
/// made smaller to fit, so its title bar never ends up off screen or under the notch.
/// </summary>
public static class EditorWindowPlacement
{
    /// <summary>The window rectangle in physical pixels.</summary>
    /// <param name="workArea">The work area of the notch's monitor, without the taskbar.</param>
    /// <param name="notch">The notch with its badge in physical pixels, or null when it is hidden or elsewhere.</param>
    /// <param name="dpiScale">The scale of that monitor, 1.0 at 100% and 1.5 at 150%.</param>
    /// <param name="width">The wanted width in device-independent pixels.</param>
    /// <param name="height">The wanted height in device-independent pixels.</param>
    /// <param name="margin">Space kept from the work area's edges and from the notch, in device-independent pixels.</param>
    public static PixelRect Compute(PixelRect workArea, PixelRect? notch, double dpiScale, double width, double height, double margin)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
            dpiScale = 1;
        var gap = ToPixels(margin, dpiScale);

        var left = (long)workArea.Left + gap;
        var top = (long)workArea.Top + gap;
        var right = (long)workArea.Right - gap;
        var bottom = (long)workArea.Bottom - gap;

        if (notch is { } n && Overlaps(n, workArea))
        {
            if (n.Width >= n.Height)
            {
                // On the top edge: start below it.
                top = Math.Max(top, (long)n.Bottom + gap);
            }
            else if ((long)n.Left + n.Width / 2 < (long)workArea.Left + workArea.Width / 2)
            {
                left = Math.Max(left, (long)n.Right + gap);
            }
            else
            {
                right = Math.Min(right, (long)n.Left - gap);
            }
        }

        // A tiny screen has no room left after the margins and the notch. Use the whole work area then.
        if (right - left < 1 || bottom - top < 1)
            (left, top, right, bottom) = (workArea.Left, workArea.Top, workArea.Right, workArea.Bottom);

        var w = Math.Max(1, Math.Min(ToPixels(width, dpiScale), right - left));
        var h = Math.Max(1, Math.Min(ToPixels(height, dpiScale), bottom - top));
        var x = Math.Clamp((long)workArea.Left + ((long)workArea.Width - w) / 2, left, Math.Max(left, right - w));
        var y = Math.Clamp(top + (bottom - top - h) / 2, top, Math.Max(top, bottom - h));
        return new PixelRect((int)x, (int)y, (int)w, (int)h);
    }

    private static bool Overlaps(PixelRect a, PixelRect b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static long ToPixels(double dips, double scale) =>
        double.IsFinite(dips) && dips > 0 ? (long)Math.Ceiling(Math.Round(dips * scale, 4)) : 0;
}
