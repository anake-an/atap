using Atap.Core.Placement;

namespace Atap.Core.Tests;

public class NotchShapeTests
{
    private const double Tolerance = 1e-9;

    [Theory]
    [InlineData(NotchState.Idle, 15)]
    [InlineData(NotchState.Compact, 15)]
    [InlineData(NotchState.Expanded, 21)]
    [InlineData(NotchState.Panel, 21)]
    public void ShoulderRadiusFor_IsSmallAtRestAndLargerWhenOpen(NotchState state, double radius)
    {
        Assert.Equal(radius, NotchShape.ShoulderRadiusFor(state));
    }

    [Fact]
    public void ShoulderRoom_Is22PixelsAt100Percent()
    {
        Assert.Equal(21.9, NotchShape.ShoulderRoom, Tolerance);
        Assert.Equal(22, Math.Ceiling(Math.Round(NotchShape.ShoulderRoom, 4)));
    }

    [Theory]
    [InlineData(NotchAnchor.TopLeft)]
    [InlineData(NotchAnchor.TopCenter)]
    [InlineData(NotchAnchor.TopRight)]
    [InlineData(NotchAnchor.LeftCenter)]
    [InlineData(NotchAnchor.RightCenter)]
    public void Outline_RealNotchSizes_IsOneShapeWithNoSeam(NotchAnchor anchor)
    {
        // An idle 120x33 notch and an expanded 360x120 one, in the room the window keeps.
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        foreach (var (length, depth, radius) in new[] { (120.0, 32.0, NotchShape.IdleShoulderRadius), (360.0, 120.0, NotchShape.ExpandedShoulderRadius) })
        {
            var outline = NotchShape.Outline(anchor, vertical ? depth : length, vertical ? length : depth, radius, NotchShape.ShoulderRoom);

            // One closed figure: every piece starts where the last one ended, and the last ends at the start.
            Assert.Equal(outline.Start, outline.Segments[^1].End);
            Assert.Equal(4, outline.Segments.Count(s => s.IsArc));
            AssertQuarterCircles(outline);
        }
    }

    [Theory]
    [InlineData(NotchAnchor.TopLeft)]
    [InlineData(NotchAnchor.TopCenter)]
    [InlineData(NotchAnchor.TopRight)]
    public void Outline_TopAnchor_FlaresIntoTheTopEdgeOnBothSides(NotchAnchor anchor)
    {
        // A 200x40 notch with 10 px shoulders: the shape is 220 wide, the notch runs from x 10 to 210.
        var outline = NotchShape.Outline(anchor, 200, 40, 10);

        Assert.Equal(220, outline.Width);
        Assert.Equal(40, outline.Height);
        Assert.Equal(new ShapePoint(0, 0), outline.Start);
        Assert.Equal(
            [
                Line(220, 0),
                Shoulder(210, 10, 10, clockwise: false),
                Line(210, 22),
                Corner(192, 40, clockwise: true),
                Line(28, 40),
                Corner(10, 22, clockwise: true),
                Line(10, 10),
                Shoulder(0, 0, 10, clockwise: false),
            ],
            outline.Segments);
    }

    [Fact]
    public void Outline_LeftCenter_FlaresIntoTheLeftEdgeAtTopAndBottom()
    {
        // A 36x120 vertical notch with 14 px shoulders: the shape is 148 tall, the notch runs from y 14 to 134.
        var outline = NotchShape.Outline(NotchAnchor.LeftCenter, 36, 120, 14);

        Assert.Equal(36, outline.Width);
        Assert.Equal(148, outline.Height);
        Assert.Equal(new ShapePoint(0, 0), outline.Start);
        Assert.Equal(
            [
                Line(0, 148),
                Shoulder(14, 134, 14, clockwise: true),
                Line(18, 134),
                Corner(36, 116, clockwise: false),
                Line(36, 32),
                Corner(18, 14, clockwise: false),
                Line(14, 14),
                Shoulder(0, 0, 14, clockwise: true),
            ],
            outline.Segments);
    }

