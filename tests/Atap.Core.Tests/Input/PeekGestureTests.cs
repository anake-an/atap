using Atap.Core.Input;

namespace Atap.Core.Tests.Input;

public class PeekGestureTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void AQuickTap_TogglesThePanel()
    {
        var gesture = new PeekGesture(Ms(1000));

        Assert.Equal(PeekAction.None, gesture.Update(Ms(1050), held: true));
        Assert.Equal(PeekAction.TogglePanel, gesture.Update(Ms(1100), held: false));
        Assert.True(gesture.IsDone);
    }

    [Fact]
    public void AHold_PeeksUntilRelease()
    {
        var gesture = new PeekGesture(Ms(0));

        Assert.Equal(PeekAction.None, gesture.Update(Ms(250), held: true));
        Assert.Equal(PeekAction.StartPeek, gesture.Update(Ms(300), held: true));
        Assert.Equal(PeekAction.None, gesture.Update(Ms(2000), held: true));
        Assert.Equal(PeekAction.EndPeek, gesture.Update(Ms(2050), held: false));
        Assert.True(gesture.IsDone);
    }

    [Fact]
    public void AStuckKey_StopsAfterThirtySeconds()
    {
        var gesture = new PeekGesture(Ms(0));
        gesture.Update(Ms(400), held: true);

        Assert.Equal(PeekAction.EndPeek, gesture.Update(Ms(30_000), held: true));
        Assert.True(gesture.IsDone);
        Assert.Equal(PeekAction.None, gesture.Update(Ms(30_050), held: false));
    }

    [Fact]
    public void AfterItIsDone_NothingMoreHappens()
    {
        var gesture = new PeekGesture(Ms(0));
        gesture.Update(Ms(100), held: false);

        Assert.Equal(PeekAction.None, gesture.Update(Ms(150), held: false));
        Assert.Equal(PeekAction.None, gesture.Update(Ms(400), held: true));
    }

    [Fact]
    public void ReleasedJustBeforeTheLimit_IsStillATap()
    {
        var gesture = new PeekGesture(Ms(0));

        Assert.Equal(PeekAction.TogglePanel, gesture.Update(Ms(299), held: false));
    }
}
