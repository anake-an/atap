using Atap.Composition;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Notch;
using Atap.Core.Widgets;
using Atap.Notch;
using Atap.Tests.Composition;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Notch;

/// <summary>Files dragged over the notch open it where the file shelf can take them, and it closes after the drag.</summary>
public sealed class FileDragTests : IAsyncLifetime
{
    private readonly TestServices _services = new();
    private readonly NotchControl _notch = new();
    private readonly TestDescriptor _alpha = new("alpha", states: [NotchState.Expanded, NotchState.Panel]);
    private readonly TestDescriptor _shelf = new(NotchFileDrag.ShelfTypeId, states: [NotchState.Expanded, NotchState.Panel]);
    private WidgetHost _widgets = null!;

    public async Task InitializeAsync()
    {
        _services.Layout.Load();
        _widgets = _services.CreateWidgetHost(WidgetCatalog.FromDescriptors([_alpha, _shelf]));
        await _widgets.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _widgets.DisposeAsync();
        _services.Dispose();
    }

    [Theory]
    [InlineData(false, true, 0, NotchDragTarget.None)]
    [InlineData(true, false, -1, NotchDragTarget.None)]
    [InlineData(true, true, 1, NotchDragTarget.Expanded)]
    [InlineData(true, false, 1, NotchDragTarget.Panel)]
    public void TheNotchOpens_OnlyForFiles_AndOnlyWithAShelf(bool hasFiles, bool shelfInExpanded, int shelfPage, NotchDragTarget expected) =>
        Assert.Equal(expected, NotchFileDrag.Target(hasFiles, shelfInExpanded, shelfPage));

    [Fact]
    public async Task FilesOverTheNotch_OpenTheHoverView_WithTheShelf_AndItClosesAfterTheDrag()
    {
        await Use(d => d.States.Expanded.Placements = [Place(NotchFileDrag.ShelfTypeId)]);
        using var model = Model();

        Assert.Equal(NotchDragTarget.None, model.FileDragTarget(hasFiles: false));
        Assert.Equal(NotchDragTarget.Expanded, model.FileDragTarget(hasFiles: true));

        model.IsDraggingFiles = true;

        Assert.Equal(NotchSurface.Expanded, model.Surface);
        Assert.Contains(model.GridItems, i => i.InstanceId == NotchFileDrag.ShelfTypeId);
        Assert.False(model.IsOpenForFiles);

        model.IsDraggingFiles = false;

        Assert.Equal(NotchSurface.Idle, model.Surface);
    }

    [Fact]
    public async Task AShelfOnlyInThePanel_OpensItsPage_WithoutTakingTheKeyboard()
    {
        await Use(d =>
        {
            d.States.Expanded.Placements = [Place("alpha")];
            d.States.Panel.Pages = [Page("main", Place("alpha")), Page("files", Place(NotchFileDrag.ShelfTypeId))];
        });
        using var model = Model();

        Assert.Equal(NotchDragTarget.Panel, model.FileDragTarget(hasFiles: true));

        model.IsDraggingFiles = true;

        Assert.Equal(NotchSurface.Panel, model.Surface);
        Assert.Equal(1, model.PageIndex);
        Assert.True(model.IsOpenForFiles);
        Assert.False(_notch.IsSet(NotchFlag.PanelOpen));

        model.IsDraggingFiles = false;

        Assert.NotEqual(NotchSurface.Panel, model.Surface);
        Assert.False(model.IsOpenForFiles);
    }

    [Fact]
    public async Task WithoutAShelf_ADragChangesNothing()
    {
        await Use(d => d.States.Expanded.Placements = [Place("alpha")]);
        using var model = Model();

        Assert.Equal(NotchDragTarget.None, model.FileDragTarget(hasFiles: true));

        model.IsDraggingFiles = true;

        Assert.Equal(NotchSurface.Idle, model.Surface);
    }

    /// <summary>Runs the widgets the layout places. The panel is empty unless the test fills it.</summary>
    private async Task Use(Action<LayoutDocument> update)
    {
        _services.Layout.Update(d =>
        {
            d.States.Panel.Pages = [];
            update(d);
            d.EnabledWidgets = [.. d.States.Expanded.Placements.Select(p => p.WidgetId)
                .Concat(d.States.Panel.Pages.SelectMany(p => p.Placements).Select(p => p.WidgetId))
                .Distinct()];
        });
        await _widgets.ApplyAsync(_services.Layout.Document.EnabledWidgets);
    }

    private NotchViewModel Model() => new(_services.Layout, _widgets, _notch, new ForegroundStub());

    private static WidgetPlacement Place(string id) => new() { WidgetId = id, Size = WidgetSize.Small };

    private static PanelPage Page(string id, params WidgetPlacement[] placements) =>
        new() { Id = id, Name = id, Placements = [.. placements] };
}
