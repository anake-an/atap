using Atap.Core.Placement;

namespace Atap.Core.Tests.Placement;

public class EditorWindowPlacementTests
{
    // A 1920 by 1080 screen with a 48 px taskbar at the bottom, and the notch with its badge at the top centre.
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);
    private static readonly PixelRect TopNotch = new(800, 0, 320, 48);

    [Fact]
    public void OpensBelowTheNotch_CentredAcross()
    {
        var rect = EditorWindowPlacement.Compute(Work, TopNotch, 1.0, 1120, 760, 16);

        Assert.Equal(new PixelRect(400, 160, 1120, 760), rect);
        Assert.True(rect.Top >= TopNotch.Bottom + 16);
    }

    [Fact]
    public void OnASecondMonitorAtAnotherScale_UsesThatMonitorsPixels()
    {
        // A 2560 by 1440 monitor at 150%, right of the main one and higher up, with its taskbar at the bottom.
        var work = new PixelRect(1920, -200, 2560, 1400);
        var notch = new PixelRect(3100, -200, 420, 72);

        var rect = EditorWindowPlacement.Compute(work, notch, 1.5, 1120, 760, 16);

        Assert.Equal(1680, rect.Width);
        Assert.Equal(1140, rect.Height);
        Assert.Equal(1920 + (2560 - 1680) / 2, rect.Left);
        Assert.True(rect.Top >= notch.Bottom + 24, "The title bar must sit below the notch and its badge.");
        AssertInside(work, rect);
    }

    [Fact]
    public void OnASmallScreen_ShrinksToFit_BelowTheNotch()
    {
        // 1366 by 768 at 125%, a 40 px taskbar.
        var work = new PixelRect(0, 0, 1366, 728);
        var notch = new PixelRect(523, 0, 320, 60);

        var rect = EditorWindowPlacement.Compute(work, notch, 1.25, 1120, 760, 16);

        Assert.Equal(1366 - 2 * 20, rect.Width);
        Assert.Equal(728 - 20 - (60 + 20), rect.Height);
        Assert.Equal(80, rect.Top);
        AssertInside(work, rect);
    }

    [Fact]
    public void ATaskbarOnTop_KeepsTheWindowBelowIt()
    {
        var work = new PixelRect(0, 48, 1920, 1032);

        var rect = EditorWindowPlacement.Compute(work, TopNotch, 1.0, 1120, 760, 16);

        Assert.True(rect.Top >= 48 + 16);
        AssertInside(work, rect);
    }

    [Fact]
    public void ANotchOnTheLeftEdge_PushesTheWindowRight()
    {
        var notch = new PixelRect(0, 376, 48, 280);

        var rect = EditorWindowPlacement.Compute(Work, notch, 1.0, 1800, 760, 16);

        Assert.True(rect.Left >= notch.Right + 16);
        AssertInside(Work, rect);
    }

    [Fact]
    public void ANotchOnTheRightEdge_KeepsTheWindowLeftOfIt()
    {
        var notch = new PixelRect(1872, 376, 48, 280);

        var rect = EditorWindowPlacement.Compute(Work, notch, 1.0, 1800, 760, 16);

        Assert.True(rect.Right <= notch.Left - 16);
        AssertInside(Work, rect);
    }

    [Fact]
    public void ANotchOnAnotherMonitor_IsIgnored()
    {
        var elsewhere = new PixelRect(2720, 0, 320, 48);

        var rect = EditorWindowPlacement.Compute(Work, elsewhere, 1.0, 1120, 760, 16);

        Assert.Equal(new PixelRect(400, 136, 1120, 760), rect);
    }

    [Fact]
    public void NoRoomLeft_UsesTheWholeWorkArea()
    {
        var work = new PixelRect(0, 0, 300, 200);

        var rect = EditorWindowPlacement.Compute(work, new PixelRect(0, 0, 300, 190), 1.0, 1120, 760, 16);

        Assert.Equal(work, rect);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void ABadScale_CountsAs100Percent(double scale)
    {
        var rect = EditorWindowPlacement.Compute(Work, TopNotch, scale, 1120, 760, 16);

        Assert.Equal(new PixelRect(400, 160, 1120, 760), rect);
    }

    private static void AssertInside(PixelRect work, PixelRect rect)
    {
        Assert.InRange(rect.Left, work.Left, work.Right);
        Assert.InRange(rect.Right, work.Left, work.Right);
        Assert.InRange(rect.Top, work.Top, work.Bottom);
        Assert.InRange(rect.Bottom, work.Top, work.Bottom);
    }
}
