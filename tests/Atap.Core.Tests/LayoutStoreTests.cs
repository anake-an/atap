using System.Text.Json.Nodes;
using Atap.Core.Layout;
using Atap.Core.Placement;
using Atap.Core.Storage;
using Atap.Core.Tests.Storage;
using Atap.Core.Widgets;

namespace Atap.Core.Tests;

/// <summary>
/// layout.json on a fake clock and a hand-fired watch, in a temp folder. Writes land
/// <see cref="JsonFileStore.FlushDelay"/> after the last change, and outside changes are read
/// <see cref="JsonFileStore.QuietDelay"/> after the last file event.
/// </summary>
public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));
    private readonly CountingTime _time = new(new DateTimeOffset(2026, 10, 25, 14, 30, 5, TimeSpan.Zero));
    private readonly ManualFileWatch _watch = new();
    private readonly List<LayoutStore> _stores = [];

    private string LayoutPath => Path.Combine(_directory, "layout.json");

    public void Dispose()
    {
        foreach (var store in _stores)
            store.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_WritesAndReturnsDefaults()
    {
        var store = Store();

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.CreatedDefaults, store.LastLoadOutcome);
        Assert.True(File.Exists(LayoutPath));
        var defaults = DefaultLayout.Create();
        Assert.Equal(defaults.EnabledWidgets, layout.EnabledWidgets);
        Assert.Equal(defaults.CompactPriority, layout.CompactPriority);
        Assert.Equal(defaults.States.Compact.Placements, layout.States.Compact.Placements);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsLayout()
    {
        var original = new LayoutDocument
        {
            EnabledWidgets = ["clock", "media"],
            States = new StateLayouts
            {
                Idle = new StateLayout { Placements = [new WidgetPlacement { WidgetId = "clock", Order = 2 }] },
                Compact = new StateLayout
                {
                    Placements =
                    [
                        new WidgetPlacement { WidgetId = "media", Slot = PlacementSlot.Left },
                        new WidgetPlacement { WidgetId = "media", Slot = PlacementSlot.Right, Order = 1 },
                    ],
                },
                Panel = new PanelLayout
                {
                    Pages = [new PanelPage { Id = "main", Name = "Main", Placements = [new WidgetPlacement { WidgetId = "media", Size = WidgetSize.Large }] }],
                },
            },
            CompactPriority = ["media", "clock"],
        };
        original.SetWidgetSetting("clock", "use24Hour", false);
        original.SetWidgetSetting("clock", "style", "bold");

        Store().Save(original);
        var store = Store();
        var loaded = store.Load();

        Assert.Equal(FileLoadOutcome.Loaded, store.LastLoadOutcome);
        Assert.Equal(LayoutDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(original.EnabledWidgets, loaded.EnabledWidgets);
        Assert.Equal(original.CompactPriority, loaded.CompactPriority);
        Assert.Equal(original.States.Idle.Placements, loaded.States.Idle.Placements);
        Assert.Equal(original.States.Compact.Placements, loaded.States.Compact.Placements);
        Assert.Empty(loaded.States.Expanded.Placements);
        var page = Assert.Single(loaded.States.Panel.Pages);
        Assert.Equal("main", page.Id);
        Assert.Equal(original.States.Panel.Pages[0].Placements, page.Placements);

        var settings = new SettingValues(
            [new SettingDefinition("use24Hour", "24-hour", SettingType.Bool, true), new SettingDefinition("style", "Style", SettingType.String, "plain")],
            loaded.GetWidgetSettings("clock"));
        Assert.False(settings.GetBool("use24Hour"));
        Assert.Equal("bold", settings.GetString("style"));
    }

    [Fact]
    public void Save_WritesCamelCaseAndStringEnums()
    {
        Store().Save(DefaultLayout.Create());

        var json = File.ReadAllText(LayoutPath);

        Assert.Contains("\"schemaVersion\"", json);
        Assert.Contains("\"compactPriority\"", json);
        Assert.Contains("\"medium\"", json);
        Assert.Contains("\"left\"", json);
        Assert.False(File.Exists(LayoutPath + ".tmp"));
    }

    [Fact]
    public void Load_CorruptFile_BacksUpAndUsesDefaults()
    {
        Write("{ this is not json");
        var store = Store();

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.RecoveredFromCorrupt, store.LastLoadOutcome);
        var backupPath = Path.Combine(_directory, "layout.corrupt-20261025-143005.json");
        Assert.True(File.Exists(backupPath));
        Assert.Equal("{ this is not json", File.ReadAllText(backupPath));
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, layout.EnabledWidgets);

        // The replacement file is valid and loads cleanly.
        var reloadStore = Store();
        reloadStore.Load();
        Assert.Equal(FileLoadOutcome.Loaded, reloadStore.LastLoadOutcome);
    }

    [Fact]
    public void Load_CorruptFileHeldOpen_LeavesItAloneAndUsesDefaults()
    {
        Write("{ this is not json");
        var store = Store();

        LayoutDocument layout;
        // Another app has the file open without delete sharing, so it cannot be moved aside.
        using (new FileStream(LayoutPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            layout = store.Load();

        Assert.Equal(FileLoadOutcome.Unreadable, store.LastLoadOutcome);
        Assert.True(store.IsReadOnly);
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, layout.EnabledWidgets);
        Assert.Equal("{ this is not json", File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_LockedFile_IsUnreadableAndLeftAlone()
    {
        Store().Save(DefaultLayout.Create());
        var before = File.ReadAllText(LayoutPath);
        var store = Store();

        // Another app holds it with no sharing at all, so it cannot even be read.
        using (new FileStream(LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Load();
            Assert.Equal(LayoutWriteResult.ReadOnly, store.Update(l => l.Placement.Offset = 7));
        }
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(FileLoadOutcome.Unreadable, store.LastLoadOutcome);
        Assert.Equal(before, File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Update_KeepsHandEditsMadeSinceLoad()
    {
        var store = Store();
        store.Load();

        // The user edits the file by hand while Atap runs, and the watcher has not seen it yet.
        var edited = DefaultLayout.Create();
        edited.EnabledWidgets = ["clock"];
        edited.CompactPriority = [];
        edited.SetWidgetSetting("clock", "use24Hour", false);
        Store().Save(edited);

        var result = store.Update(layout =>
        {
            layout.Placement.Anchor = NotchAnchor.LeftCenter;
            layout.Placement.Monitor = MonitorSelection.All();
        });
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(LayoutWriteResult.Applied, result);
        var loaded = Store().Load();
        Assert.Equal(NotchAnchor.LeftCenter, loaded.Placement.Anchor);
        Assert.Equal(MonitorMode.All, loaded.Placement.Monitor.Mode);
        Assert.Equal(["clock"], loaded.EnabledWidgets);
        Assert.Empty(loaded.CompactPriority);
        var settings = new SettingValues([new SettingDefinition("use24Hour", "24-hour", SettingType.Bool, true)], loaded.GetWidgetSettings("clock"));
        Assert.False(settings.GetBool("use24Hour"));
        // The edit reached memory too.
        Assert.Equal(["clock"], store.Document.EnabledWidgets);
    }

    [Fact]
    public void Update_FileDoesNotParse_FailsAndLeavesIt()
    {
        var store = Store();
        store.Load();
        File.WriteAllText(LayoutPath, "{ half-typed");

        Assert.Equal(LayoutWriteResult.Applied, store.Update(layout => layout.Placement.Anchor = NotchAnchor.TopRight));
        var result = store.Flush();

        Assert.Equal(LayoutWriteResult.Failed, result);
        Assert.Equal(StoreProblem.FileDoesNotParse, store.Problem);
        Assert.Equal("{ half-typed", File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
        // The change still applies for this session.
        Assert.Equal(NotchAnchor.TopRight, store.Document.Placement.Anchor);
    }

    [Fact]
    public void Update_MissingFile_SavesTheWholeLayout()
    {
        var store = Store();

        Assert.Equal(LayoutWriteResult.Applied, store.Update(layout => layout.Placement.Anchor = NotchAnchor.RightCenter));
        _time.Advance(JsonFileStore.FlushDelay);

        var loaded = Store().Load();
        Assert.Equal(NotchAnchor.RightCenter, loaded.Placement.Anchor);
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, loaded.EnabledWidgets);
    }

    [Fact]
    public void Update_ReadOnlySession_ChangesMemoryOnly()
    {
        Write("{ this is not json");
        var store = Store();
        using (new FileStream(LayoutPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            store.Load();

        var result = store.Update(layout => layout.Placement.Anchor = NotchAnchor.TopLeft);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(LayoutWriteResult.ReadOnly, result);
        Assert.False(store.HasPendingChanges);
        Assert.Equal(NotchAnchor.TopLeft, store.Document.Placement.Anchor);
        Assert.Equal("{ this is not json", File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void Update_RaisesChangedAndRefreshesActive()
    {
        var store = Store();
        store.Load();
        var raised = 0;
        store.Changed += (_, _) => raised++;

        store.Update(layout => layout.EnabledWidgets.Remove("media"));

        Assert.Equal(1, raised);
        Assert.Equal(["clock", "missed-alerts"], store.Active.EnabledWidgets);
    }

    [Theory]
    [InlineData(int.MaxValue, NotchPlacement.MaxOffset)]
    [InlineData(int.MinValue, -NotchPlacement.MaxOffset)]
    [InlineData(-250, -250)]
    public void Load_Offset_IsClampedToTheAllowedRange(int offset, int expected)
    {
        Write($$"""{ "schemaVersion": 3, "placement": { "anchor": "topCenter", "offset": {{offset}} } }""");

        var layout = Store().Load();

        Assert.Equal(expected, layout.Placement.Offset);
    }

    [Fact]
    public void Load_NullsInFile_AreNormalized()
    {
        Write("""{ "schemaVersion": 1, "enabledWidgets": null, "states": { "idle": null } }""");

        var layout = Store().Load();

        Assert.Empty(layout.EnabledWidgets);
        Assert.Empty(layout.States.Idle.Placements);
        Assert.Empty(layout.CompactPriority);
        Assert.Equal(PanelLayout.MainPageId, Assert.Single(layout.States.Panel.Pages).Id);
    }

    [Fact]
    public void Load_SchemaV1File_MigratesToTopCenterOnPrimaryAndSavesBack()
    {
        Write("""
            {
              "schemaVersion": 1,
              "enabledWidgets": [ "media", "clock" ],
              "states": {
                "idle": { "placements": [ { "widgetId": "clock", "size": "small", "order": 0, "slot": "center" } ] }
              },
              "compactPriority": [ "media" ],
              "widgetSettings": { "clock": { "use24Hour": false } }
            }
            """);
        var store = Store();

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.Migrated, store.LastLoadOutcome);
        Assert.Equal(LayoutDocument.CurrentSchemaVersion, layout.SchemaVersion);
        Assert.Equal(NotchAnchor.TopCenter, layout.Placement.Anchor);
        Assert.Equal(0, layout.Placement.Offset);
        Assert.Equal(MonitorMode.Primary, layout.Placement.Monitor.Mode);

        // Existing content survives the upgrade.
        Assert.Equal(["media", "clock"], layout.EnabledWidgets);
        Assert.Equal("clock", Assert.Single(layout.States.Idle.Placements).WidgetId);
        var settings = new SettingValues([new SettingDefinition("use24Hour", "24-hour", SettingType.Bool, true)], layout.GetWidgetSettings("clock"));
        Assert.False(settings.GetBool("use24Hour"));

        // The upgraded file is written back and loads as current.
        var json = File.ReadAllText(LayoutPath);
        Assert.Contains($"\"schemaVersion\": {LayoutDocument.CurrentSchemaVersion}", json);
        Assert.Contains("\"topCenter\"", json);
        var reloadStore = Store();
        reloadStore.Load();
        Assert.Equal(FileLoadOutcome.Loaded, reloadStore.LastLoadOutcome);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsPlacement()
    {
        var original = DefaultLayout.Create();
        original.Placement = new NotchPlacement
        {
            Anchor = NotchAnchor.RightCenter,
            Offset = -40,
            Monitor = new MonitorSelection { Mode = MonitorMode.Specific, DeviceId = @"\\?\DISPLAY#DELA0F4#5&2&UID4352", FriendlyName = "DELL U2720Q" },
        };

        Store().Save(original);
        var loaded = Store().Load();

        Assert.Equal(NotchAnchor.RightCenter, loaded.Placement.Anchor);
        Assert.Equal(-40, loaded.Placement.Offset);
        Assert.Equal(MonitorMode.Specific, loaded.Placement.Monitor.Mode);
        Assert.Equal(original.Placement.Monitor.DeviceId, loaded.Placement.Monitor.DeviceId);
        Assert.Equal("DELL U2720Q", loaded.Placement.Monitor.FriendlyName);
        Assert.Contains("\"rightCenter\"", File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void Load_InvalidPlacement_IsNormalizedToDefaults()
    {
        Write("""
            { "schemaVersion": 3, "placement": { "anchor": 42, "monitor": { "mode": "specific" } } }
            """);

        var layout = Store().Load();

        Assert.Equal(NotchAnchor.TopCenter, layout.Placement.Anchor);
        Assert.Equal(MonitorMode.Primary, layout.Placement.Monitor.Mode);
        Assert.Null(layout.Placement.Monitor.DeviceId);
    }

    [Fact]
    public void Load_NullPlacement_IsNormalizedToDefaults()
    {
        Write("""{ "schemaVersion": 3, "placement": null }""");

        var layout = Store().Load();

        Assert.Equal(NotchAnchor.TopCenter, layout.Placement.Anchor);
        Assert.Equal(MonitorMode.Primary, layout.Placement.Monitor.Mode);
    }

    [Fact]
    public void Load_NewerSchema_IsReadOnlyAndNeverWritten()
    {
        var json = """{ "schemaVersion": 99, "enabledWidgets": [ "clock" ], "futureField": { "a": 1 } }""";
        Write(json);
        var store = Store();

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.NewerVersionReadOnly, store.LastLoadOutcome);
        Assert.Equal(["clock"], layout.EnabledWidgets);
        Assert.Equal(LayoutWriteResult.ReadOnly, store.Update(l => l.Placement.Anchor = NotchAnchor.TopLeft));
        Assert.Equal(LayoutWriteResult.ReadOnly, store.Flush());
        _time.Advance(TimeSpan.FromSeconds(5));
        store.Dispose();
        Assert.Equal(json, File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_NewerSchemaWithAShapeThisVersionCannotRead_UsesDefaultsAndLeavesTheFile()
    {
        var json = """{ "schemaVersion": 99, "enabledWidgets": [ { "id": "clock", "pinned": true } ] }""";
        Write(json);
        var store = Store();

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.NewerVersionReadOnly, store.LastLoadOutcome);
        Assert.True(store.IsReadOnly);
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, layout.EnabledWidgets);
        Assert.Equal(LayoutWriteResult.ReadOnly, store.Update(l => l.Placement.Anchor = NotchAnchor.TopLeft));
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(json, File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void Load_CurrentFile_IsNotUpgradedAgain()
    {
        Directory.CreateDirectory(_directory);
        Store().Save(DefaultLayout.Create());
        var written = File.GetLastWriteTimeUtc(LayoutPath);

        var store = Store();
        store.Load();

        Assert.Equal(FileLoadOutcome.Loaded, store.LastLoadOutcome);
        Assert.Equal(written, File.GetLastWriteTimeUtc(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("default")]
    [InlineData("Default")]
    public void Load_ProfileNamedDefault_GetsAFreeIdAndCannotHideTheTopLevel(string id)
    {
        Write($$"""
            { "schemaVersion": 3, "enabledWidgets": [ "clock", "media" ],
              "profiles": [ { "id": "{{id}}", "name": "Default", "enabledWidgets": [ "media" ] }, { "id": "default-2", "name": "Taken" } ] }
            """);
        var store = Store();

        var layout = store.Load();

        Assert.Equal(["default-3", "default-2"], layout.Profiles.Select(p => p.Id));
        Assert.Equal("Default", layout.Profiles[0].Name);
        Assert.Null(layout.FindProfile(EffectiveLayout.DefaultProfileId));
        store.SetActiveProfile(EffectiveLayout.DefaultProfileId);
        Assert.Equal(["clock", "media"], store.Active.EnabledWidgets);
    }

    [Fact]
    public void SaveThenLoad_KeepsUnknownFields()
    {
        Write("""
            { "schemaVersion": 3, "enabledWidgets": [ "clock" ], "laterFeature": { "on": true },
              "states": { "idle": { "placements": [ { "widgetId": "clock", "glow": 2 } ] } } }
            """);
        var store = Store();
        store.Load();

        store.Update(l => l.Placement.Offset = 5);
        _time.Advance(JsonFileStore.FlushDelay);

        var json = File.ReadAllText(LayoutPath);
        Assert.Contains("\"laterFeature\"", json);
        Assert.Contains("\"glow\"", json);
        Assert.Contains("\"offset\": 5", json);
    }

    [Fact]
    public void Update_TenChangesWithin500Ms_GiveOneWrite()
    {
        var store = Store();
        store.Load();
        var writes = 0;
        store.Saved += (_, _) => writes++;

        for (var i = 1; i <= 10; i++)
        {
            var offset = i * 10;
            store.Update(l => l.Placement.Offset = offset);
            _time.Advance(TimeSpan.FromMilliseconds(50));
        }

        // The last change was 50 ms ago, so the write is 450 ms away.
        _time.Advance(TimeSpan.FromMilliseconds(449));
        Assert.Equal(0, writes);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, writes);
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, writes);
        Assert.Equal(100, Store().Load().Placement.Offset);
    }

    [Fact]
    public void SelfWrite_GivesNoReload()
    {
        var store = Store();
        store.Load();
        store.Update(l => l.Placement.Offset = 12);
        _time.Advance(JsonFileStore.FlushDelay);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        // The rename of the temp file over layout.json comes back as file events.
        _watch.Fire();
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(0, changes);
        Assert.Equal(12, store.Document.Placement.Offset);
    }

    [Fact]
    public void ExternalWrite_GivesExactlyOneReload()
    {
        var store = Store();
        store.Load();
        var changes = 0;
        store.Changed += (_, _) => changes++;

        var edited = DefaultLayout.Create();
        edited.EnabledWidgets = ["clock"];
        Store().Save(edited);
        // An editor's save often shows up as several events.
        _watch.Fire();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _watch.Fire();
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay - TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, changes);
        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(1, changes);
        Assert.Equal(["clock"], store.Document.EnabledWidgets);
        Assert.Equal(["clock"], store.Active.EnabledWidgets);
        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ExternalWriteSeenByTheWatcher_IsKeptAcrossUpdate()
    {
        var store = Store();
        store.Load();
        var edited = DefaultLayout.Create();
        edited.CompactPriority = ["clock"];
        Store().Save(edited);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        store.Update(l => l.Placement.Anchor = NotchAnchor.TopLeft);
        _time.Advance(JsonFileStore.FlushDelay);

        var loaded = Store().Load();
        Assert.Equal(["clock"], loaded.CompactPriority);
        Assert.Equal(NotchAnchor.TopLeft, loaded.Placement.Anchor);
    }

    [Fact]
    public void QueuedChange_IsReplayedOnAnOutsideChange()
    {
        var store = Store();
        store.Load();
        store.Update(l => l.Placement.Anchor = NotchAnchor.TopLeft);

        // An outside edit arrives before the queued change is written.
        var edited = DefaultLayout.Create();
        edited.EnabledWidgets = ["clock"];
        Store().Save(edited);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(["clock"], store.Document.EnabledWidgets);
        Assert.Equal(NotchAnchor.TopLeft, store.Document.Placement.Anchor);
        _time.Advance(JsonFileStore.FlushDelay);
        var loaded = Store().Load();
        Assert.Equal(["clock"], loaded.EnabledWidgets);
        Assert.Equal(NotchAnchor.TopLeft, loaded.Placement.Anchor);
    }

    [Fact]
    public void NewerFileWrittenWhileRunning_IsNeverWritten()
    {
        var store = Store();
        store.Load();
        var newer = """{ "schemaVersion": 99, "enabledWidgets": [ "clock" ], "pins": [ 1 ] }""";
        File.WriteAllText(LayoutPath, newer);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(FileLoadOutcome.NewerVersionReadOnly, store.LastLoadOutcome);
        Assert.True(store.IsReadOnly);
        Assert.Equal(["clock"], store.Document.EnabledWidgets);
        Assert.Equal(LayoutWriteResult.ReadOnly, store.Update(l => l.Placement.Offset = 3));
        _time.Advance(TimeSpan.FromSeconds(5));
        store.Dispose();
        Assert.Equal(newer, File.ReadAllText(LayoutPath));
    }

    [Fact]
    public void NewerFileWrittenBeforeAQueuedChange_IsNeverDowngraded()
    {
        // For example a layout.json synced from a PC with a newer Atap, before the watcher noticed.
        var store = Store();
        store.Load();
        store.Update(l => l.Placement.Offset = 3);
        var newer = """{ "schemaVersion": 99, "enabledWidgets": [ "clock" ] }""";
        File.WriteAllText(LayoutPath, newer);

        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(newer, File.ReadAllText(LayoutPath));
        Assert.True(store.IsReadOnly);
        Assert.False(store.HasPendingChanges);
        // What the newer file says, with the change kept for this session.
        Assert.Equal(["clock"], store.Document.EnabledWidgets);
        Assert.Equal(3, store.Document.Placement.Offset);
        Assert.Equal(LayoutWriteResult.ReadOnly, store.Update(l => l.Placement.Offset = 4));
    }

    [Fact]
    public void FileThatStopsParsing_IsLeftAloneUntilItParsesAgain()
    {
        var store = Store();
        store.Load();
        var problems = 0;
        store.ProblemChanged += (_, _) => problems++;

        File.WriteAllText(LayoutPath, "{ \"enabledWidgets\": [ \"clo");
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(StoreProblem.FileDoesNotParse, store.Problem);
        Assert.Equal(1, problems);
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, store.Document.EnabledWidgets);
        Assert.Equal(LayoutWriteResult.Failed, store.Update(l => l.Placement.Anchor = NotchAnchor.TopRight));
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.Equal("{ \"enabledWidgets\": [ \"clo", File.ReadAllText(LayoutPath));
        Assert.Single(Directory.GetFiles(_directory));

        // The user finishes the edit. The queued change lands on top of it.
        File.WriteAllText(LayoutPath, """{ "schemaVersion": 3, "enabledWidgets": [ "clock" ] }""");
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);
        Assert.Equal(StoreProblem.None, store.Problem);
        Assert.Equal(2, problems);
        Assert.Equal(["clock"], store.Document.EnabledWidgets);
        Assert.Equal(NotchAnchor.TopRight, store.Document.Placement.Anchor);

        _time.Advance(JsonFileStore.FlushDelay);
        var loaded = Store().Load();
        Assert.Equal(["clock"], loaded.EnabledWidgets);
        Assert.Equal(NotchAnchor.TopRight, loaded.Placement.Anchor);
    }

    [Fact]
    public void LockedFile_IsRetriedOnce_ThenReported_AndTheNextChangeRetries()
    {
        var store = Store();
        store.Load();
        var problems = 0;
        store.ProblemChanged += (_, _) => problems++;

        using (new FileStream(LayoutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Update(l => l.Placement.Offset = 21);
            _time.Advance(JsonFileStore.FlushDelay);
            Assert.Equal(StoreProblem.None, store.Problem);
            _time.Advance(JsonFileStore.RetryDelay);
            Assert.Equal(StoreProblem.CannotWrite, store.Problem);
            Assert.Equal(1, problems);
            _time.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(1, problems);
        }

        Assert.Equal(21, store.Document.Placement.Offset);
        Assert.True(store.HasPendingChanges);
        store.Update(l => l.Placement.Anchor = NotchAnchor.TopLeft);
        _time.Advance(JsonFileStore.FlushDelay);

        Assert.Equal(StoreProblem.None, store.Problem);
        var loaded = Store().Load();
        Assert.Equal(21, loaded.Placement.Offset);
        Assert.Equal(NotchAnchor.TopLeft, loaded.Placement.Anchor);
    }

    [Fact]
    public void FileDeletedWhileRunning_IsWrittenWholeAtTheNextChange()
    {
        var store = Store();
        store.Load();
        var changes = 0;
        store.Changed += (_, _) => changes++;
        File.Delete(LayoutPath);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(0, changes);
        store.Update(l => l.Placement.Offset = 9);
        _time.Advance(JsonFileStore.FlushDelay);

        var loaded = Store().Load();
        Assert.Equal(9, loaded.Placement.Offset);
        Assert.Equal(DefaultLayout.Create().EnabledWidgets, loaded.EnabledWidgets);
    }

    [Fact]
    public void ProfileWithItsOwnList_KeepsTheAlarmRunning()
    {
        Write("""{ "schemaVersion": 3, "enabledWidgets": [ "clock", "alarm" ], "profiles": [ { "id": "gaming", "name": "Gaming", "enabledWidgets": [ "media" ] } ] }""");
        var store = Store();
        store.Load();
        store.SetActiveProfile("gaming");

        store.WorksWithoutPlacement = id => id == "alarm";

        Assert.Equal(["media", "alarm"], store.Active.EnabledWidgets);
    }

    [Fact]
    public void OutsideChange_DroppingTheActiveProfile_FallsBackToDefault()
    {
        Write("""{ "schemaVersion": 3, "enabledWidgets": [ "clock", "media" ], "profiles": [ { "id": "gaming", "name": "Gaming", "enabledWidgets": [ "media" ] } ] }""");
        var store = Store();
        store.Load();
        store.SetActiveProfile("gaming");
        Assert.Equal(["media"], store.Active.EnabledWidgets);

        File.WriteAllText(LayoutPath, """{ "schemaVersion": 3, "enabledWidgets": [ "clock" ] }""");
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        Assert.Equal(EffectiveLayout.DefaultProfileId, store.ActiveProfileId);
        Assert.Equal(["clock"], store.Active.EnabledWidgets);
    }

    [Fact]
    public void Flush_WritesAtOnce_AndDisposeWritesWhatIsLeft()
    {
        var store = Store();
        store.Load();

        store.Update(l => l.Placement.Offset = 1);
        Assert.True(store.HasPendingChanges);
        Assert.Equal(LayoutWriteResult.Applied, store.Flush());
        Assert.False(store.HasPendingChanges);
        Assert.Equal(1, Store().Load().Placement.Offset);

        store.Update(l => l.Placement.Offset = 2);
        store.Dispose();
        Assert.Equal(2, Store().Load().Placement.Offset);
    }

    [Fact]
    public void Idle_ArmsNoTimer()
    {
        var store = Store();
        store.Load();
        Assert.Equal(0, _time.Armed);

        store.Update(l => l.Placement.Offset = 1);
        Assert.Equal(1, _time.Armed);
        _time.Advance(JsonFileStore.FlushDelay);
        _watch.Fire();
        _time.Advance(JsonFileStore.QuietDelay);

        // Written and checked: nothing is left to wake for.
        Assert.Equal(0, _time.Armed);
    }

    [Fact]
    public void Load_WatchesTheFile_AndAStoppedWatchComesBackAfterTheNextWrite()
    {
        var store = Store();
        store.Load();
        Assert.Equal(1, _watch.Open);

        _watch.Fire(FileChange.Stopped);
        Assert.Equal(0, _watch.Open);
        _time.Advance(JsonFileStore.QuietDelay);

        store.Update(l => l.Placement.Offset = 1);
        _time.Advance(JsonFileStore.FlushDelay);
        Assert.Equal(1, _watch.Open);

        store.Dispose();
        Assert.Equal(0, _watch.Open);
    }

    [Fact]
    public void ProfileEditThroughUpdate_IsCopiedOnWriteAndSurvivesReplay()
    {
        Write("""{ "schemaVersion": 3, "enabledWidgets": [ "clock", "media" ], "profiles": [ { "id": "gaming", "name": "Gaming" } ] }""");
        var store = Store();
        store.Load();
        store.SetActiveProfile("gaming");

        store.Update(d => LayoutResolver.EnabledWidgetsToEdit(d, "gaming").Remove("clock"));
        // Someone renames the profile by hand before the write. The edit is replayed on that file.
        File.WriteAllText(LayoutPath, """{ "schemaVersion": 3, "enabledWidgets": [ "clock", "media" ], "profiles": [ { "id": "gaming", "name": "Games" } ] }""");
        _time.Advance(JsonFileStore.FlushDelay);

        var saved = JsonNode.Parse(File.ReadAllText(LayoutPath))!;
        Assert.Equal(["clock", "media"], saved["enabledWidgets"]!.AsArray().Select(n => n!.GetValue<string>()));
        var profile = saved["profiles"]![0]!;
        Assert.Equal("Games", profile["name"]!.GetValue<string>());
        Assert.Equal(["media"], profile["enabledWidgets"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Equal(["media"], store.Active.EnabledWidgets);
    }

    private LayoutStore Store()
    {
        var store = new LayoutStore(LayoutPath, timeProvider: _time, ui: InlineContext.Instance, watch: _watch);
        _stores.Add(store);
        return store;
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LayoutPath, json);
    }
}
