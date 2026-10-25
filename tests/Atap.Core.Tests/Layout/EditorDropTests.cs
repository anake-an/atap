using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Layout;

/// <summary>The editor's notch preview: where tiles are drawn, where a drop lands, and the one change it makes.</summary>
public class EditorDropTests
{
    private static readonly EditSpot Open = new(NotchState.Expanded);
    private static readonly EditSpot Idle = new(NotchState.Idle);
    private static readonly EditSpot Page1 = new(NotchState.Panel, 0);
    private static readonly EditSpot Page2 = new(NotchState.Panel, 1);
    private static readonly PreviewMetrics Metrics = PreviewMetrics.Notch;

    private static readonly WidgetCatalog Catalog = WidgetCatalog.FromDescriptors(
    [
        new Descriptor("media", [WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("clock", [WidgetSize.Small], [NotchState.Idle, NotchState.Expanded]),
        new Descriptor("missed-alerts", [WidgetSize.Medium], [NotchState.Panel]),
        new Descriptor("focus-timer", [WidgetSize.Small, WidgetSize.Medium], [NotchState.Compact, NotchState.Expanded, NotchState.Panel]),
        new Descriptor("stopwatch", [WidgetSize.Small], [NotchState.Expanded, NotchState.Panel]),
    ]);

    [Fact]
    public void Cells_OnTheGrid_PackLikeTheNotch()
    {
        var cells = PreviewLayout.Cells(PreviewShape.Grid, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Small, WidgetSize.Medium], Metrics);

        Assert.Equal(
            [
                new PreviewRect(0, 0, 156, 72),
                new PreviewRect(164, 0, 320, 72),
                new PreviewRect(492, 0, 156, 72),
                new PreviewRect(0, 80, 320, 72),
            ],
            cells);
    }

    [Fact]
    public void Cells_InTheSmallNotch_SitSideBySide()
    {
        var cells = PreviewLayout.Cells(PreviewShape.Row, [WidgetSize.Small, WidgetSize.Medium], Metrics);

        Assert.Equal([new PreviewRect(0, 0, 156, 72), new PreviewRect(164, 0, 320, 72)], cells);
        Assert.Equal(PreviewShape.Row, PreviewLayout.ShapeOf(NotchState.Idle));
        Assert.Equal(PreviewShape.Grid, PreviewLayout.ShapeOf(NotchState.Panel));
    }

    [Theory]
    [InlineData(10, 30, 0)] // left half of the first tile
    [InlineData(150, 30, 1)] // right half of the first tile
    [InlineData(200, 30, 1)] // left half of the medium tile
    [InlineData(600, 30, 3)] // right half of the last tile in the first row
    [InlineData(900, 30, 3)] // far right of the first row
    [InlineData(20, 100, 3)] // left half of the tile in the second row
    [InlineData(500, 120, 4)] // beside the short second row
    [InlineData(100, 400, 3)] // far below, nearest the second row
    public void DropIndex_GoesBeforeOrAfterTheNearestTile(double x, double y, int expected)
    {
        var cells = PreviewLayout.Cells(PreviewShape.Grid, [WidgetSize.Small, WidgetSize.Medium, WidgetSize.Small, WidgetSize.Medium], Metrics);

        Assert.Equal(expected, PreviewLayout.DropIndex(cells, x, y));
    }

    [Fact]
    public void DropIndex_OnAnEmptyNotch_IsTheFirstPlace() =>
        Assert.Equal(0, PreviewLayout.DropIndex([], 300, 300));

    [Fact]
    public void FinalIndex_CountsTheTileLeavingItsOldPlace()
    {
        var placed = EditorDrag.FromPreview(Open, 1);

        Assert.Equal(0, EditorDrop.FinalIndex(placed, Open, 0));
        Assert.Equal(1, EditorDrop.FinalIndex(placed, Open, 2));
        Assert.Equal(2, EditorDrop.FinalIndex(placed, Open, 3));
        Assert.Equal(3, EditorDrop.FinalIndex(placed, Page2, 3));
        Assert.Equal(3, EditorDrop.FinalIndex(EditorDrag.FromGallery("clock"), Open, 3));
    }

    [Fact]
    public void FromTheGallery_AddsWhereItWasDropped_InOneUndoStep()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Applied, EditorDrop.Apply(session, EditorDrag.FromGallery("focus-timer"), Open, 0));

