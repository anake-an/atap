using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Features.Panel;
using Atap.Notch;
using Atap.Tests.Composition;
using Atap.Tests.Infrastructure;
using Atap.Ui.Controls;

namespace Atap.Tests.Notch;

/// <summary>The panel: click to open and close, its pages and dots, the grid, and alerts that come while the notch is open.</summary>
public sealed class PanelTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly TestDescriptor _alpha = new("alpha", states: [NotchState.Expanded, NotchState.Panel]);
    private readonly TestDescriptor _beta = new("beta", states: [NotchState.Panel]);
    private WidgetHost _widgets = null!;
    private PanelFeature _feature = null!;

    public async Task InitializeAsync()
    {
        _services.Layout.Load();
        _services.Layout.Update(d =>
        {
            d.EnabledWidgets = ["alpha", "beta"];
            d.States.Expanded.Placements = [new WidgetPlacement { WidgetId = "alpha", Size = WidgetSize.Medium }];
            d.States.Panel.Pages =
            [
                Page("main", new WidgetPlacement { WidgetId = "alpha", Size = WidgetSize.Medium }, new WidgetPlacement { WidgetId = "beta", Order = 1 }),
                // Nothing on this page can show, so it is skipped.
                Page("gone", new WidgetPlacement { WidgetId = "not-installed" }),
                Page("two", new WidgetPlacement { WidgetId = "beta", Size = WidgetSize.Large }),
            ];
        });
        _widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([_alpha, _beta]));
        await _widgets.ApplyAsync(["alpha", "beta"]);
        await _widgets.StartAsync();
        _feature = new PanelFeature(_notch, _services.Commands, PanelDescriptor.FeatureId);
        await _feature.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _feature.DisposeAsync();
        await _widgets.DisposeAsync();
        _services.Dispose();
    }

    [Fact]
    public void AClick_OpensThePanel_AndAnotherClickClosesIt()
    {
        using var model = Model();

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Expanded)));
        Assert.Equal(NotchSurface.Panel, model.Surface);

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Panel)));
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));
    }

    [Theory]
    [InlineData(NotchSurface.Idle)]
    [InlineData(NotchSurface.Compact)]
    [InlineData(NotchSurface.Slim)]
    public void AClickOnTheClosedNotch_OpensThePanel(NotchSurface surface)
    {
        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Click, surface)));
        Assert.True(_notch.IsSet(NotchFlag.PanelOpen));
    }

    [Fact]
    public void AClickOnAnAlert_StaysWithTheAlert()
    {
        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Click, NotchSurface.Alert)));
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));
    }

    [Fact]
    public void Esc_ClosesThePanel()
    {
        _feature.Toggle();

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Key, NotchSurface.Panel) { Key = Key.Escape }));
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));
    }

    [Fact]
    public async Task TheMenuItem_OpensAndCloses_AndIsTickedWhileOpen()
    {
        var command = _services.Commands.All.Single(c => c.Id == PanelFeature.ToggleCommand);
        Assert.Equal(PanelFeature.MenuGroup, command.MenuGroup);
        Assert.False(command.IsChecked!());

        Assert.True(await _services.Commands.TryRunAsync(PanelFeature.ToggleCommand));
        Assert.True(command.IsChecked!());

        Assert.True(await _services.Commands.TryRunAsync(PanelFeature.ToggleCommand));
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));
    }

    [Fact]
    public void ThePanel_ShowsTheFirstPage_OnTheGrid_AndSkipsEmptyPages()
    {
        using var model = Model();
        _feature.Toggle();

        Assert.Equal(["alpha", "beta"], model.GridItems.Select(i => i.InstanceId));
        Assert.All(model.GridItems, i => Assert.Equal(NotchState.Panel, i.State));
        Assert.Empty(model.CenterItems);
        Assert.Equal(2, model.PageCount);
        Assert.True(model.HasPages);
        Assert.Equal(0, model.PageIndex);
    }

    [Fact]
    public void StepPage_TurnsPages_AndStopsAtTheEnds()
    {
        using var model = Model();
        _feature.Toggle();
        var changes = 0;
        model.ContentChanged += (_, _) => changes++;

        Assert.False(model.StepPage(-1));
        Assert.True(model.StepPage(1));
        Assert.Equal(1, model.PageIndex);
        Assert.Equal(["beta"], model.GridItems.Select(i => i.InstanceId));
        Assert.Equal(WidgetSize.Large, model.GridItems[0].Size);
        Assert.False(model.StepPage(1));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void StepPage_DoesNothing_WhileThePanelIsClosed()
    {
        using var model = Model();

        Assert.False(model.StepPage(1));
        Assert.Equal(0, model.PageIndex);
    }

    [Fact]
    public void ThePageShown_StaysInRange_WhenPagesGoAway()
    {
        using var model = Model();
        _feature.Toggle();
        model.PageIndex = 1;

        _services.Layout.Update(d => d.States.Panel.Pages.RemoveAt(2));

        Assert.Equal(0, model.PageIndex);
        Assert.Equal(1, model.PageCount);
        Assert.False(model.HasPages);
    }

    [Fact]
    public void AnEmptyPanel_SaysSo()
    {
        _services.Layout.Update(d => d.States.Panel.Pages = [Page("main")]);
        using var model = Model();
        _feature.Toggle();

        Assert.Equal(NotchSurface.Panel, model.Surface);
        Assert.True(model.IsGridEmpty);
        Assert.Equal(0, model.PageCount);
    }

    [Fact]
    public void Expanded_PutsItsWidgetsOnTheGrid()
    {
        using var model = Model();

        model.IsHovered = true;

        Assert.Equal(NotchSurface.Expanded, model.Surface);
        Assert.Equal(["alpha"], model.GridItems.Select(i => i.InstanceId));
        Assert.Empty(model.CenterItems);
    }

    [Fact]
    public void AnAlert_ShowsAsABanner_WhileTheNotchIsOpen()
    {
        using var model = Model();
        model.IsHovered = true;
        var changes = 0;
        model.ContentChanged += (_, _) => changes++;

        _notch.SetSurfaceContent(NotchSurface.Alert, "alert", "alerts");

        Assert.Equal(NotchSurface.Expanded, model.Surface);
        Assert.Equal("alert", model.Banner);
        Assert.Equal(1, changes);

        model.IsHovered = false;

        // Closed, the alert has its own surface, so there is no banner.
        Assert.Equal(NotchSurface.Alert, model.Surface);
        Assert.Null(model.Banner);
    }

    [Fact]
    public void SafeMode_HasNoPanel()
    {
        var descriptor = new PanelDescriptor();

        Assert.False(descriptor.IsEnabled(SettingValues.Empty, AppMode.Safe));
        Assert.True(descriptor.IsEnabled(SettingValues.Empty, AppMode.Normal));
    }

    [Fact]
    public void TheGrid_PlacesWidgetsByTheirSize() => Sta.Run(() =>
    {
        var grid = new WidgetGrid { ColumnWidth = 100, Gap = 10 };
        grid.Children.Add(Card(WidgetSize.Medium, 40));
        grid.Children.Add(Card(WidgetSize.Small, 60));
        grid.Children.Add(Card(WidgetSize.Large, 20));

        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        grid.Arrange(new Rect(grid.DesiredSize));

        // Row 0: medium and small, 60 tall. Row 1: large, 20 tall. Four columns wide.
        Assert.Equal(new Size(430, 90), grid.DesiredSize);
        Assert.Equal(new Rect(0, 0, 210, 60), Slot(grid.Children[0]));
        Assert.Equal(new Rect(220, 0, 100, 60), Slot(grid.Children[1]));
        Assert.Equal(new Rect(0, 70, 430, 20), Slot(grid.Children[2]));
    });

    [Fact]
    public void TheGrid_IsOnlyAsWideAsItsWidestRow() => Sta.Run(() =>
    {
        var grid = new WidgetGrid();
        grid.Children.Add(Card(WidgetSize.Medium, 40));

        grid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(grid.WidthOf(2), grid.DesiredSize.Width);
    });

    [Fact]
    public void PageDots_FindTheDotUnderThePointer() => Sta.Run(() =>
    {
        var dots = new PageDots { Count = 3, SelectedIndex = 0 };

        // The current dot is 16 wide, the others 6, with 6 between them.
        Assert.Equal(0, dots.IndexAt(8));
        Assert.Equal(1, dots.IndexAt(24));
        Assert.Equal(2, dots.IndexAt(36));
        Assert.Equal(-1, dots.IndexAt(60));
    });

    private NotchViewModel Model() => new(_services.Layout, _widgets, _notch, new ForegroundStub());

    private static PanelPage Page(string id, params WidgetPlacement[] placements) =>
        new() { Id = id, Name = id, Placements = [.. placements] };

    private static Border Card(WidgetSize size, double height)
    {
        var card = new Border { Height = height };
        WidgetGrid.SetSize(card, size);
        return card;
    }

    private static Rect Slot(UIElement element) => LayoutInformation.GetLayoutSlot((FrameworkElement)element);
}
