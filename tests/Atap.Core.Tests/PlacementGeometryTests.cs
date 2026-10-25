using Atap.Core.Placement;

namespace Atap.Core.Tests;

public class PlacementGeometryTests
{
    // 1920x1080 primary at the origin, and a 2560x1440 second monitor to its right, raised 360 px.
    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect Second = new(1920, -360, 2560, 1440);

    private const double ContentWidth = 200;
    private const double ContentHeight = 40;

    private const NotchCorners Bottom = NotchCorners.BottomLeft | NotchCorners.BottomRight;
    private const NotchCorners RightSide = NotchCorners.TopRight | NotchCorners.BottomRight;
    private const NotchCorners LeftSide = NotchCorners.TopLeft | NotchCorners.BottomLeft;

    /// <summary>Monitor, scale, anchor, then the expected rectangle (x, y, width, height).</summary>
    public static TheoryData<string, double, NotchAnchor, int, int, int, int> Cases => new()
    {
        // Primary at 100%: 200x40 px, 24 px corner margin.
        { "primary", 1.0, NotchAnchor.TopLeft, 24, 0, 200, 40 },
        { "primary", 1.0, NotchAnchor.TopCenter, 860, 0, 200, 40 },
        { "primary", 1.0, NotchAnchor.TopRight, 1696, 0, 200, 40 },
        { "primary", 1.0, NotchAnchor.LeftCenter, 0, 520, 200, 40 },
        { "primary", 1.0, NotchAnchor.RightCenter, 1720, 520, 200, 40 },

        // Primary at 150%: 300x60 px, 36 px corner margin.
        { "primary", 1.5, NotchAnchor.TopLeft, 36, 0, 300, 60 },
        { "primary", 1.5, NotchAnchor.TopCenter, 810, 0, 300, 60 },
        { "primary", 1.5, NotchAnchor.TopRight, 1584, 0, 300, 60 },
        { "primary", 1.5, NotchAnchor.LeftCenter, 0, 510, 300, 60 },
        { "primary", 1.5, NotchAnchor.RightCenter, 1620, 510, 300, 60 },

        // Second monitor at 100%.
        { "second", 1.0, NotchAnchor.TopLeft, 1944, -360, 200, 40 },
        { "second", 1.0, NotchAnchor.TopCenter, 3100, -360, 200, 40 },
        { "second", 1.0, NotchAnchor.TopRight, 4256, -360, 200, 40 },
        { "second", 1.0, NotchAnchor.LeftCenter, 1920, 340, 200, 40 },
        { "second", 1.0, NotchAnchor.RightCenter, 4280, 340, 200, 40 },

        // Second monitor at 150%.
        { "second", 1.5, NotchAnchor.TopLeft, 1956, -360, 300, 60 },
        { "second", 1.5, NotchAnchor.TopCenter, 3050, -360, 300, 60 },
        { "second", 1.5, NotchAnchor.TopRight, 4144, -360, 300, 60 },
        { "second", 1.5, NotchAnchor.LeftCenter, 1920, 330, 300, 60 },
        { "second", 1.5, NotchAnchor.RightCenter, 4180, 330, 300, 60 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Compute_PlacesWindowForEveryAnchor(
        string monitor, double scale, NotchAnchor anchor, int x, int y, int width, int height)
    {
        var bounds = monitor == "primary" ? Primary : Second;

        var geometry = PlacementGeometry.Compute(bounds, scale, anchor, ContentWidth, ContentHeight);

        Assert.Equal(new PixelRect(x, y, width, height), geometry.Bounds);
    }

    [Theory]
    [InlineData(NotchAnchor.TopLeft)]
    [InlineData(NotchAnchor.TopCenter)]
    [InlineData(NotchAnchor.TopRight)]
    public void Compute_TopAnchors_AreHorizontalAndFlushWithTheTopEdge(NotchAnchor anchor)
    {
        var geometry = PlacementGeometry.Compute(Second, 1.5, anchor, ContentWidth, ContentHeight);

        Assert.Equal(NotchOrientation.Horizontal, geometry.Orientation);
        Assert.Equal(Bottom, geometry.RoundedCorners);
        Assert.Equal(Second.Top, geometry.Bounds.Top);
    }

    [Fact]
    public void Compute_LeftCenter_IsVerticalFlushLeftAndRoundedOnTheRight()
    {
        var geometry = PlacementGeometry.Compute(Primary, 1.0, NotchAnchor.LeftCenter, 36, 120);

        Assert.Equal(NotchOrientation.Vertical, geometry.Orientation);
        Assert.Equal(RightSide, geometry.RoundedCorners);
        Assert.Equal(new PixelRect(0, 480, 36, 120), geometry.Bounds);
    }

    [Fact]
    public void Compute_RightCenter_IsVerticalFlushRightAndRoundedOnTheLeft()
    {
        var geometry = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.RightCenter, 36, 120);

        Assert.Equal(NotchOrientation.Vertical, geometry.Orientation);
        Assert.Equal(LeftSide, geometry.RoundedCorners);
        Assert.Equal(Second.Right, geometry.Bounds.Right);
        Assert.Equal(new PixelRect(4426, 270, 54, 180), geometry.Bounds);
    }

    [Fact]
    public void Compute_OffsetShiftsAlongTheEdgeAndScalesWithDpi()
    {
        var top = PlacementGeometry.Compute(Primary, 1.5, NotchAnchor.TopCenter, ContentWidth, ContentHeight, offset: 100);
        var side = PlacementGeometry.Compute(Primary, 1.5, NotchAnchor.LeftCenter, ContentWidth, ContentHeight, offset: -100);

        Assert.Equal(new PixelRect(960, 0, 300, 60), top.Bounds);
        Assert.Equal(new PixelRect(0, 360, 300, 60), side.Bounds);
    }

    [Fact]
    public void Compute_LargeOffset_KeepsTheNotchOnTheMonitor()
    {
        var right = PlacementGeometry.Compute(Second, 1.0, NotchAnchor.TopRight, ContentWidth, ContentHeight, offset: 5000);
        var left = PlacementGeometry.Compute(Second, 1.0, NotchAnchor.TopLeft, ContentWidth, ContentHeight, offset: -5000);
        var down = PlacementGeometry.Compute(Second, 1.0, NotchAnchor.RightCenter, ContentWidth, ContentHeight, offset: 5000);

        Assert.Equal(Second.Right - 200, right.Bounds.Left);
        Assert.Equal(Second.Left, left.Bounds.Left);
        Assert.Equal(Second.Bottom - 40, down.Bounds.Top);
    }

    [Fact]
    public void Compute_ExtremeOffset_PinsToTheNearEdgeWithoutWrapping()
    {
        // A monitor left of the primary, at negative x.
        var left = new PixelRect(-2560, 0, 2560, 1440);

        var farRight = PlacementGeometry.Compute(Primary, 1.5, NotchAnchor.TopCenter, ContentWidth, ContentHeight, offset: int.MaxValue);
        var farLeft = PlacementGeometry.Compute(left, 1.0, NotchAnchor.TopCenter, ContentWidth, ContentHeight, offset: int.MinValue);
        var farDown = PlacementGeometry.Compute(left, 1.0, NotchAnchor.LeftCenter, ContentWidth, ContentHeight, offset: double.MaxValue);
        var farUp = PlacementGeometry.Compute(Second, 1.0, NotchAnchor.RightCenter, ContentWidth, ContentHeight, offset: double.MinValue);

        Assert.Equal(Primary.Right - 300, farRight.Bounds.Left);
        Assert.Equal(left.Left, farLeft.Bounds.Left);
        Assert.Equal(left.Bottom - 40, farDown.Bounds.Top);
        Assert.Equal(Second.Top, farUp.Bounds.Top);
    }

    [Fact]
    public void Compute_FractionalScale_RoundsSizeUpToWholePixels()
    {
        var geometry = PlacementGeometry.Compute(Primary, 1.25, NotchAnchor.TopCenter, 121, 30);

        Assert.Equal(152, geometry.Bounds.Width);
        Assert.Equal(38, geometry.Bounds.Height);
    }

    [Fact]
    public void Compute_InvalidScale_FallsBackTo100Percent()
    {
        var geometry = PlacementGeometry.Compute(Primary, 0, NotchAnchor.TopCenter, ContentWidth, ContentHeight);

        Assert.Equal(new PixelRect(860, 0, 200, 40), geometry.Bounds);
    }

    /// <summary>Scale, shoulder radius, anchor, then the expected window rectangle with shoulders (x, y, width, height).</summary>
    public static TheoryData<double, double, NotchAnchor, int, int, int, int> ShoulderCases => new()
    {
        // 100%, radius 15: 15 px shoulders. The notch stays at 24 px from the side for the corner anchors.
        { 1.0, 15, NotchAnchor.TopLeft, 9, 0, 230, 40 },
        { 1.0, 15, NotchAnchor.TopCenter, 845, 0, 230, 40 },
        { 1.0, 15, NotchAnchor.TopRight, 1681, 0, 230, 40 },
        { 1.0, 15, NotchAnchor.LeftCenter, 0, 505, 200, 70 },
        { 1.0, 15, NotchAnchor.RightCenter, 1720, 505, 200, 70 },

        // 100%, radius 21: 21 px shoulders.
        { 1.0, 21, NotchAnchor.TopLeft, 3, 0, 242, 40 },
        { 1.0, 21, NotchAnchor.TopCenter, 839, 0, 242, 40 },
        { 1.0, 21, NotchAnchor.TopRight, 1675, 0, 242, 40 },
        { 1.0, 21, NotchAnchor.LeftCenter, 0, 499, 200, 82 },
        { 1.0, 21, NotchAnchor.RightCenter, 1720, 499, 200, 82 },

        // 150%, radius 15: 22.5 px rounds up to 23 px shoulders on a 300x60 px notch, 36 px corner margin.
        { 1.5, 15, NotchAnchor.TopLeft, 13, 0, 346, 60 },
        { 1.5, 15, NotchAnchor.TopCenter, 787, 0, 346, 60 },
        { 1.5, 15, NotchAnchor.TopRight, 1561, 0, 346, 60 },
        { 1.5, 15, NotchAnchor.LeftCenter, 0, 487, 300, 106 },
        { 1.5, 15, NotchAnchor.RightCenter, 1620, 487, 300, 106 },

        // 150%, radius 21: 31.5 px rounds up to 32 px shoulders.
        { 1.5, 21, NotchAnchor.TopLeft, 4, 0, 364, 60 },
        { 1.5, 21, NotchAnchor.TopCenter, 778, 0, 364, 60 },
        { 1.5, 21, NotchAnchor.TopRight, 1552, 0, 364, 60 },
        { 1.5, 21, NotchAnchor.LeftCenter, 0, 478, 300, 124 },
        { 1.5, 21, NotchAnchor.RightCenter, 1620, 478, 300, 124 },
    };

    [Theory]
    [MemberData(nameof(ShoulderCases))]
    public void Compute_WithShoulders_GrowsTheWindowAlongTheEdge(
        double scale, double radius, NotchAnchor anchor, int x, int y, int width, int height)
    {
        var geometry = PlacementGeometry.Compute(Primary, scale, anchor, ContentWidth, ContentHeight, shoulderRadius: radius);

        Assert.Equal(new PixelRect(x, y, width, height), geometry.Bounds);
    }

    public static TheoryData<NotchAnchor, double, double> AnchorScaleRadius()
    {
        var data = new TheoryData<NotchAnchor, double, double>();
        foreach (var anchor in Enum.GetValues<NotchAnchor>())
            foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
                foreach (var radius in new[] { NotchShape.IdleShoulderRadius, NotchShape.ExpandedShoulderRadius, NotchShape.ShoulderRoom })
                    data.Add(anchor, scale, radius);
        return data;
    }

    [Theory]
    [MemberData(nameof(AnchorScaleRadius))]
    public void Compute_Shoulders_DoNotMoveTheNotch(NotchAnchor anchor, double scale, double radius)
    {
        var plain = PlacementGeometry.Compute(Second, scale, anchor, ContentWidth, ContentHeight).Bounds;
        var withShoulders = PlacementGeometry.Compute(Second, scale, anchor, ContentWidth, ContentHeight, shoulderRadius: radius).Bounds;

        Assert.Equal(plain, WithoutShoulders(withShoulders, anchor, scale, radius));
    }

    [Theory]
    [MemberData(nameof(AnchorScaleRadius))]
    public void Compute_Shoulders_StayOnTheMonitor(NotchAnchor anchor, double scale, double radius)
    {
        var bounds = PlacementGeometry.Compute(Second, scale, anchor, ContentWidth, ContentHeight, shoulderRadius: radius).Bounds;

        Assert.InRange(bounds.Left, Second.Left, Second.Right);
        Assert.InRange(bounds.Right, Second.Left, Second.Right);
        Assert.InRange(bounds.Top, Second.Top, Second.Bottom);
        Assert.InRange(bounds.Bottom, Second.Top, Second.Bottom);
    }

    [Fact]
    public void Compute_CornerAnchorsAtDefaultOffset_LeaveAGapBesideTheShoulder()
    {
        // 150% and the large radius: 36 px margin minus a 32 px shoulder.
        var left = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.TopLeft, ContentWidth, ContentHeight, shoulderRadius: 21);
        var right = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.TopRight, ContentWidth, ContentHeight, shoulderRadius: 21);

