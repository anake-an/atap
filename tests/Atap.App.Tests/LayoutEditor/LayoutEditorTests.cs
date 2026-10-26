using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Layout.Editing;
using Atap.Core.Widgets;
using Atap.Features.LayoutEditor;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.LayoutEditor;

/// <summary>The editor's model over a real layout store on a temp folder, with the real widget catalog.</summary>
public sealed class LayoutEditorTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly LayoutBackup _backup;

    public LayoutEditorTests()
    {
        _services.Layout.Load();
        _backup = new LayoutBackup(_services.Paths.LayoutFile);
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Done_SavesOnce_AndKeepsThePreviousLayout()
    {
        using var editor = NewEditor();
        var saves = 0;
        _services.Layout.Changed += (_, _) => saves++;

        editor.Tab = EditorTab.Open;
        AddFromGallery(editor, "focus-timer");
        AddFromGallery(editor, "stopwatch");
        Assert.True(editor.IsDirty);
        Assert.Equal(0, saves);

        editor.DoneCommand.Execute(null);

        Assert.Equal(1, saves);
        Assert.Equal(["media", "focus-timer", "stopwatch"], _services.Layout.Document.States.Expanded.Placements.Select(p => p.WidgetId));
        Assert.True(_backup.Exists);
        Assert.Equal(["media"], _backup.TryLoad()!.States.Expanded.Placements.Select(p => p.WidgetId));
    }

    [Fact]
    public void Cancel_SavesNothing()
    {
        using var editor = NewEditor();
        var closed = false;
        editor.CloseRequested += (_, _) => closed = true;
        AddFromGallery(editor, "focus-timer");

        editor.CancelCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal(["media"], _services.Layout.Document.States.Expanded.Placements.Select(p => p.WidgetId));
        Assert.False(_backup.Exists);
    }

    [Fact]
    public void Tiles_ResizeMoveAndRemove_WithButtons_AndUndo()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "focus-timer");

        var tile = editor.Tiles[1];
        Assert.Equal("Focus timer", tile.Name);
        tile.Sizes.Single(s => s.Label == "M").Pick.Execute(null);
        Assert.Contains(editor.Tiles[1].Sizes, s => s.Label == "M" && s.IsSelected);

        editor.Tiles[1].MoveEarlier.Execute(null);
        Assert.Equal("Focus timer", editor.Tiles[0].Name);
        Assert.False(editor.Tiles[0].CanMoveEarlier);

        editor.Tiles[0].Remove.Execute(null);
        Assert.Single(editor.Tiles);

        editor.UndoCommand.Execute(null);
        Assert.Equal(2, editor.Tiles.Count);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void Gallery_SaysWhatCannotGoHere()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Idle;

        var media = editor.Gallery.SelectMany(g => g.Items).Single(i => i.TypeId == "media");
        var clock = editor.Gallery.SelectMany(g => g.Items).Single(i => i.TypeId == "clock");

        Assert.False(media.CanAdd);
        Assert.Equal("Not for this spot", media.Note);
        Assert.False(clock.CanAdd);
        Assert.Equal("Already here", clock.Note);
    }

    [Fact]
    public void Gallery_Search_MatchesNamesAndDescriptions()
    {
        using var editor = NewEditor();

        editor.Search = "laps";

        Assert.Equal(["stopwatch"], editor.Gallery.SelectMany(g => g.Items).Select(i => i.TypeId));
    }

    [Fact]
    public void Panel_Pages_AddSwitchAndRemove()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Panel;

        editor.AddPageCommand.Execute(null);
        Assert.Equal(["Main", "Page 2"], editor.Pages.Select(p => p.Name));
        Assert.Equal(1, editor.SelectedPage?.Index);
        Assert.True(editor.HasNoTiles);

        editor.SelectedPage = editor.Pages[0];
        Assert.NotEmpty(editor.Tiles);
        editor.SelectedPage = editor.Pages[1];
        editor.RemovePageCommand.Execute(null);
        Assert.Single(editor.Pages);
        Assert.False(editor.CanRemovePage);
    }

    [Fact]
    public void AnEmptyFirstPanelPage_SaysAClickShowsTheHoverWidgets_UntilAWidgetIsChosen()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Panel;
        EmptyThePage(editor);

        Assert.True(editor.Picture.IsEmpty);
        Assert.Equal(
            "A click now shows what you see when you point at it, a little bigger. Drag a widget here to choose your own.",
            editor.Picture.EmptyText);

        editor.AddPageCommand.Execute(null);
        AddFromGallery(editor, "stopwatch");
        editor.SelectedPage = editor.Pages[0];

        Assert.True(editor.Picture.IsEmpty);
        Assert.Equal("Drag a widget here.", editor.Picture.EmptyText);
    }

    [Fact]
    public void AnEmptySecondPanelPage_AsksForAWidget_EvenWhileThePanelFollowsHover()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Panel;
        editor.AddPageCommand.Execute(null);

        Assert.Equal(1, editor.SelectedPage?.Index);
        Assert.True(editor.Picture.IsEmpty);
        Assert.Equal("Drag a widget here.", editor.Picture.EmptyText);
    }

    [Fact]
    public void AnEmptySpotOffThePanel_AsksForAWidget()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Open;
        EmptyThePage(editor);

        Assert.True(editor.Picture.IsEmpty);
        Assert.Equal("Drag a widget here.", editor.Picture.EmptyText);
    }

    [Fact]
    public void Priority_ListsOnlyWidgetsThatAreOn()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "stopwatch");
        editor.Tab = EditorTab.Priority;

        Assert.Equal(["Now playing", "Stopwatch"], editor.Priority.Select(p => p.Name));
        editor.Priority[1].MoveUp.Execute(null);
        Assert.Equal(["Stopwatch", "Now playing"], editor.Priority.Select(p => p.Name));
    }

    [Fact]
    public void AChangeFromOutside_OffersAReload_AndReloadStartsAgain()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "focus-timer");

        _services.Layout.Update(d => d.CompactPriority.Insert(0, "stopwatch"));

        Assert.True(editor.ChangedOutside);
        editor.ReloadCommand.Execute(null);
        Assert.False(editor.ChangedOutside);
        Assert.False(editor.IsDirty);
        Assert.Single(editor.Tiles);
    }

    [Fact]
    public async Task Feature_PutsTheNotchInEditMode_WhileTheEditorIsOpen()
    {
        var commands = new CommandRegistry();
        var notch = new NotchControl();
        var window = new FakeWindow();
        var feature = new LayoutEditorFeature(commands, notch, () => window);
        await feature.StartAsync();

        Assert.True(commands.Contains(NotchMenuViewModel.EditLayoutCommandId));
        await commands.TryRunAsync(NotchMenuViewModel.EditLayoutCommandId);
        Assert.True(notch.IsSet(NotchFlag.Editing));

        await commands.TryRunAsync(NotchMenuViewModel.EditLayoutCommandId);
        Assert.Equal(1, window.Activations);

        window.Close();
        Assert.False(notch.IsSet(NotchFlag.Editing));
        await feature.DisposeAsync();
    }

    [Fact]
    public void Tabs_UsePlainNames()
    {
        using var editor = NewEditor();

        Assert.Equal(["Small notch", "When you point at it", "When you click it", "When something is live"], editor.Tabs.Select(t => t.Label));
        Assert.Equal("The notch grows a little when you point at it.", editor.TabHint);
    }

    [Fact]
    public void Gallery_ShowsEveryWidgetWithAPreviewAndAPlainLine()
    {
        using var editor = NewEditor();
        var catalog = Discovery.Widgets(typeof(App).Assembly);

        var items = editor.Gallery.SelectMany(g => g.Items).ToList();

        Assert.Equal(catalog.Descriptors.Count, items.Count);
        Assert.All(items, item =>
        {
            catalog.TryGet(item.TypeId, out var descriptor);
            Assert.True(item.Preview?.IsSample, $"{item.TypeId} has no sample preview.");
            Assert.NotEmpty(item.Line);
            Assert.Equal(descriptor!.SupportedSizes.Count, item.Sizes.Count);
            Assert.Equal(descriptor.SupportedSizes.Min(), item.Preview!.Size);
        });
        Assert.Equal(["Media", "Productivity", "System", "Your own", "Malaysia"], editor.Gallery.Select(g => g.Name));
    }

    [Fact]
    public void DragFromTheGallery_ShowsWhereItGoes_ThenAddsItThere()
    {
        using var editor = NewEditor();

        editor.BeginDrag(EditorDrag.FromGallery("stopwatch"));
        editor.DragOver(editor.Spot, 0);

        Assert.Equal(["stopwatch", "media"], editor.Picture.Tiles.Select(t => t.InstanceId));
        Assert.True(editor.Picture.Tiles[0].IsMarker);
        Assert.Equal("Let go to add it here", editor.DragHint);
        Assert.False(editor.IsDirty);

        Assert.Equal(EditResult.Applied, editor.Drop());

        Assert.Equal(["stopwatch", "media"], editor.Picture.Tiles.Select(t => t.InstanceId));
        Assert.DoesNotContain(editor.Picture.Tiles, t => t.IsMarker);
        Assert.Equal(0, editor.SelectedIndex);
        Assert.True(editor.Picture.Tiles[0].IsSelected);
        editor.UndoCommand.Execute(null);
        Assert.Equal(["media"], editor.Picture.Tiles.Select(t => t.InstanceId));
    }

    [Fact]
    public void DragFromTheGallery_LetGoOutside_ChangesNothing()
    {
        using var editor = NewEditor();

        editor.BeginDrag(EditorDrag.FromGallery("stopwatch"));
        editor.DragOver(null, 0);
        Assert.Equal("Let go here to cancel", editor.DragHint);

        Assert.Equal(EditResult.Unchanged, editor.Drop());
        Assert.False(editor.IsDirty);
        Assert.False(editor.IsDragging);
    }

    [Fact]
    public void DragWhereItCannotGo_SaysSo_AndChangesNothing()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Idle;

        editor.BeginDrag(EditorDrag.FromGallery("media"));
        editor.DragOver(editor.Spot, 0);

        Assert.Equal("Not for this spot", editor.DragHint);
        Assert.DoesNotContain(editor.Picture.Tiles, t => t.IsMarker);
        Assert.Equal(EditResult.Rejected, editor.Drop());
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void DragATileOffTheNotch_RemovesIt_AndUndoBringsItBack()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "focus-timer");

        editor.BeginDrag(EditorDrag.FromPreview(editor.Spot, 1));
        Assert.True(editor.IsDraggingPlaced);
        editor.DragOver(null, 0);
        Assert.Equal(["media"], editor.Picture.Tiles.Select(t => t.InstanceId));
        Assert.Equal("Let go to remove it", editor.DragHint);

        Assert.Equal(EditResult.Applied, editor.Drop());
        Assert.Single(editor.Tiles);
        Assert.Equal("Removed. Press Undo to bring it back.", editor.StatusText);

        editor.UndoCommand.Execute(null);
        Assert.Equal(2, editor.Tiles.Count);
    }

    [Fact]
    public void DragATileWithinTheNotch_Reorders()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "focus-timer");
        AddFromGallery(editor, "stopwatch");

        editor.BeginDrag(EditorDrag.FromPreview(editor.Spot, 0));
        editor.DragOver(editor.Spot, editor.DropIndexAt(10_000, 10));
        Assert.Equal(["focus-timer", "stopwatch", "media"], editor.Picture.Tiles.Select(t => t.InstanceId));
        editor.Drop();

        Assert.Equal(["Focus timer", "Stopwatch", "Now playing"], editor.Tiles.Select(t => t.Name));
        Assert.Equal(2, editor.SelectedIndex);
    }

    [Fact]
    public void DragATileOntoAnotherPage_MovesItThere()
    {
        _services.Layout.Update(d => d.States.Panel.Pages[0].Placements.Insert(0, new WidgetPlacement { WidgetId = "media", Size = WidgetSize.Medium }));
        using var editor = NewEditor();
        editor.Tab = EditorTab.Panel;
        editor.AddPageCommand.Execute(null);
        editor.SelectedPage = editor.Pages[0];

        editor.BeginDrag(EditorDrag.FromPreview(editor.Spot, 1));
        editor.DragOver(LayoutEditorViewModel.PageSpot(1), int.MaxValue);
        Assert.Equal("Let go to move it to Page 2", editor.DragHint);
        editor.Drop();

        Assert.Equal(1, editor.SelectedPage?.Index);
        Assert.Equal(["missed-alerts"], editor.Picture.Tiles.Select(t => t.InstanceId));
        editor.SelectedPage = editor.Pages[0];
        Assert.Equal(["media"], editor.Picture.Tiles.Select(t => t.InstanceId));
    }

    [Fact]
    public void TheSelectedWidget_MovesResizesAndRemoves_WithoutAMouse()
    {
        using var editor = NewEditor();
        AddFromGallery(editor, "focus-timer");
        Assert.Equal(1, editor.SelectedIndex);
        Assert.Equal("Focus timer", editor.Selected?.Name);

        editor.MoveSelected(-1);
        Assert.Equal(0, editor.SelectedIndex);
        Assert.Equal("focus-timer", editor.Picture.Tiles[0].InstanceId);

        editor.Selected!.Sizes.Single(s => s.Name == "Medium").Pick.Execute(null);
        Assert.Equal(WidgetSize.Medium, editor.Picture.Tiles[0].Size);
        Assert.True(editor.Picture.Tiles[0].IsSelected);

        editor.RemoveSelected();
        Assert.Single(editor.Tiles);
        Assert.False(editor.HasSelection);
        Assert.True(editor.ShowsSelectHint);
    }

    [Fact]
    public void DropIndexAt_ReadsThePicture()
    {
        using var editor = NewEditor();

        // The open notch holds one Medium widget, 320 wide.
        Assert.Equal(0, editor.DropIndexAt(10, 10));
        Assert.Equal(1, editor.DropIndexAt(300, 10));
    }

    [Fact]
    public void Priority_ShowsTheSmallNotchOfTheFirstLiveWidget()
    {
        using var editor = NewEditor();
        editor.Tab = EditorTab.Priority;

        Assert.Equal(["media"], editor.Picture.Tiles.Select(t => t.InstanceId));
        Assert.Equal("When something is live", editor.Picture.Label);
    }

    [Fact]
    public void Window_Builds() => Sta.Run(() =>
    {
        using var editor = NewEditor();
        var window = (System.Windows.Window)Activator.CreateInstance(
            typeof(LayoutEditorWindow),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null,
            [editor, null],
            null)!;
        try
        {
            window.Measure(new System.Windows.Size(1120, 760));
            window.Arrange(new System.Windows.Rect(0, 0, 1120, 760));
            window.UpdateLayout();
        }
        finally
        {
            window.Close();
        }
    });

    private LayoutEditorViewModel NewEditor() =>
        new(_services.Layout, Discovery.Widgets(typeof(App).Assembly), _services.Text, _backup);

    private static void AddFromGallery(LayoutEditorViewModel editor, string typeId) =>
        editor.Gallery.SelectMany(g => g.Items).Single(i => i.TypeId == typeId).Add.Execute(null);

    private static void EmptyThePage(LayoutEditorViewModel editor)
    {
        while (editor.Tiles.Count > 0)
            editor.Tiles[0].Remove.Execute(null);
    }

    private sealed class FakeWindow : ILayoutEditorWindow
    {
        public int Activations { get; private set; }

        public event EventHandler? Closed;

        public void Activate() => Activations++;

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
