using Atap.Core.Notch;

namespace Atap.Core.Tests.Notch;

public class NotchSurfaceResolverTests
{
    private static readonly NotchInputs Nothing = new(
        Hidden: false, Editing: false, PanelOpen: false, Hovered: false, Peeking: false,
        HasExpandedContent: false, HasAlert: false, Slim: false, HasCompactOwner: false, HasCompactContent: false);

    private static readonly NotchInputs Everything = new(
        Hidden: true, Editing: true, PanelOpen: true, Hovered: true, Peeking: true,
        HasExpandedContent: true, HasAlert: true, Slim: true, HasCompactOwner: true, HasCompactContent: true);

    [Fact]
    public void Nothing_IsIdle()
    {
        Assert.Equal(NotchSurface.Idle, NotchSurfaceResolver.Resolve(Nothing));
    }

    // Each row turns off everything above the expected surface, so it must win over everything below it.
    public static TheoryData<NotchInputs, NotchSurface> PrecedenceRows => new()
    {
        { Everything, NotchSurface.Hidden },
        { Everything with { Hidden = false }, NotchSurface.Edit },
        { Everything with { Hidden = false, Editing = false }, NotchSurface.Panel },
        { Everything with { Hidden = false, Editing = false, PanelOpen = false }, NotchSurface.Expanded },
        { Everything with { Hidden = false, Editing = false, PanelOpen = false, Hovered = false, Peeking = false }, NotchSurface.Alert },
        { Everything with { Hidden = false, Editing = false, PanelOpen = false, Hovered = false, Peeking = false, HasAlert = false }, NotchSurface.Slim },
        { Everything with { Hidden = false, Editing = false, PanelOpen = false, Hovered = false, Peeking = false, HasAlert = false, Slim = false }, NotchSurface.Compact },
        { Everything with { Hidden = false, Editing = false, PanelOpen = false, Hovered = false, Peeking = false, HasAlert = false, Slim = false, HasCompactOwner = false }, NotchSurface.Idle },
    };

    [Theory]
    [MemberData(nameof(PrecedenceRows))]
    public void Resolve_FollowsThePrecedenceTable(NotchInputs inputs, NotchSurface expected)
    {
        Assert.Equal(expected, NotchSurfaceResolver.Resolve(inputs));
    }

    [Fact]
    public void Expanded_NeedsHoverOrPeek()
    {
        Assert.Equal(NotchSurface.Expanded, NotchSurfaceResolver.Resolve(Nothing with { Hovered = true, HasExpandedContent = true }));
        Assert.Equal(NotchSurface.Expanded, NotchSurfaceResolver.Resolve(Nothing with { Peeking = true, HasExpandedContent = true }));
        Assert.Equal(NotchSurface.Idle, NotchSurfaceResolver.Resolve(Nothing with { HasExpandedContent = true }));
    }

    [Fact]
    public void Expanded_NeedsContent()
    {
        Assert.Equal(NotchSurface.Idle, NotchSurfaceResolver.Resolve(Nothing with { Hovered = true }));
        Assert.Equal(NotchSurface.Compact, NotchSurfaceResolver.Resolve(Nothing with { Hovered = true, HasCompactOwner = true, HasCompactContent = true }));
    }

    [Fact]
    public void Compact_NeedsAnOwnerWithContent()
    {
        Assert.Equal(NotchSurface.Idle, NotchSurfaceResolver.Resolve(Nothing with { HasCompactOwner = true }));
        Assert.Equal(NotchSurface.Idle, NotchSurfaceResolver.Resolve(Nothing with { HasCompactContent = true }));
        Assert.Equal(NotchSurface.Compact, NotchSurfaceResolver.Resolve(Nothing with { HasCompactOwner = true, HasCompactContent = true }));
    }

    [Fact]
    public void Slim_ReplacesCompactAndIdle()
    {
        Assert.Equal(NotchSurface.Slim, NotchSurfaceResolver.Resolve(Nothing with { Slim = true }));
        Assert.Equal(NotchSurface.Slim, NotchSurfaceResolver.Resolve(Nothing with { Slim = true, HasCompactOwner = true, HasCompactContent = true }));
        Assert.Equal(NotchSurface.Expanded, NotchSurfaceResolver.Resolve(Nothing with { Slim = true, Hovered = true, HasExpandedContent = true }));
    }

    [Fact]
    public void Hidden_WinsOverEverything()
    {
        Assert.Equal(NotchSurface.Hidden, NotchSurfaceResolver.Resolve(Nothing with { Hidden = true }));
        Assert.Equal(NotchSurface.Hidden, NotchSurfaceResolver.Resolve(Everything));
    }

    [Theory]
    [InlineData(NotchSurface.Idle, NotchState.Idle)]
    [InlineData(NotchSurface.Compact, NotchState.Compact)]
    [InlineData(NotchSurface.Expanded, NotchState.Expanded)]
    [InlineData(NotchSurface.Panel, NotchState.Panel)]
    [InlineData(NotchSurface.Alert, null)]
    [InlineData(NotchSurface.Slim, null)]
    [InlineData(NotchSurface.Edit, null)]
    [InlineData(NotchSurface.Hidden, null)]
    public void StateOf_MapsSurfacesThatShowPlacements(NotchSurface surface, NotchState? state)
    {
        Assert.Equal(state, NotchSurfaceResolver.StateOf(surface));
    }
}
