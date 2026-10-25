using System.Windows;
using Atap.Core.Placement;
using Atap.Notch;

namespace Atap.Tests.Notch;

/// <summary>A state change is one step: no flash for content that stays, no window move while the shape springs.</summary>
public class NotchTransitionTests
{
    [Theory]
    [InlineData(NotchContentChange.Swapped, false, true, true, true)]
    [InlineData(NotchContentChange.Refreshed, false, true, false, true)]
    [InlineData(NotchContentChange.Swapped, true, true, false, false)]
    [InlineData(NotchContentChange.Refreshed, true, true, false, false)]
    [InlineData(NotchContentChange.Swapped, false, false, false, false)]
    public void Plan_FadesOnlyNewContent_AndLessMotionChangesAtOnce(NotchContentChange change, bool calm, bool hasSize, bool fade, bool spring)
    {
        Assert.Equal(new NotchTransitionPlan(fade, spring), NotchTransition.Plan(change, calm, hasSize));
    }

    [Fact]
    public void Room_OnTheTopCentre_KeepsItsWidth_SoTheWindowNeverMovesSideways()
    {
        // Idle to open: the width grows with room for the overshoot, the depth too.
        var (during, after) = NotchTransition.Room(default, new Size(120, 33), new Size(648, 200), spring: true, NotchAnchor.TopCenter);
        Assert.Equal(648 + (648 - 120) * NotchTransition.OvershootAllowance, during.Width, 6);
        Assert.Equal(200 + (200 - 33) * NotchTransition.OvershootAllowance, during.Height, 6);
        // After the spring only the free side (the bottom) shrinks back.
        Assert.Equal(during.Width, after.Width);
        Assert.Equal(200, after.Height);

        // Open to idle: the width stays, so the left edge of the window stays.
        var (closing, closed) = NotchTransition.Room(during, new Size(648, 200), new Size(120, 33), spring: true, NotchAnchor.TopCenter);
        Assert.Equal(during.Width, closing.Width);
        Assert.Equal(during.Width, closed.Width);
        Assert.Equal(33, closed.Height);
    }

    [Theory]
    [InlineData(NotchAnchor.TopLeft, false, false)]
    [InlineData(NotchAnchor.TopRight, true, false)]
    [InlineData(NotchAnchor.LeftCenter, false, true)]
    [InlineData(NotchAnchor.RightCenter, true, true)]
    public void Room_KeepsOnlyTheSidesWhoseEdgeWouldMove(NotchAnchor anchor, bool keepsWidth, bool keepsHeight)
    {
        var kept = new Size(500, 500);

        var (_, after) = NotchTransition.Room(kept, new Size(100, 100), new Size(80, 80), spring: false, anchor);

        Assert.Equal(keepsWidth ? 500 : 80, after.Width);
        Assert.Equal(keepsHeight ? 500 : 80, after.Height);
    }

    [Fact]
    public void Margin_CentresTheNotchWhereThePlacementPutsIt()
    {
        var room = new PixelRect(100, 0, 600, 300);

        // Centred in the room: no margin.
        Assert.Equal(new Thickness(0), NotchTransition.Margin(room, new PixelRect(340, 0, 120, 33), HorizontalAlignment.Center, VerticalAlignment.Top, 1));
        // Pushed right by 20 pixels: twice that on the left moves a centred notch by 20, at 150 % that is in DIPs.
        Assert.Equal(new Thickness(40 / 1.5, 0, 0, 0), NotchTransition.Margin(room, new PixelRect(360, 0, 120, 33), HorizontalAlignment.Center, VerticalAlignment.Top, 1.5));
        // Pinned to the right of the room.
        Assert.Equal(new Thickness(0, 0, 10, 0), NotchTransition.Margin(room, new PixelRect(570, 0, 120, 33), HorizontalAlignment.Right, VerticalAlignment.Top, 1));
    }
}
