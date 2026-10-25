using Atap.Core.Placement;

namespace Atap.Core.Tests.Placement;

public class PopupPlacementTests
{
    // A 1920 by 1080 screen with a 48 px taskbar at the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    [Fact]
    public void FromTheTaskbar_OpensAboveIt_CentredOnThePointer()
    {
        var corner = PopupPlacement.Near(new PixelPoint(1500, 1060), Work, 360, 400, 12);

        Assert.Equal(new PixelPoint(1320, 620), corner);
    }

    [Fact]
    public void FromTheNotch_OpensBelowThePointer()
    {
        var corner = PopupPlacement.Near(new PixelPoint(960, 20), Work, 360, 400, 12);

        Assert.Equal(new PixelPoint(780, 32), corner);
    }

    [Fact]
    public void NearTheRightEdge_StaysOnScreen()
    {
        var corner = PopupPlacement.Near(new PixelPoint(1915, 1060), Work, 360, 400, 12);

        Assert.Equal(1920 - 360 - 12, corner.X);
    }

    [Fact]
    public void WithTheTaskbarOnTheLeft_OpensBesideIt()
    {
        var work = new PixelRect(64, 0, 1856, 1080);

        var corner = PopupPlacement.Near(new PixelPoint(30, 900), work, 360, 400, 12);

        Assert.Equal(64 + 12, corner.X);
        Assert.Equal(900 - 400 - 12, corner.Y);
    }

    [Fact]
    public void OnASecondMonitor_UsesItsOwnWorkArea()
    {
        var work = new PixelRect(-1280, 0, 1280, 984);

        var corner = PopupPlacement.Near(new PixelPoint(-100, 1000), work, 360, 400, 12);

        Assert.Equal(new PixelPoint(-360 - 12, 984 - 400 - 12), corner);
    }

    [Fact]
    public void APanelTallerThanTheScreen_KeepsItsTopOnScreen()
    {
        var corner = PopupPlacement.Near(new PixelPoint(960, 1060), new PixelRect(0, 0, 1920, 300), 360, 400, 12);

        Assert.Equal(12, corner.Y);
    }
}
