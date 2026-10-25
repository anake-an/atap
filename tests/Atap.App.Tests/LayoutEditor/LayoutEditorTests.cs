using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
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
        Assert.Equal("Cannot go here", media.Note);
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

    private LayoutEditorViewModel NewEditor() =>
        new(_services.Layout, Discovery.Widgets(typeof(App).Assembly), _services.Text, _backup);

    private static void AddFromGallery(LayoutEditorViewModel editor, string typeId) =>
        editor.Gallery.SelectMany(g => g.Items).Single(i => i.TypeId == typeId).Add.Execute(null);

    private sealed class FakeWindow : ILayoutEditorWindow
    {
        public int Activations { get; private set; }

        public event EventHandler? Closed;

        public void Activate() => Activations++;

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }
}