    [Fact]
    public void Outline_RightCenter_FlaresIntoTheRightEdgeAtTopAndBottom()
    {
        var outline = NotchShape.Outline(NotchAnchor.RightCenter, 36, 120, 14);

        Assert.Equal(36, outline.Width);
        Assert.Equal(148, outline.Height);
        Assert.Equal(new ShapePoint(36, 0), outline.Start);
        Assert.Equal(
            [
                Line(36, 148),
                Shoulder(22, 134, 14, clockwise: false),
                Line(18, 134),
                Corner(0, 116, clockwise: true),
                Line(0, 32),
                Corner(18, 14, clockwise: true),
                Line(22, 14),
                Shoulder(36, 0, 14, clockwise: false),
            ],
            outline.Segments);
    }

    public static TheoryData<NotchAnchor, double> AnchorAndRadius()
    {
        var data = new TheoryData<NotchAnchor, double>();
        foreach (var anchor in Enum.GetValues<NotchAnchor>())
        {
            data.Add(anchor, NotchShape.IdleShoulderRadius);
            data.Add(anchor, NotchShape.ExpandedShoulderRadius);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AnchorAndRadius))]
    public void Outline_IsOneClosedShapeInsideItsBox(NotchAnchor anchor, double radius)
    {
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        var (width, height) = vertical ? (40.0, 160.0) : (160.0, 40.0);

        var outline = NotchShape.Outline(anchor, width, height, radius);

        // The shoulders add room on both ends along the edge only.
        Assert.Equal(vertical ? width : width + 2 * radius, outline.Width);
        Assert.Equal(vertical ? height + 2 * radius : height, outline.Height);

        // It ends where it starts, and every point is inside the shape's box.
        Assert.Equal(outline.Start, outline.Segments[^1].End);
        foreach (var point in Points(outline))
        {
            Assert.InRange(point.X, -Tolerance, outline.Width + Tolerance);
            Assert.InRange(point.Y, -Tolerance, outline.Height + Tolerance);
        }

        // Two shoulders and two rounded corners, each a true quarter circle.
        Assert.Equal(2, outline.Segments.Count(s => s.IsArc && s.Radius == radius));
        Assert.Equal(2, outline.Segments.Count(s => s.IsArc && s.Radius == NotchShape.CornerRadius));
        AssertQuarterCircles(outline);
    }

    [Theory]
    [MemberData(nameof(AnchorAndRadius))]
    public void Outline_ShouldersStartOnTheScreenEdge(NotchAnchor anchor, double radius)
    {
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        var outline = NotchShape.Outline(anchor, vertical ? 40 : 160, vertical ? 160 : 40, radius);

        // The screen edge is the shape's top for top anchors, and its left or right side for the side anchors.
        Func<ShapePoint, bool> onEdge = anchor switch
        {
            NotchAnchor.LeftCenter => p => p.X == 0,
            NotchAnchor.RightCenter => p => p.X == outline.Width,
            _ => p => p.Y == 0,
        };

        var points = Points(outline).ToList();
        var shoulders = Enumerable.Range(1, outline.Segments.Count)
            .Where(i => outline.Segments[i - 1].Radius == radius)
            .Select(i => (From: points[i - 1], To: points[i]))
            .ToList();

        // Each shoulder has one end on the screen edge and the other on the side of the notch.
        Assert.Equal(2, shoulders.Count);
        Assert.All(shoulders, s => Assert.True(onEdge(s.From) ^ onEdge(s.To)));
    }

    [Fact]
    public void Outline_ShallowNotch_ShrinksShoulderAndCornerEvenlyToFit()
    {
        // 14 + 18 = 32 does not fit in 24, so both shrink by 24 / 32.
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, 200, 24, 14);

        var radii = outline.Segments.Where(s => s.IsArc).Select(s => s.Radius).ToList();
        Assert.Equal([10.5, 13.5, 13.5, 10.5], radii);
        Assert.Equal(new ShapePoint(3.5, 0), outline.Start);
        AssertQuarterCircles(outline);
    }

    [Fact]
    public void Outline_ShortNotch_LimitsTheCornersToHalfItsLength()
    {
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, 20, 40, 10);

        Assert.Equal([10, 10, 10, 10], outline.Segments.Where(s => s.IsArc).Select(s => s.Radius));
        AssertQuarterCircles(outline);
    }

    [Fact]
    public void Outline_NoShoulder_IsTheRoundedNotchOnly()
    {
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, 200, 40, 0);

        Assert.Equal(200, outline.Width);
        Assert.Equal(new ShapePoint(0, 0), outline.Start);
        Assert.Equal(
            [
                Line(200, 0),
                Line(200, 22),
                Corner(182, 40, clockwise: true),
                Line(18, 40),
                Corner(0, 22, clockwise: true),
                Line(0, 0),
            ],
            outline.Segments);
    }

    [Fact]
    public void Outline_RoomLargerThanRadius_StartsTheShoulderFurtherIn()
    {
        // The window keeps room for the largest shoulder, so a small one starts further in.
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, 200, 40, 10, shoulderRoom: 14);

        Assert.Equal(228, outline.Width);
        Assert.Equal(new ShapePoint(4, 0), outline.Start);
        Assert.Equal(Line(224, 0), outline.Segments[0]);
        Assert.Equal(Shoulder(214, 10, 10, clockwise: false), outline.Segments[1]);
    }

    [Fact]
    public void Outline_RadiusLargerThanRoom_IsCutToTheRoom()
    {
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, 200, 40, 14, shoulderRoom: 10);

        Assert.Equal(220, outline.Width);
        Assert.Equal(new ShapePoint(0, 0), outline.Start);
        Assert.Equal(2, outline.Segments.Count(s => s.IsArc && s.Radius == 10));
    }

    [Theory]
    [InlineData(NotchAnchor.TopLeft)]
    [InlineData(NotchAnchor.TopCenter)]
    [InlineData(NotchAnchor.TopRight)]
    [InlineData(NotchAnchor.LeftCenter)]
    [InlineData(NotchAnchor.RightCenter)]
    public void Outline_ShoulderRoom_FitsEveryRadiusWithoutMovingTheNotch(NotchAnchor anchor)
    {
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        var (width, height) = vertical ? (40.0, 160.0) : (160.0, 40.0);

        var idle = NotchShape.Outline(anchor, width, height, NotchShape.IdleShoulderRadius, NotchShape.ShoulderRoom);
        var expanded = NotchShape.Outline(anchor, width, height, NotchShape.ExpandedShoulderRadius, NotchShape.ShoulderRoom);

        // The room holds the expanded radius whole, with space left for the spring to overshoot it.
        Assert.True(NotchShape.ShoulderRoom > NotchShape.ExpandedShoulderRadius);
        Assert.Equal(2, expanded.Segments.Count(s => s.IsArc && s.Radius == NotchShape.ExpandedShoulderRadius));

        // The box and the rounded corners away from the screen edge stay put. Only the shoulders change.
        Assert.Equal(idle.Width, expanded.Width);
        Assert.Equal(idle.Height, expanded.Height);
        Assert.Equal(Corners(idle), Corners(expanded));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5)]
    public void Outline_InvalidSizes_GiveAnEmptyShape(double size)
    {
        var outline = NotchShape.Outline(NotchAnchor.TopCenter, size, size, size);

        Assert.Equal(0, outline.Width);
        Assert.Equal(0, outline.Height);
        Assert.Empty(outline.Segments);
    }

    private static OutlineSegment Line(double x, double y) => new(new ShapePoint(x, y));

    private static OutlineSegment Shoulder(double x, double y, double radius, bool clockwise) =>
        new(new ShapePoint(x, y), radius, clockwise);

    private static OutlineSegment Corner(double x, double y, bool clockwise) =>
        new(new ShapePoint(x, y), NotchShape.CornerRadius, clockwise);

    private static IEnumerable<ShapePoint> Points(NotchOutline outline) =>
        outline.Segments.Select(s => s.End).Prepend(outline.Start);

    private static List<OutlineSegment> Corners(NotchOutline outline) =>
        outline.Segments.Where(s => s.Radius == NotchShape.CornerRadius).ToList();

    // A quarter circle moves exactly one radius across and one radius along.
    private static void AssertQuarterCircles(NotchOutline outline)
    {
        var from = outline.Start;
        foreach (var segment in outline.Segments)
        {
            if (segment.IsArc)
            {
                Assert.Equal(segment.Radius, Math.Abs(segment.End.X - from.X), Tolerance);
                Assert.Equal(segment.Radius, Math.Abs(segment.End.Y - from.Y), Tolerance);
            }
            from = segment.End;
        }
    }
}
