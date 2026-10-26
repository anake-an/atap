using Atap.Composition;
using Atap.Core.Notch;
using Atap.Core.Placement;
using Atap.Core.Widgets;
using Atap.Notch;
using Atap.Tests.Alerts;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Notch;

/// <summary>A full-screen or borderless window turns the notch on its screen slim, and only there.</summary>
public sealed class FullScreenSlimTests : IDisposable
{
    private static readonly PixelRect Main = new(0, 0, 2560, 1600);
    private static readonly PixelRect Side = new(329, -1080, 1920, 1080);

    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly FakeForeground _foreground = new();

    public FullScreenSlimTests() => _services.Layout.Load();

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(329, -1080, 1920, 1080, true)] // Borderless or full screen, exactly the screen.
    [InlineData(321, -1088, 1936, 1096, true)] // A little past every edge.
    [InlineData(329, -1080, 1920, 1032, false)] // Maximized: stops at the taskbar.
    [InlineData(400, -1000, 800, 600, false)] // A normal window.
    public void Covers_OnlyAWindowThatReachesEveryEdge(int left, int top, int width, int height, bool covers) =>
        Assert.Equal(covers, ScreenCover.Covers(new PixelRect(left, top, width, height), Side));

    [Fact]
    public async Task ACoveredScreen_TurnsItsNotchSlim_UntilTheWindowGoes()
    {
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        using var side = Model(widgets, Side);
        using var main = Model(widgets, Main);

        _foreground.CoveredScreen = Side;

        Assert.Equal(NotchSurface.Slim, side.Surface);
        Assert.NotEqual(NotchSurface.Slim, main.Surface);
        Assert.NotEqual(NotchSurface.Hidden, main.Surface);

        _foreground.CoveredScreen = null;

        Assert.NotEqual(NotchSurface.Slim, side.Surface);
    }

    [Fact]
    public async Task AFullScreenAppOnAnotherScreen_NoLongerHidesThisNotch()
    {
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        using var main = Model(widgets, Main);

        _foreground.FullScreen = FullScreenKind.FullScreen;
        _foreground.CoveredScreen = Side;

        Assert.NotEqual(NotchSurface.Hidden, main.Surface);
        Assert.NotEqual(NotchSurface.Slim, main.Surface);
    }

    [Theory]
    [InlineData(FullScreenKind.Game)]
    [InlineData(FullScreenKind.Presentation)]
    public async Task AGameOrAPresentation_StillHidesTheNotch(FullScreenKind kind)
    {
        await using var widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([]));
        using var main = Model(widgets, Main);

        _foreground.FullScreen = kind;

        Assert.Equal(NotchSurface.Hidden, main.Surface);
    }

    private NotchViewModel Model(WidgetHost widgets, PixelRect screen)
    {
        var model = new NotchViewModel(_services.Layout, widgets, _notch, _foreground);
        model.SetScreen(screen);
        return model;
    }
}
