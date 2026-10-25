using Atap.Core.Notch;

namespace Atap.Core.Tests.Notch;

public class WheelPagerTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void OneTiltNotch_StepsOnePage()
    {
        var pager = new WheelPager();

        Assert.Equal(1, pager.Add(120, Ms(0)));
    }

    [Fact]
    public void ToTheLeft_StepsBack()
    {
        var pager = new WheelPager();

        Assert.Equal(-1, pager.Add(-120, Ms(0)));
    }

    [Fact]
    public void SmallTouchpadAmounts_AddUpToOneStep()
    {
        var pager = new WheelPager();

        Assert.Equal(0, pager.Add(40, Ms(0)));
        Assert.Equal(0, pager.Add(40, Ms(16)));
        Assert.Equal(1, pager.Add(40, Ms(32)));
    }

    [Fact]
    public void TheRestOfASwipe_DoesNotTurnAnotherPage()
    {
        var pager = new WheelPager();

        Assert.Equal(1, pager.Add(120, Ms(0)));
        Assert.Equal(0, pager.Add(120, Ms(50)));
        Assert.Equal(0, pager.Add(120, Ms(100)));
    }

    [Fact]
    public void AfterItSettles_TheNextSwipeTurnsAgain()
    {
        var pager = new WheelPager();

        Assert.Equal(1, pager.Add(120, Ms(0)));
        Assert.Equal(1, pager.Add(120, Ms(400)));
    }

    [Fact]
    public void APause_DropsAHalfDoneCount()
    {
        var pager = new WheelPager();

        Assert.Equal(0, pager.Add(80, Ms(0)));
        Assert.Equal(0, pager.Add(80, Ms(500)));
    }

    [Fact]
    public void BackAndForth_CancelsOut()
    {
        var pager = new WheelPager();

        Assert.Equal(0, pager.Add(80, Ms(0)));
        Assert.Equal(0, pager.Add(-80, Ms(20)));
        Assert.Equal(0, pager.Add(80, Ms(40)));
    }
}
