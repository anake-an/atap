using System.Text.Json;
using System.Text.Json.Nodes;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Tests.Storage;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Settings;

/// <summary>settings.json on a fake clock and a hand-fired watch, in a temp folder.</summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));
    private readonly CountingTime _time = new(new DateTimeOffset(2026, 10, 25, 8, 15, 0, TimeSpan.Zero));
    private readonly ManualFileWatch _watch = new();
    private readonly List<SettingsStore> _stores = [];

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_WritesNothing()
    {
        var store = Store();

        Assert.Equal(FileLoadOutcome.CreatedDefaults, store.Load());
        Assert.False(store.FileExisted);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Load_ExistingFile_IsLoaded()
    {
        Write("""{ "schemaVersion": 1, "values": { "a.flag": true } }""");
        var store = Store();

        Assert.Equal(FileLoadOutcome.Loaded, store.Load());
        Assert.True(store.FileExisted);
        Assert.True(store.Values.GetBool("a.flag"));
    }

    [Fact]
    public void Load_CorruptFile_IsMovedAsideAndNothingIsWritten()
    {
        Write("{ \"values\": ");
        var store = Store();

        Assert.Equal(FileLoadOutcome.RecoveredFromCorrupt, store.Load());

        Assert.True(store.FileExisted);
        Assert.False(store.IsReadOnly);
        Assert.False(File.Exists(SettingsPath));
        Assert.True(File.Exists(Path.Combine(_directory, "settings.corrupt-20261025-081500.json")));
    }

    [Fact]
    public void Load_LockedFile_IsUnreadableAndNeverWritten()
    {
        var json = """{ "schemaVersion": 1, "values": { "a.flag": true } }""";
        Write(json);
        var store = Store();

        using (new FileStream(SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(FileLoadOutcome.Unreadable, store.Load());
            Assert.Equal(SettingWriteResult.ReadOnly, store.Set("a.flag", false));
        }
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.True(store.IsReadOnly);
        Assert.False(store.Values.GetBool("a.flag"));
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Set_WritesTheValueAndKeepsOtherFields()
    {
        Write("""{ "schemaVersion": 1, "values": { "other.key": 3 }, "rules": [ { "id": "r1" } ], "later": { "x": 1 } }""");
        var store = Store();
        store.Load();
        store.Register([new SettingDefinition("language.choice", "l", SettingType.Choice, "system", ["system", "en", "ms"])]);
        var changed = new List<string>();
        store.Changed += (_, e) => changed.AddRange(e.Keys);

        Assert.Equal(SettingWriteResult.Applied, store.Set("language.choice", "ms"));
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal("ms", store.Values.GetString("language.choice"));
        Assert.Equal(["language.choice"], changed);
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal("ms", root["values"]!["language.choice"]!.GetValue<string>());
        Assert.Equal(3, root["values"]!["other.key"]!.GetValue<int>());
        Assert.Equal("r1", root["rules"]![0]!["id"]!.GetValue<string>());
        Assert.Equal(1, root["later"]!["x"]!.GetValue<int>());
    }

    [Fact]
    public void Set_InvalidOrUnchangedValues_WriteNothing()
    {
        var store = Store();
        store.Load();
        store.Register([new SettingDefinition("a.flag", "l", SettingType.Bool, false)]);

        Assert.Equal(SettingWriteResult.Invalid, store.Set("a.flag", "yes"));
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.False(File.Exists(SettingsPath));
        Assert.Equal(SettingWriteResult.Applied, store.Set("a.flag", true));
        Assert.Equal(SettingWriteResult.Unchanged, store.Set("a.flag", true));
    }

    [Fact]
    public void Set_Null_RemovesTheValueSoTheDefaultApplies()
    {
        var store = Store();
        store.Load();
        store.Register([new SettingDefinition("a.count", "l", SettingType.Int, 4)]);
        store.Set("a.count", 9);
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(SettingWriteResult.Applied, store.Set("a.count", null));
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(4, store.Values.GetInt("a.count"));
        Assert.Null(JsonNode.Parse(File.ReadAllText(SettingsPath))!["values"]!["a.count"]);
    }

    [Fact]
    public void NewerFile_IsReadOnly()
    {
        Directory.CreateDirectory(_directory);
        var json = """{ "schemaVersion": 5, "values": { "a.flag": true } }""";
        File.WriteAllText(SettingsPath, json);
        var store = Store();

        Assert.Equal(FileLoadOutcome.NewerVersionReadOnly, store.Load());
        Assert.True(store.IsReadOnly);
        Assert.True(store.Values.GetBool("a.flag"));
        Assert.Equal(SettingWriteResult.ReadOnly, store.Set("a.flag", false));
        _time.Advance(TimeSpan.FromSeconds(5));
        store.Dispose();
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void NewerFileWrittenSinceLoad_IsNotDowngraded()
    {
        // For example a settings.json synced from a PC with a newer Atap while this one runs.
        var store = Store();
        store.Load();
        Assert.Equal(SettingWriteResult.Applied, store.Set("a.flag", true));
        Assert.Equal(SettingWriteResult.Applied, store.Flush());
        var newer = """{ "schemaVersion": 2, "values": { "a.flag": true }, "newField": 1 }""";
        File.WriteAllText(SettingsPath, newer);

        Assert.Equal(SettingWriteResult.Applied, store.Set("a.flag", false));
        Assert.Equal(SettingWriteResult.Failed, store.Flush());
        Assert.Equal(SettingWriteResult.ReadOnly, store.SetRules(JsonSerializer.SerializeToElement(new[] { new { id = "r1" } })));
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(newer, File.ReadAllText(SettingsPath));
        Assert.True(store.IsReadOnly);
        // The value still applies for this session.
        Assert.False(store.Values.GetBool("a.flag"));
    }

    [Theory]
    [InlineData("plain-token-value")]
    [InlineData("secret:")]
    [InlineData("secret:   ")]
    [InlineData("Secret:7f2a")]
    public void Set_Secret_RefusesAnythingButAReference(string value)
    {
        var store = Store();
        store.Load();
        store.Register([new SettingDefinition("sync.token", "l", SettingType.Secret, null)]);

        Assert.Equal(SettingWriteResult.Invalid, store.Set("sync.token", value));
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Set_Secret_AcceptsAReference()
    {
        var store = Store();
        store.Load();
        store.Register([new SettingDefinition("sync.token", "l", SettingType.Secret, null)]);

        Assert.Equal(SettingWriteResult.Applied, store.Set("sync.token", "secret:7f2a"));
        Assert.Equal("secret:7f2a", store.Values.GetSecretRef("sync.token"));
    }

    [Fact]
    public void Rules_RoundTripAndKeepValues()
    {
        Write("""{ "schemaVersion": 1, "values": { "other.key": 3 }, "rules": [ { "id": "r1" } ] }""");
        var store = Store();
        store.Load();
        var changed = new List<string>();
        store.Changed += (_, e) => changed.AddRange(e.Keys);
        Assert.Equal("r1", store.Rules[0].GetProperty("id").GetString());

        var rules = JsonSerializer.SerializeToElement(new JsonArray(new JsonObject { ["id"] = "r2" }, new JsonObject { ["id"] = "r3" }));
        Assert.Equal(SettingWriteResult.Applied, store.SetRules(rules));
        Assert.Equal(SettingWriteResult.Unchanged, store.SetRules(rules));
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal([SettingsChangedEventArgs.RulesKey], changed);
        var root = JsonNode.Parse(File.ReadAllText(SettingsPath))!;
        Assert.Equal("r3", root["rules"]![1]!["id"]!.GetValue<string>());
        Assert.Equal(3, root["values"]!["other.key"]!.GetValue<int>());
        var reload = Store();
        reload.Load();
        Assert.Equal(2, reload.Rules.GetArrayLength());
    }

    [Fact]
    public void Rules_MissingOrNotAList_AreEmpty_AndOnlyAListCanBeSet()
    {
        Write("""{ "schemaVersion": 1, "rules": { "id": "r1" } }""");
        var store = Store();
        store.Load();

        Assert.Equal(0, store.Rules.GetArrayLength());
        Assert.Equal(SettingWriteResult.Invalid, store.SetRules(JsonSerializer.SerializeToElement(new { id = "r1" })));
    }

    [Fact]
    public void TenSetsWithin500Ms_GiveOneWrite()
    {
        var store = Store();
        store.Load();
        var writes = 0;
        store.Saved += (_, _) => writes++;

        for (var i = 1; i <= 10; i++)
        {
            store.Set("volume.step", i);
            _time.Advance(TimeSpan.FromMilliseconds(40));
        }
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(1, writes);
        Assert.Equal(10, JsonNode.Parse(File.ReadAllText(SettingsPath))!["values"]!["volume.step"]!.GetValue<int>());
    }

    [Fact]
    public void HandEditMadeWhileRunning_IsKeptAcrossSet()
    {
        Write("""{ "schemaVersion": 1, "values": { "a.flag": true } }""");
        var store = Store();
        store.Load();

        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "values": { "a.flag": true, "b.name": "typed by hand" } }""");
        store.Set("a.flag", false);
        _time.Advance(JsonFileStore.FlushDelay);

        var values = JsonNode.Parse(File.ReadAllText(SettingsPath))!["values"]!;
        Assert.False(values["a.flag"]!.GetValue<bool>());
        Assert.Equal("typed by hand", values["b.name"]!.GetValue<string>());
        Assert.Equal("typed by hand", store.Values.GetString("b.name"));
    }

    [Fact]
    public void SelfWrite_GivesNoChange_AndAnOutsideWriteGivesOneWithTheKeysThatDiffer()
    {
        Write("""{ "schemaVersion": 1, "values": { "a.flag": true, "b.name": "x", "c.size": 2 } }""");
        var store = Store();
        store.Load();
        store.Set("a.flag", false);
        _time.Advance(JsonFileStore.FlushDelay);
        var changes = new List<IReadOnlyCollection<string>>();
        store.Changed += (_, e) => changes.Add(e.Keys);

        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);
        Assert.Empty(changes);

        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "values": { "a.flag": false, "b.name": "y", "d.new": 1 }, "rules": [ { "id": "r1" } ] }""");
        _watch.Fire();
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        var keys = Assert.Single(changes);
        Assert.Equal(["b.name", "c.size", "d.new", SettingsChangedEventArgs.RulesKey], keys.Order(StringComparer.Ordinal));
        Assert.Equal("y", store.Values.GetString("b.name"));
        Assert.Equal(1, store.Rules.GetArrayLength());
    }

    [Fact]
    public void Values_AreSnapshots()
    {
        var store = Store();
        store.Load();
        store.Set("a.count", 1);
        var before = store.Values;

        store.Set("a.count", 2);

        Assert.Equal(1, before.Raw["a.count"].GetInt32());
        Assert.Equal(2, store.Values.Raw["a.count"].GetInt32());
    }

    private SettingsStore Store()
    {
        var store = new SettingsStore(SettingsPath, _time, InlineContext.Instance, _watch);
        _stores.Add(store);
        return store;
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
    }
}