        Assert.Equal(Second.Left + 4, left.Bounds.Left);
        Assert.Equal(Second.Right - 4, right.Bounds.Right);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    [InlineData(2.25)]
    [InlineData(2.5)]
    [InlineData(3.0)]
    public void Compute_CornerAnchors_KeepTheShoulderRoomOnTheMonitorAtDefaultOffset(double scale)
    {
        // The corner margin is wider than the room, so the notch stays at the margin and nothing is pushed in.
        var room = NotchShape.ShoulderRoom;
        var left = PlacementGeometry.Compute(Second, scale, NotchAnchor.TopLeft, ContentWidth, ContentHeight, shoulderRadius: room).Bounds;
        var right = PlacementGeometry.Compute(Second, scale, NotchAnchor.TopRight, ContentWidth, ContentHeight, shoulderRadius: room).Bounds;
        var margin = (int)Math.Round(PlacementGeometry.CornerMargin * scale);

        Assert.True(left.Left >= Second.Left);
        Assert.True(right.Right <= Second.Right);
        Assert.Equal(Second.Left + margin, WithoutShoulders(left, NotchAnchor.TopLeft, scale, room).Left);
        Assert.Equal(Second.Right - margin, WithoutShoulders(right, NotchAnchor.TopRight, scale, room).Right);
    }

