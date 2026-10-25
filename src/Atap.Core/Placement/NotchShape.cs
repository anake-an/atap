namespace Atap.Core.Placement;

/// <summary>A point in device-independent pixels, from the top left of the notch shape.</summary>
public readonly record struct ShapePoint(double X, double Y);

/// <summary>
/// One piece of the notch outline, running from the end of the previous piece to <see cref="End"/>.
/// It is a straight line, or a quarter circle when <see cref="Radius"/> is above 0.
/// </summary>
/// <param name="End">Where the piece ends.</param>
/// <param name="Radius">Arc radius in device-independent pixels, or 0 for a straight line.</param>
/// <param name="Clockwise">Arc direction as seen on screen, where y grows downward.</param>
public readonly record struct OutlineSegment(ShapePoint End, double Radius = 0, bool Clockwise = false)
{
    public bool IsArc => Radius > 0;
}

/// <summary>The closed outline of the notch and its shoulders, as one shape. The last segment ends at <see cref="Start"/>.</summary>
/// <param name="Width">Shape width in device-independent pixels, shoulder room included.</param>
/// <param name="Height">Shape height in device-independent pixels, shoulder room included.</param>
/// <param name="Start">First point, on the screen edge.</param>
/// <param name="Segments">The pieces in drawing order.</param>
public sealed record NotchOutline(double Width, double Height, ShapePoint Start, IReadOnlyList<OutlineSegment> Segments);

/// <summary>
/// Pure shape math for the notch: rounded on the side facing into the screen, and flared where it meets
/// the screen edge with concave shoulders, so it looks like it grows out of the bezel.
/// </summary>
public static class NotchShape
{
    /// <summary>Radius of the notch's corners away from the screen edge.</summary>
    public const double CornerRadius = 18;

    /// <summary>Shoulder radius in the idle and compact states.</summary>
    public const double IdleShoulderRadius = 15;

    /// <summary>Shoulder radius in the expanded and panel states.</summary>
    public const double ExpandedShoulderRadius = 21;

    /// <summary>
    /// Space kept for each shoulder in every state, in device-independent pixels: 21.9, so 22 pixels at 100% scale.
    /// It fits the expanded radius plus 15% of the radius change, far more than the spring ever overshoots.
    /// Only the radius animates inside it.
    /// </summary>
    /// <remarks>
    /// The room must not change with the state. A notch pushed against the side of the monitor stops one room
    /// short of it, so a room that grew or shrank would move the notch and the shape under the mouse.
    /// </remarks>
    public const double ShoulderRoom = ExpandedShoulderRadius + (ExpandedShoulderRadius - IdleShoulderRadius) * 0.15;

    /// <summary>Shoulder radius for a notch state, in device-independent pixels.</summary>
    public static double ShoulderRadiusFor(NotchState state) =>
        state is NotchState.Expanded or NotchState.Panel ? ExpandedShoulderRadius : IdleShoulderRadius;

    /// <summary>
    /// Builds the outline of a notch of the given size with a shoulder at each end of the side touching the screen edge.
    /// The shape is the notch plus <paramref name="shoulderRoom"/> on both ends along the edge.
    /// </summary>
    /// <param name="anchor">Edge position. It decides which side touches the screen edge.</param>
    /// <param name="notchWidth">Notch width in device-independent pixels, without the shoulders.</param>
    /// <param name="notchHeight">Notch height in device-independent pixels, without the shoulders.</param>
    /// <param name="shoulderRadius">Shoulder radius in device-independent pixels.</param>
    /// <param name="shoulderRoom">
    /// Space kept for each shoulder. Defaults to the radius. It may be larger, as the window keeps
    /// <see cref="ShoulderRoom"/> in every state, and then the shoulder starts further in.
    /// A radius larger than the room is cut to the room.
    /// </param>
    /// <param name="cornerRadius">Radius of the corners away from the screen edge.</param>
    public static NotchOutline Outline(
        NotchAnchor anchor,
        double notchWidth,
        double notchHeight,
        double shoulderRadius,
        double? shoulderRoom = null,
        double cornerRadius = CornerRadius)
    {
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;

        // Work as if on the top edge: u runs along the screen edge, v points away from it.
        var length = NonNegative(vertical ? notchHeight : notchWidth);
        var depth = NonNegative(vertical ? notchWidth : notchHeight);
        var room = NonNegative(shoulderRoom ?? shoulderRadius);
        var shoulder = Math.Min(NonNegative(shoulderRadius), room);
        var corner = Math.Min(NonNegative(cornerRadius), length / 2);

        // A shoulder and a corner share each end of the notch. When the notch is too shallow for both, shrink them evenly.
        if (shoulder + corner > depth)
        {
            var fit = depth / (shoulder + corner);
            shoulder *= fit;
            corner *= fit;
        }

        var near = room;
        var far = room + length;
        var shapeLength = length + 2 * room;

        // Clockwise on screen when drawn on the top edge: along the edge, then down the far side,
        // across the bottom and back up. Corners turn clockwise, shoulders turn the other way.
        var builder = new OutlineBuilder(near - shoulder, 0);
        builder.Line(far + shoulder, 0);
        builder.Arc(far, shoulder, shoulder, clockwise: false);
        builder.Line(far, depth - corner);
        builder.Arc(far - corner, depth, corner, clockwise: true);
        builder.Line(near + corner, depth);
        builder.Arc(near, depth - corner, corner, clockwise: true);
        builder.Line(near, shoulder);
        builder.Arc(near - shoulder, 0, shoulder, clockwise: false);

        return anchor switch
        {
            // Swapping u and v mirrors the shape, so the turns flip.
            NotchAnchor.LeftCenter => builder.Build(depth, shapeLength, (u, v) => new ShapePoint(v, u), mirrored: true),
            // A quarter turn keeps the turns as they are, and the screen edge ends up on the right.
            NotchAnchor.RightCenter => builder.Build(depth, shapeLength, (u, v) => new ShapePoint(depth - v, u), mirrored: false),
            _ => builder.Build(shapeLength, depth, (u, v) => new ShapePoint(u, v), mirrored: false),
        };
    }

    private static double NonNegative(double value) => double.IsFinite(value) && value > 0 ? value : 0;

    /// <summary>Collects segments in top-edge space and drops the ones that draw nothing.</summary>
    private sealed class OutlineBuilder
    {
        // Lines shorter than this are rounding noise.
        private const double Epsilon = 1e-9;

        private readonly double _startU;
        private readonly double _startV;
        private readonly List<(double U, double V, double Radius, bool Clockwise)> _segments = [];
        private double _u;
        private double _v;

        public OutlineBuilder(double startU, double startV)
        {
            _startU = _u = startU;
            _startV = _v = startV;
        }

        public void Line(double u, double v)
        {
            if (Math.Abs(u - _u) < Epsilon && Math.Abs(v - _v) < Epsilon)
                return;
            Add(u, v, 0, false);
        }

        public void Arc(double u, double v, double radius, bool clockwise)
        {
            if (radius <= 0)
                return;
            Add(u, v, radius, clockwise);
        }

        public NotchOutline Build(double width, double height, Func<double, double, ShapePoint> map, bool mirrored) =>
            new(
                width,
                height,
                map(_startU, _startV),
                _segments
                    .Select(s => new OutlineSegment(map(s.U, s.V), s.Radius, s.Radius > 0 && s.Clockwise != mirrored))
                    .ToList());

        private void Add(double u, double v, double radius, bool clockwise)
        {
            _segments.Add((u, v, radius, clockwise));
            _u = u;
            _v = v;
        }
    }
}
