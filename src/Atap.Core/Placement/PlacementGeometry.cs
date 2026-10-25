namespace Atap.Core.Placement;

/// <summary>Where the notch window goes and how it is shaped.</summary>
/// <param name="Bounds">Window rectangle in physical pixels, shoulders included.</param>
/// <param name="RoundedCorners">Corners away from the screen edge; the others stay square.</param>
/// <param name="Orientation">Horizontal on the top edge, vertical on a side edge.</param>
public readonly record struct NotchGeometry(PixelRect Bounds, NotchCorners RoundedCorners, NotchOrientation Orientation);

/// <summary>Pure placement math: monitor, scale, anchor and content size in, window rectangle out.</summary>
public static class PlacementGeometry
{
    /// <summary>Gap between a corner anchor (TopLeft, TopRight) and the side of the screen, at 100% scale.</summary>
    /// <remarks>
    /// Measured to the notch, not its shoulder. It is larger than <see cref="NotchShape.ShoulderRoom"/> at every
    /// scale, so the shoulders stay on the monitor and the notch sits at the same place in every state.
    /// </remarks>
    public const double CornerMargin = 24;

    /// <summary>
    /// Computes the notch window rectangle on a monitor.
    /// </summary>
    /// <param name="monitorBounds">Monitor area in physical pixels.</param>
    /// <param name="dpiScale">Monitor scale factor, 1.0 at 100%, 1.5 at 150%.</param>
    /// <param name="anchor">Edge position.</param>
    /// <param name="contentWidth">Notch width in device-independent pixels (1/96 inch), without the shoulders.</param>
    /// <param name="contentHeight">Notch height in device-independent pixels, without the shoulders.</param>
    /// <param name="offset">Shift along the edge at 100% scale; see <see cref="NotchPlacement.Offset"/>.</param>
    /// <param name="shoulderRadius">
    /// Room for each shoulder in device-independent pixels. The window grows by it on both ends of the notch
    /// along the edge (left and right on the top edge, top and bottom on a side edge). The notch itself stays put,
    /// unless the offset pushes it against the side of the monitor. Then it stops one room short of that side,
    /// so pass the same room in every state, such as <see cref="NotchShape.ShoulderRoom"/>.
    /// </param>
    public static NotchGeometry Compute(
        PixelRect monitorBounds,
        double dpiScale,
        NotchAnchor anchor,
        double contentWidth,
        double contentHeight,
        double offset = 0,
        double shoulderRadius = 0)
    {
        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
            dpiScale = 1;

        var orientation = OrientationOf(anchor);
        var vertical = orientation == NotchOrientation.Vertical;
        var shoulder = ToPixels(shoulderRadius, dpiScale);
        var width = ToPixels(contentWidth, dpiScale) + (vertical ? 0 : 2 * shoulder);
        var height = ToPixels(contentHeight, dpiScale) + (vertical ? 2 * shoulder : 0);
        var margin = (int)Math.Round(CornerMargin * dpiScale);
        // Positions are summed in long so a huge hand-edited offset cannot wrap around to the other side.
        var shift = double.IsFinite(offset) ? (long)Math.Clamp(Math.Round(offset * dpiScale), int.MinValue, int.MaxValue) : 0L;
        var m = monitorBounds;

        // The corner anchors keep the notch, not its shoulder, at the margin.
        var (x, y) = anchor switch
        {
            NotchAnchor.TopLeft => ((long)m.Left + margin - shoulder + shift, (long)m.Top),
            NotchAnchor.TopRight => ((long)m.Right - margin - width + shoulder + shift, (long)m.Top),
            NotchAnchor.LeftCenter => ((long)m.Left, (long)m.Top + ((long)m.Height - height) / 2 + shift),
            NotchAnchor.RightCenter => ((long)m.Right - width, (long)m.Top + ((long)m.Height - height) / 2 + shift),
            _ => ((long)m.Left + ((long)m.Width - width) / 2 + shift, (long)m.Top),
        };

        // The offset may push the notch along its edge, but never off the monitor. The shoulders count as part of it.
        if (vertical)
            y = Clamp(y, m.Top, (long)m.Bottom - height);
        else
            x = Clamp(x, m.Left, (long)m.Right - width);

        return new NotchGeometry(new PixelRect((int)x, (int)y, width, height), RoundedCornersOf(anchor), orientation);
    }

    public static NotchOrientation OrientationOf(NotchAnchor anchor) =>
        anchor is NotchAnchor.LeftCenter or NotchAnchor.RightCenter
            ? NotchOrientation.Vertical
            : NotchOrientation.Horizontal;

    /// <summary>The corners on the side facing into the screen.</summary>
    public static NotchCorners RoundedCornersOf(NotchAnchor anchor) => anchor switch
    {
        NotchAnchor.LeftCenter => NotchCorners.TopRight | NotchCorners.BottomRight,
        NotchAnchor.RightCenter => NotchCorners.TopLeft | NotchCorners.BottomLeft,
        _ => NotchCorners.BottomLeft | NotchCorners.BottomRight,
    };

    // Rounds away the floating-point noise of dip * scale before rounding up to whole pixels.
    private static int ToPixels(double dips, double scale) =>
        double.IsFinite(dips) && dips > 0 ? (int)Math.Ceiling(Math.Round(dips * scale, 4)) : 0;

    // Like Math.Clamp, but tolerates max < min (content larger than the monitor): min wins.
    private static long Clamp(long value, long min, long max) => Math.Max(min, Math.Min(value, max));
}
