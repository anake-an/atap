using Atap.Core.Notch;
using Atap.Notch;

namespace Atap.Tests.Notch;

public class NotchControlTests
{
    [Fact]
    public void Flags_AreOredOverOwners()
    {
        var control = new NotchControl();
        var changes = 0;
        control.Changed += (_, _) => changes++;

        control.SetFlag(NotchFlag.Hide, "rules", true);
        control.SetFlag(NotchFlag.Hide, "tray", true);
        control.SetFlag(NotchFlag.Hide, "rules", false);
        Assert.True(control.IsSet(NotchFlag.Hide));

        control.SetFlag(NotchFlag.Hide, "tray", false);
        Assert.False(control.IsSet(NotchFlag.Hide));
        // Only the on and off transitions are reported.
        Assert.Equal(2, changes);
    }

    [Fact]
    public void ClearOwner_RemovesItsFlagsAndContent()
    {
        var control = new NotchControl();
        control.SetFlag(NotchFlag.Slim, "slim", true);
        control.SetSurfaceContent(NotchSurface.Alert, new object(), "slim");
        control.SetFlag(NotchFlag.Peek, "peek", true);

        control.ClearOwner("slim");

        Assert.False(control.IsSet(NotchFlag.Slim));
        Assert.Null(control.GetSurfaceContent(NotchSurface.Alert));
        Assert.True(control.IsSet(NotchFlag.Peek));
    }

    [Fact]
    public void SurfaceContent_CanBeTakenBackOnlyByItsOwner()
    {
        var control = new NotchControl();
        var content = new object();
        control.SetSurfaceContent(NotchSurface.Edit, content, "editor");

        control.SetSurfaceContent(NotchSurface.Edit, null, "someone-else");
        Assert.Same(content, control.GetSurfaceContent(NotchSurface.Edit));

        control.SetSurfaceContent(NotchSurface.Edit, null, "editor");
        Assert.Null(control.GetSurfaceContent(NotchSurface.Edit));
    }

    [Fact]
    public void Input_GoesToHandlersInOrderUntilOneTakesIt()
    {
        var control = new NotchControl();
        var router = new NotchInputRouter(control);
        var seen = new List<string>();
        using var first = control.AddInputHandler(new Handler("first", seen, takes: false));
        using var second = control.AddInputHandler(new Handler("second", seen, takes: true));
        using var third = control.AddInputHandler(new Handler("third", seen, takes: true));

        Assert.True(router.OnWheel(120, NotchSurface.Idle));

        Assert.Equal(["first", "second"], seen);
    }

    [Fact]
    public void Input_WithNoHandlers_IsNotTaken()
    {
        var control = new NotchControl();
        var router = new NotchInputRouter(control);
        var handler = control.AddInputHandler(new Handler("gone", [], takes: true));
        handler.Dispose();

        Assert.False(router.OnClick(NotchSurface.Idle));
        Assert.False(router.OnMiddleClick(NotchSurface.Compact));
        Assert.False(router.OnHorizontalWheel(-120, NotchSurface.Panel));
        Assert.Equal(0, control.InputHandlerCount);
    }

    private sealed class Handler(string name, List<string> seen, bool takes) : INotchInputHandler
    {
        public bool TryHandle(NotchInput input)
        {
            seen.Add(name);
            return takes;
        }
    }
}