    [Fact]
    public void Compute_NotchPushedIntoAMonitorCorner_KeepsTheShoulderOnTheMonitor()
    {
        // 32 px shoulders at 150%. The window, shoulders included, stops at the monitor edge,
        // so the notch itself stops one shoulder short of it.
        var left = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.TopLeft, ContentWidth, ContentHeight, offset: -5000, shoulderRadius: 21);
        var right = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.TopRight, ContentWidth, ContentHeight, offset: 5000, shoulderRadius: 21);
        var up = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.LeftCenter, ContentWidth, ContentHeight, offset: -5000, shoulderRadius: 21);
        var down = PlacementGeometry.Compute(Second, 1.5, NotchAnchor.RightCenter, ContentWidth, ContentHeight, offset: 5000, shoulderRadius: 21);

        Assert.Equal(new PixelRect(Second.Left, Second.Top, 364, 60), left.Bounds);
        Assert.Equal(new PixelRect(Second.Right - 364, Second.Top, 364, 60), right.Bounds);
        Assert.Equal(new PixelRect(Second.Left, Second.Top, 300, 124), up.Bounds);
        Assert.Equal(new PixelRect(Second.Right - 300, Second.Bottom - 124, 300, 124), down.Bounds);
    }

    [Fact]
    public void Compute_TopCenterPushedToTheSide_KeepsTheShoulderOnTheMonitor()
    {
        var right = PlacementGeometry.Compute(Primary, 1.0, NotchAnchor.TopCenter, ContentWidth, ContentHeight, offset: 5000, shoulderRadius: 15);
        var left = PlacementGeometry.Compute(Primary, 1.0, NotchAnchor.TopCenter, ContentWidth, ContentHeight, offset: -5000, shoulderRadius: 15);

        Assert.Equal(Primary.Right, right.Bounds.Right);
        Assert.Equal(Primary.Left, left.Bounds.Left);
    }

    [Fact]
    public void Compute_TopLeftPushedIntoTheCorner_HoldsTheNotchOneRoomFromTheSideInEveryState()
    {
        // The window keeps 21.9 DIPs of shoulder room in every state, 22 px at 100%.
        // An idle 120x30 notch and an expanded 360x120 one both start 22 px from the side.
        var idle = PlacementGeometry.Compute(Primary, 1.0, NotchAnchor.TopLeft, 120, 30, offset: -5000, shoulderRadius: NotchShape.ShoulderRoom);
        var expanded = PlacementGeometry.Compute(Primary, 1.0, NotchAnchor.TopLeft, 360, 120, offset: -5000, shoulderRadius: NotchShape.ShoulderRoom);

        Assert.Equal(new PixelRect(0, 0, 164, 30), idle.Bounds);
        Assert.Equal(new PixelRect(0, 0, 404, 120), expanded.Bounds);
    }

    public static TheoryData<NotchAnchor, double, double> AnchorPushOffsetScale()
    {
        var data = new TheoryData<NotchAnchor, double, double>();
        foreach (var anchor in Enum.GetValues<NotchAnchor>())
            foreach (var offset in new[] { -5000.0, 5000.0 })
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                    data.Add(anchor, offset, scale);
        return data;
    }

    [Theory]
    [MemberData(nameof(AnchorPushOffsetScale))]
    public void Compute_PushedAgainstAMonitorSide_KeepsTheNotchStillFromIdleToExpanded(NotchAnchor anchor, double offset, double scale)
    {
        var vertical = PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical;
        var (idleWidth, idleHeight) = vertical ? (30.0, 120.0) : (120.0, 30.0);
        var (expandedWidth, expandedHeight) = vertical ? (120.0, 360.0) : (360.0, 120.0);
        var room = NotchShape.ShoulderRoom;

        var idle = WithoutShoulders(
            PlacementGeometry.Compute(Second, scale, anchor, idleWidth, idleHeight, offset, room).Bounds, anchor, scale, room);
        var expanded = WithoutShoulders(
            PlacementGeometry.Compute(Second, scale, anchor, expandedWidth, expandedHeight, offset, room).Bounds, anchor, scale, room);

        // The notch keeps the edge it was pushed toward one room short of the monitor side, whatever its size.
        var roomPixels = (int)Math.Ceiling(Math.Round(room * scale, 4));
        Func<PixelRect, int> pushedEdge = (vertical, offset < 0) switch
        {
            (false, true) => r => r.Left - Second.Left,
            (false, false) => r => Second.Right - r.Right,
            (true, true) => r => r.Top - Second.Top,
            (true, false) => r => Second.Bottom - r.Bottom,
        };

        Assert.Equal(roomPixels, pushedEdge(idle));
        Assert.Equal(roomPixels, pushedEdge(expanded));
    }

    [Fact]
    public void Compute_FractionalScale_RoundsShouldersUpToWholePixels()
    {
        // 15 DIPs at 125% is 18.75 px, so 19 px on each side of a 250 px notch, 30 px from the side.
        var geometry = PlacementGeometry.Compute(Primary, 1.25, NotchAnchor.TopLeft, ContentWidth, ContentHeight, shoulderRadius: 15);

        Assert.Equal(new PixelRect(11, 0, 288, 50), geometry.Bounds);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-10)]
    [InlineData(0)]
    public void Compute_InvalidShoulderRadius_AddsNoShoulders(double radius)
    {
        var plain = PlacementGeometry.Compute(Primary, 1.5, NotchAnchor.TopLeft, ContentWidth, ContentHeight);
        var geometry = PlacementGeometry.Compute(Primary, 1.5, NotchAnchor.TopLeft, ContentWidth, ContentHeight, shoulderRadius: radius);

        Assert.Equal(plain.Bounds, geometry.Bounds);
    }

    // The notch rectangle inside a window with shoulders, using the same whole-pixel rounding as Compute.
    private static PixelRect WithoutShoulders(PixelRect bounds, NotchAnchor anchor, double scale, double radius)
    {
        var shoulder = (int)Math.Ceiling(Math.Round(radius * scale, 4));
        return PlacementGeometry.OrientationOf(anchor) == NotchOrientation.Vertical
            ? new PixelRect(bounds.Left, bounds.Top + shoulder, bounds.Width, bounds.Height - 2 * shoulder)
            : new PixelRect(bounds.Left + shoulder, bounds.Top, bounds.Width - 2 * shoulder, bounds.Height);
    }
}
