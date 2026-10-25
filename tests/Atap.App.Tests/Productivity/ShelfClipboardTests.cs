using System.Text.Json;
using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Platform.Clipboard;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Shelf;

namespace Atap.Tests.Productivity;

public sealed class ShelfClipboardTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly OwnerLifetime _lifetime = new("shelf");
    private readonly ObservableValue<WidgetVisibility> _visibility = new(WidgetVisibility.Hidden);

    public void Dispose()
    {
        _lifetime.Dispose();
        _services.Dispose();
    }

    [Fact]
    public async Task Clipboard_ListensOnlyWhileRunning_AndForgetsOnStop()
    {
        var watchers = new List<FakeClipboard>();
        var descriptor = new ClipboardDescriptor();
        var widget = new ClipboardWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), () =>
        {
            var watcher = new FakeClipboard();
            watchers.Add(watcher);
            return watcher;
        });
        var view = Assert.IsType<ClipboardViewModel>(widget.ViewModel);
        Assert.Empty(watchers);

        await widget.StartAsync(SettingValues.Empty);
        watchers[0].Raise("one");
        watchers[0].Raise("two\nlines");
        watchers[0].Raise("one");

        Assert.Equal(["one", "two…"], view.Rows.Select(r => r.Preview));
        view.Rows[1].Copy.Execute(null);
        Assert.Equal("two\nlines", watchers[0].Copies.Single());
        Assert.Equal("two…", view.Rows[0].Preview);

        await widget.StopAsync();
        Assert.True(watchers[0].Disposed);
        Assert.Empty(view.Rows);
    }

    [Fact]
    public async Task KeepHistory_SavesProtected_AndReadsItBackOnStart()
    {
        var keep = KeepSettings(true);
        var (widget, watcher) = CreateClipboard();
        await widget.StartAsync(keep);
        watcher().Raise("first secret note");
        watcher().Raise("second line");

        // Saved once, after the short wait.
        Assert.False(File.Exists(HistoryPath));
        _services.Time.Advance(ClipboardWidget.SaveDelay + TimeSpan.FromMilliseconds(100));
        Assert.True(File.Exists(HistoryPath));

        var saved = File.ReadAllText(HistoryPath);
        Assert.DoesNotContain("first secret note", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("second line", saved, StringComparison.Ordinal);

        await widget.StopAsync();
        Assert.Empty(Assert.IsType<ClipboardViewModel>(widget.ViewModel).Rows);

        var (again, _) = CreateClipboard();
        await again.StartAsync(keep);
        Assert.Equal(["second line", "first secret note"], Assert.IsType<ClipboardViewModel>(again.ViewModel).Rows.Select(r => r.Text));
        await again.StopAsync();
    }

    [Fact]
    public async Task KeepHistory_SavesWhatIsPendingOnStop()
    {
        var keep = KeepSettings(true);
        var (widget, watcher) = CreateClipboard();
        await widget.StartAsync(keep);
        watcher().Raise("quick copy");
        await widget.StopAsync();

        var (again, _) = CreateClipboard();
        await again.StartAsync(keep);
        Assert.Equal(["quick copy"], Assert.IsType<ClipboardViewModel>(again.ViewModel).Rows.Select(r => r.Text));
        await again.StopAsync();
    }

    [Fact]
    public async Task KeepHistory_Off_DeletesTheSavedFile_AndSavesNothing()
    {
        var (widget, watcher) = CreateClipboard();
        await widget.StartAsync(KeepSettings(true));
        watcher().Raise("kept");
        await widget.StopAsync();
        Assert.True(File.Exists(HistoryPath));

        var (off, offWatcher) = CreateClipboard();
        await off.StartAsync(KeepSettings(false));
        Assert.False(File.Exists(HistoryPath));
        Assert.Empty(Assert.IsType<ClipboardViewModel>(off.ViewModel).Rows);

        offWatcher().Raise("not kept");
        _services.Time.Advance(ClipboardWidget.SaveDelay + TimeSpan.FromMilliseconds(100));
        await off.StopAsync();
        Assert.False(File.Exists(HistoryPath));
    }

    [Fact]
    public void KeepHistory_IsOffByDefault_AndNeverSyncs()
    {
        var keep = Assert.Single(new ClipboardDescriptor().Settings, s => s.Key == ClipboardDescriptor.KeepKey);
        Assert.Equal(false, keep.DefaultValue);
        Assert.Equal(SettingScope.Machine, keep.Scope);
    }

    [Fact]
    public async Task Shelf_KeepsPaths_AndGreysMissingFiles()
    {
        Directory.CreateDirectory(_services.Paths.Local);
        var existing = Path.Combine(_services.Paths.Local, "here.txt");
        File.WriteAllText(existing, "x");
        var missing = Path.Combine(_services.Paths.Local, "gone.txt");
        var descriptor = new FileShelfDescriptor();
        var opened = new List<string>();
        var context = _services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility);
        var widget = new FileShelfWidget(context, opened.Add, FileShelfDescriptor.PathExists);
        await widget.StartAsync(SettingValues.Empty);
        var view = Assert.IsType<FileShelfViewModel>(widget.ViewModel);

        view.Drop([existing, missing]);

        Assert.Equal(["here.txt", "gone.txt"], view.Rows.Select(r => r.Name));
        Assert.True(view.Rows[0].Exists);
        Assert.False(view.Rows[1].Exists);
        Assert.False(view.Rows[1].Open.CanExecute(null));
        view.Rows[0].Open.Execute(null);
        Assert.Equal([existing], opened);
        await widget.StopAsync();

        // A new instance reads the same paths back from machine storage.
        var again = new FileShelfWidget(context, _ => { }, FileShelfDescriptor.PathExists);
        await again.StartAsync(SettingValues.Empty);
        Assert.Equal(2, Assert.IsType<FileShelfViewModel>(again.ViewModel).Rows.Count);
        await again.StopAsync();
    }

    private string HistoryPath => Path.Combine(_services.Paths.MachineState, ClipboardDescriptor.TypeIdValue, ClipboardHistoryStore.FileName + ".json");

    private static SettingValues KeepSettings(bool keep) => new(
        new ClipboardDescriptor().Settings,
        new Dictionary<string, JsonElement> { [ClipboardDescriptor.KeepKey] = JsonSerializer.SerializeToElement(keep) });

    /// <summary>A clipboard widget, and the watcher it made last.</summary>
    private (ClipboardWidget Widget, Func<FakeClipboard> Watcher) CreateClipboard()
    {
        FakeClipboard? last = null;
        var descriptor = new ClipboardDescriptor();
        var widget = new ClipboardWidget(_services.CreateContext(descriptor.TypeId, descriptor, _lifetime, _visibility), () => last = new FakeClipboard());
        return (widget, () => last!);
    }

    private sealed class FakeClipboard : IClipboardWatcher
    {
        public List<string> Copies { get; } = [];

        public bool Disposed { get; private set; }

        public event EventHandler<string>? Copied;

        public void Raise(string text) => Copied?.Invoke(this, text);

        public void Copy(string text) => Copies.Add(text);

        public void Dispose() => Disposed = true;
    }
}
