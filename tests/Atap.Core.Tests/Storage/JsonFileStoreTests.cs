using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Atap.Core.Storage;

namespace Atap.Core.Tests.Storage;

/// <summary>
/// The generic store on a small two-version document, for what layout.json and settings.json do not reach:
/// a migration of its own, the no-defaults mode, backups that fail, and where timer work runs.
/// </summary>
public sealed class JsonFileStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly TempFolder _folder = new();
    private readonly CountingTime _time = new(new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.Zero));
    private readonly ManualFileWatch _watch = new();
    private readonly List<JsonFileStore<Note>> _stores = [];

    private string NotePath => _folder.File("notes.json");

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        _folder.Dispose();
    }

    [Fact]
    public void Missing_WithDefaultsAtLoad_IsCreated()
    {
        var store = Store();

        Assert.Equal(FileLoadOutcome.CreatedDefaults, store.Load());
        Assert.True(File.Exists(NotePath));
    }

    [Fact]
    public void Missing_WithoutDefaultsAtLoad_WritesNothingUntilTheFirstChange()
    {
        var store = Store(writeDefaults: false);

        Assert.Equal(FileLoadOutcome.CreatedDefaults, store.Load());
        Assert.False(Directory.Exists(_folder.Path));

        store.Update(n => n.Text = "first");
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.Equal("first", Read()["text"]!.GetValue<string>());
    }

    [Fact]
    public void Current_IsLoaded()
    {
        Write("""{ "schemaVersion": 2, "text": "hi", "tags": [ "a" ] }""");
        var store = Store();

        Assert.Equal(FileLoadOutcome.Loaded, store.Load());
        Assert.Equal("hi", store.Document.Text);
        Assert.False(store.IsReadOnly);
    }

    [Fact]
    public void Older_IsBackedUpUpgradedAndSaved()
    {
        // No schemaVersion: a file from the first release.
        const string v1 = """{ "title": "old name", "tags": [ "a" ] }""";
        Write(v1);
        var store = Store();

        Assert.Equal(FileLoadOutcome.Migrated, store.Load());

        Assert.Equal("old name", store.Document.Text);
        Assert.Equal(v1, File.ReadAllText(_folder.File("notes.v1.bak.json")));
        var saved = Read();
        Assert.Equal(2, saved["schemaVersion"]!.GetValue<int>());
        Assert.Equal("old name", saved["text"]!.GetValue<string>());
        Assert.Null(saved["title"]);
    }

    [Fact]
    public void Older_WhenTheBackupCannotBeWritten_IsUsedReadOnlyAndLeftAlone()
    {
        const string v1 = """{ "schemaVersion": 1, "title": "keep me" }""";
        Write(v1);
        // A folder where the backup file would go makes the copy fail.
        Directory.CreateDirectory(_folder.File("notes.v1.bak.json"));
        var store = Store();

        Assert.Equal(FileLoadOutcome.Unreadable, store.Load());

        Assert.True(store.IsReadOnly);
        Assert.Equal("keep me", store.Document.Text);
        Assert.Equal(FileWriteResult.ReadOnly, store.Update(n => n.Text = "changed"));
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(v1, File.ReadAllText(NotePath));
    }

    [Fact]
    public void Corrupt_WithoutDefaultsAtLoad_IsMovedAsideAndNothingIsWritten()
    {
        Write("not json at all");
        var store = Store(writeDefaults: false);

        Assert.Equal(FileLoadOutcome.RecoveredFromCorrupt, store.Load());

        Assert.False(File.Exists(NotePath));
        Assert.Equal("not json at all", File.ReadAllText(_folder.File("notes.corrupt-20261025-090000.json")));
        Assert.False(store.IsReadOnly);
    }

    [Theory]
    [InlineData("[ 1, 2 ]")]
    [InlineData("\"text\"")]
    [InlineData("""{ "text": 5 }""")]
    public void WrongShape_IsTreatedAsCorrupt(string json)
    {
        Write(json);
        var store = Store();

        Assert.Equal(FileLoadOutcome.RecoveredFromCorrupt, store.Load());
        Assert.Equal(string.Empty, store.Document.Text);
    }

    [Fact]
    public void Newer_IsReadOnlyAndNeverWritten()
    {
        const string newer = """{ "schemaVersion": 3, "text": "from the future", "colour": "red" }""";
        Write(newer);
        var store = Store();

        Assert.Equal(FileLoadOutcome.NewerVersionReadOnly, store.Load());

        Assert.Equal("from the future", store.Document.Text);
        Assert.Equal(FileWriteResult.ReadOnly, store.Update(n => n.Text = "mine"));
        Assert.Equal(FileWriteResult.ReadOnly, store.Flush());
        store.Dispose();
        Assert.Equal(newer, File.ReadAllText(NotePath));
        Assert.Single(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void FileWithAByteOrderMark_Loads()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(NotePath, """{ "schemaVersion": 2, "text": "bom" }""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var store = Store();

        Assert.Equal(FileLoadOutcome.Loaded, store.Load());
        Assert.Equal("bom", store.Document.Text);
    }

    [Fact]
    public void OlderFileWrittenWhileRunning_IsBackedUpAndUpgraded()
    {
        Write("""{ "schemaVersion": 2, "text": "now" }""");
        var store = Store();
        store.Load();
        var reloads = 0;
        store.Reloaded += (_, _) => reloads++;

        // For example an old backup copied back by hand.
        const string v1 = """{ "schemaVersion": 1, "title": "restored" }""";
        File.WriteAllText(NotePath, v1);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(1, reloads);
        Assert.Equal("restored", store.Document.Text);
        Assert.Equal(v1, File.ReadAllText(_folder.File("notes.v1.bak.json")));
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.Equal(2, Read()["schemaVersion"]!.GetValue<int>());
        Assert.Equal("restored", Read()["text"]!.GetValue<string>());
    }

    [Fact]
    public void UnreadableAtLoad_BecomesWritableOnceTheFileReadsAgain()
    {
        Write("""{ "schemaVersion": 2, "text": "locked" }""");
        var store = Store();
        using (new FileStream(NotePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(FileLoadOutcome.Unreadable, store.Load());

        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(FileLoadOutcome.Loaded, store.LastLoadOutcome);
        Assert.Equal("locked", store.Document.Text);
        Assert.Equal(FileWriteResult.Applied, store.Update(n => n.Text = "free"));
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.Equal("free", Read()["text"]!.GetValue<string>());
    }

    [Fact]
    public void Replay_DropsAChangeThatNoLongerFits_AndKeepsTheRest()
    {
        Write("""{ "schemaVersion": 2, "text": "a", "tags": [ "x" ] }""");
        var store = Store();
        store.Load();
        store.Update(n => n.Tags.RemoveAt(0));
        store.Update(n => n.Text = "b");

        // Edited by hand meanwhile: the tag is already gone.
        File.WriteAllText(NotePath, """{ "schemaVersion": 2, "text": "a", "tags": [], "pinned": true }""");
        Assert.Equal(FileWriteResult.Applied, store.Flush());

        var saved = Read();
        Assert.Equal("b", saved["text"]!.GetValue<string>());
        Assert.Empty(saved["tags"]!.AsArray());
        Assert.True(saved["pinned"]!.GetValue<bool>());
    }

    [Fact]
    public void TimerWork_RunsOnTheStoreContext()
    {
        var ui = new QueueContext();
        var store = Store(ui: ui);
        store.Load();
        store.Update(n => n.Text = "queued");

        _time.Advance(JsonFileStore.FlushDelay);
        Assert.True(store.HasPendingChanges);
        Assert.Equal(1, ui.Count);

        ui.RunAll();
        Assert.False(store.HasPendingChanges);
        Assert.Equal("queued", Read()["text"]!.GetValue<string>());
    }

    [Fact]
    public void AfterDispose_TimersAndWatchAreGone()
    {
        var store = Store();
        store.Load();
        store.Update(n => n.Text = "last");

        store.Dispose();

        Assert.Equal("last", Read()["text"]!.GetValue<string>());
        Assert.Equal(0, _watch.Open);
        Assert.Equal(0, _time.Armed);
        Assert.Throws<ObjectDisposedException>(() => store.Update(n => n.Text = "late"));
        _watch.Fire();
        _time.Advance(TimeSpan.FromSeconds(5));
    }

    private JsonFileStore<Note> Store(bool writeDefaults = true, SynchronizationContext? ui = null)
    {
        var format = new JsonFileFormat<Note>
        {
            CurrentVersion = 2,
            CreateDefaults = () => new Note(),
            Parse = root => root.Deserialize<Note>(Options),
            Serialize = note =>
            {
                note.SchemaVersion = 2;
                return JsonSerializer.SerializeToUtf8Bytes(note, Options);
            },
            Normalize = note =>
            {
                note.Text ??= string.Empty;
                note.Tags ??= [];
            },
            Migrations = [new TitleBecomesText()],
            WriteDefaultsAtLoad = writeDefaults,
        };
        var store = new JsonFileStore<Note>(NotePath, format, _time, ui ?? InlineContext.Instance, _watch);
        _stores.Add(store);
        return store;
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(NotePath, json);
    }

    private JsonNode Read() => JsonNode.Parse(File.ReadAllText(NotePath))!;

    public sealed class Note
    {
        public int SchemaVersion { get; set; } = 2;

        public string Text { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = [];

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private sealed class TitleBecomesText : IJsonMigration
    {
        public int FromVersion => 1;

        public void Apply(JsonObject root)
        {
            if (root["title"] is { } title)
            {
                root.Remove("title");
                root["text"] = title;
            }
        }
    }

    /// <summary>Holds posted work until the test runs it, as a busy UI thread would.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int Count => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void RunAll()
        {
            while (_queue.TryDequeue(out var work))
                work.Callback(work.State);
        }
    }
}
