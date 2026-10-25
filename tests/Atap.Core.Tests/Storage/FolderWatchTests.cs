using Atap.Core.Layout;
using Atap.Core.Storage;

namespace Atap.Core.Tests.Storage;

/// <summary>
/// The real <see cref="FileSystemWatcher"/> on a temp folder, with the real clock. Each test waits a few
/// seconds at most. A private <see cref="FolderWatch"/> keeps these tests apart from the shared one.
/// </summary>
public sealed class FolderWatchTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempFolder _folder = new();
    private readonly FolderWatch _watch = new();

    public FolderWatchTests() => Directory.CreateDirectory(_folder.Path);

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void MissingFolder_CannotBeWatched()
    {
        Assert.Null(_watch.Watch(Path.Combine(_folder.Path, "nothing-here", "layout.json"), _ => { }));
        Assert.Equal(0, _watch.FolderCount);
    }

    [Fact]
    public void FilesInOneFolder_ShareOneWatcher_ThatClosesWithTheLast()
    {
        var layout = _watch.Watch(_folder.File("layout.json"), _ => { });
        var settings = _watch.Watch(_folder.File("settings.json"), _ => { });
        Assert.Equal(1, _watch.FolderCount);

        layout!.Dispose();
        Assert.Equal(1, _watch.FolderCount);
        settings!.Dispose();
        settings.Dispose();
        Assert.Equal(0, _watch.FolderCount);
    }

    [Fact]
    public void WriteToTheFile_IsReported_AndOtherFilesAreNot()
    {
        using var layoutEvent = new ManualResetEventSlim();
        using var otherEvent = new ManualResetEventSlim();
        using var layout = _watch.Watch(_folder.File("layout.json"), _ => layoutEvent.Set());
        using var other = _watch.Watch(_folder.File("settings.json"), _ => otherEvent.Set());

        File.WriteAllText(_folder.File("layout.json"), "{}");
        File.WriteAllText(_folder.File("notes.txt"), "not json, never reported");

        Assert.True(layoutEvent.Wait(Patience));
        Assert.False(otherEvent.Wait(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void TempFileRenamedOverTheFile_IsReported()
    {
        File.WriteAllText(_folder.File("layout.json"), "{}");
        using var changed = new ManualResetEventSlim();
        using var watch = _watch.Watch(_folder.File("layout.json"), _ => changed.Set());

        AtomicFile.Write(_folder.File("layout.json"), stream => stream.Write("{ }"u8));

        Assert.True(changed.Wait(Patience));
    }

    [Fact]
    public void LayoutStore_OnTheRealWatcher_ReloadsAnOutsideWriteButNotItsOwn()
    {
        var path = _folder.File("layout.json");
        using var store = new LayoutStore(path, timeProvider: TimeProvider.System, ui: InlineContext.Instance, watch: _watch);
        store.Load();
        store.Update(l => l.Placement.Offset = 5);
        Assert.Equal(LayoutWriteResult.Applied, store.Flush());
        var reloads = 0;
        using var reloaded = new ManualResetEventSlim();
        store.Changed += (_, _) =>
        {
            Interlocked.Increment(ref reloads);
            reloaded.Set();
        };

        // Its own writes, the defaults and the flush: the renames come back as events, and the hash says they are its own.
        Thread.Sleep(JsonFileStore.QuietDelay * 3);
        Assert.Equal(0, Volatile.Read(ref reloads));

        File.WriteAllText(path, """{ "schemaVersion": 3, "enabledWidgets": [ "clock" ] }""");

        Assert.True(reloaded.Wait(Patience));
        Thread.Sleep(JsonFileStore.QuietDelay * 3);
        Assert.Equal(1, Volatile.Read(ref reloads));
        Assert.Equal(["clock"], store.Active.EnabledWidgets);
    }
}