        Assert.Equal(["focus-timer", "media"], Ids(session, Open));
        Assert.Contains("focus-timer", session.Draft.EnabledWidgets);
        Assert.True(session.Undo());
        Assert.Equal(["media"], Ids(session, Open));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void FromTheGallery_OutsideTheNotch_ChangesNothing()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Unchanged, EditorDrop.Apply(session, EditorDrag.FromGallery("focus-timer"), null, 0));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void FromTheGallery_WhereItCannotGo_IsRejected()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Rejected, EditorDrop.Apply(session, EditorDrag.FromGallery("missed-alerts"), Idle, 0));
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void WithinThePlace_Reorders()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);
        session.Add("stopwatch", Open);

        // The first tile dropped after the last one.
        Assert.Equal(EditResult.Applied, EditorDrop.Apply(session, EditorDrag.FromPreview(Open, 0), Open, 3));
        Assert.Equal(["focus-timer", "stopwatch", "media"], Ids(session, Open));

        // The last tile dropped before the first one.
        Assert.Equal(EditResult.Applied, EditorDrop.Apply(session, EditorDrag.FromPreview(Open, 2), Open, 0));
        Assert.Equal(["media", "focus-timer", "stopwatch"], Ids(session, Open));

        // Dropped back where it was.
        Assert.Equal(EditResult.Unchanged, EditorDrop.Apply(session, EditorDrag.FromPreview(Open, 1), Open, 2));
    }

    [Fact]
    public void OntoAnotherPage_MovesIt_AndUndoBringsItBack()
    {
        var session = NewSession();
        session.AddPage("Page 2");

        Assert.Equal(EditResult.Applied, EditorDrop.Apply(session, EditorDrag.FromPreview(Page1, 1), Page2, int.MaxValue));

        Assert.Equal(["media"], Ids(session, Page1));
        Assert.Equal(["missed-alerts"], Ids(session, Page2));
        Assert.Contains("missed-alerts", session.Draft.EnabledWidgets);
        session.Undo();
        Assert.Equal(["media", "missed-alerts"], Ids(session, Page1));
        Assert.Empty(session.PlacementsAt(Page2));
    }

    [Fact]
    public void OntoAPageThatHasItAlready_IsRejected()
    {
        var session = NewSession();
        session.AddPage("Page 2");
        session.Add("media", Page2);

        Assert.Equal(EditResult.Rejected, EditorDrop.Apply(session, EditorDrag.FromPreview(Page1, 0), Page2, 0));
    }

    [Fact]
    public void IntoAnotherState_IsRejected()
    {
        var session = NewSession();

        Assert.Equal(EditResult.Rejected, session.MoveTo(Open, 0, Page1, 0));
    }

    [Fact]
    public void OffTheNotch_RemovesIt_AndUndoBringsItBack()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);

        Assert.Equal(EditResult.Applied, EditorDrop.Apply(session, EditorDrag.FromPreview(Open, 1), null, 0));

        Assert.Equal(["media"], Ids(session, Open));
        Assert.DoesNotContain("focus-timer", session.Draft.EnabledWidgets);
        session.Undo();
        Assert.Equal(["media", "focus-timer"], Ids(session, Open));
    }

    [Fact]
    public void Add_AtAPlace_PutsItThere()
    {
        var session = NewSession();
        session.Add("focus-timer", Open);

        Assert.Equal(EditResult.Applied, session.Add("stopwatch", Open, at: 1));
        Assert.Equal(["media", "stopwatch", "focus-timer"], Ids(session, Open));
        Assert.Equal([0, 1, 2], session.PlacementsAt(Open).Select(p => p.Order));

        Assert.Equal(EditResult.Applied, session.Add("clock", Open, at: 99));
        Assert.Equal("clock", Ids(session, Open)[^1]);
    }

    private static LayoutEditSession NewSession() => new(DefaultLayout.Create(), EffectiveLayout.DefaultProfileId, Catalog);

    private static List<string> Ids(LayoutEditSession session, EditSpot spot) => session.PlacementsAt(spot).Select(p => p.WidgetId).ToList();

    private sealed class Descriptor(string typeId, IReadOnlyList<WidgetSize> sizes, IReadOnlyList<NotchState> states) : IWidgetDescriptor
    {
        public string TypeId => typeId;

        public string NameKey => $"{typeId}.name";

        public string DescriptionKey => $"{typeId}.description";

        public WidgetGroup Group => WidgetGroup.Custom;

        public IReadOnlyList<WidgetSize> SupportedSizes => sizes;

        public IReadOnlyList<NotchState> SupportedStates => states;

        public IReadOnlyList<SettingDefinition> Settings => [];

        public WidgetCapabilities Capabilities => WidgetCapabilities.None;

        public bool AllowsMultipleInstances => false;

        public bool WorksWithoutPlacement => false;

        public Uri ViewsSource => new($"/Tests;component/{typeId}.xaml", UriKind.Relative);

        public IWidget Create(WidgetContext context) => throw new NotSupportedException();
    }
}
