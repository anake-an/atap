using System.Text.Json;
using System.Text.Json.Nodes;
using Atap.Core.Layout;
using Atap.Core.Placement;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Core.Tests.Layout;

public sealed class LayoutMigrationTests : IDisposable
{
    // A layout.json as version 0.1 wrote it, before placement existed.
    private const string V1Fixture = """
        {
          "schemaVersion": 1,
          "enabledWidgets": [ "media", "clock" ],
          "states": {
            "idle": { "placements": [ { "widgetId": "clock", "size": "small", "order": 0, "slot": "center" } ] },
            "compact": { "placements": [ { "widgetId": "media", "size": "small", "order": 0, "slot": "left" } ] },
            "expanded": { "placements": [ { "widgetId": "media", "size": "medium", "order": 0, "slot": "center" } ] },
            "panel": { "placements": [ { "widgetId": "media", "size": "large", "order": 0, "slot": "center" } ] }
          },
          "compactPriority": [ "media" ],
          "widgetSettings": { "clock": { "use24Hour": false } }
        }
        """;

    // A layout.json as the current release writes it.
    private const string V2Fixture = """
        {
          "schemaVersion": 2,
          "placement": { "anchor": "rightCenter", "offset": -40, "monitor": { "mode": "all" } },
          "enabledWidgets": [ "media", "clock" ],
          "states": {
            "idle": { "placements": [ { "widgetId": "clock", "size": "small", "order": 0, "slot": "center" } ] },
            "compact": {
              "placements": [
                { "widgetId": "media", "size": "small", "order": 0, "slot": "left" },
                { "widgetId": "media", "size": "small", "order": 0, "slot": "right" }
              ]
            },
            "expanded": { "placements": [ { "widgetId": "media", "size": "medium", "order": 0, "slot": "center" } ] },
            "panel": {
              "placements": [
                { "widgetId": "clock", "size": "small", "order": 1, "slot": "center" },
                { "widgetId": "media", "size": "large", "order": 0, "slot": "center" }
              ]
            }
          },
          "compactPriority": [ "media" ],
          "widgetSettings": { "clock": { "use24Hour": false } }
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    private string LayoutPath => Path.Combine(_directory, "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void V2_IsBackedUpBeforeTheUpgrade()
    {
        Write(V2Fixture);

        new LayoutStore(LayoutPath).Load();

        var backup = Path.Combine(_directory, "layout.v2.bak.json");
        Assert.True(File.Exists(backup));
        Assert.Equal(V2Fixture, File.ReadAllText(backup));
    }

    [Fact]
    public void V2_LoadsUnchangedAndSavesAsCurrent()
    {
        Write(V2Fixture);
        var store = new LayoutStore(LayoutPath);

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.Migrated, store.LastLoadOutcome);
        Assert.Equal(NotchAnchor.RightCenter, layout.Placement.Anchor);
        Assert.Equal(-40, layout.Placement.Offset);
        Assert.Equal(MonitorMode.All, layout.Placement.Monitor.Mode);
        Assert.Equal(["media", "clock"], layout.EnabledWidgets);
        Assert.Equal(["media"], layout.CompactPriority);
        Assert.Equal(2, layout.States.Compact.Placements.Count);
        Assert.Equal(WidgetSize.Medium, Assert.Single(layout.States.Expanded.Placements).Size);
        Assert.Empty(layout.Profiles);
        var settings = new SettingValues([new SettingDefinition("use24Hour", "l", SettingType.Bool, true)], layout.GetWidgetSettings("clock"));
        Assert.False(settings.GetBool("use24Hour"));

        var saved = JsonNode.Parse(File.ReadAllText(LayoutPath))!.AsObject();
        Assert.Equal(LayoutDocument.CurrentSchemaVersion, saved["schemaVersion"]!.GetValue<int>());
        Assert.IsType<JsonArray>(saved["profiles"]);

        var reload = new LayoutStore(LayoutPath);
        reload.Load();
        Assert.Equal(FileLoadOutcome.Loaded, reload.LastLoadOutcome);
    }

    [Fact]
    public void V2_PanelPlacementsBecomePageMain()
    {
        Write(V2Fixture);

        var layout = new LayoutStore(LayoutPath).Load();

        var page = Assert.Single(layout.States.Panel.Pages);
        Assert.Equal("main", page.Id);
        Assert.Equal("Main", page.Name);
        Assert.Equal(["clock", "media"], page.Placements.Select(p => p.WidgetId));
        Assert.Equal(WidgetSize.Large, page.Placements[1].Size);

        var panel = JsonNode.Parse(File.ReadAllText(LayoutPath))!["states"]!["panel"]!.AsObject();
        Assert.False(panel.ContainsKey("placements"));
        Assert.Equal("main", panel["pages"]![0]!["id"]!.GetValue<string>());
    }

    [Fact]
    public void V1_UpgradesThroughEveryStep()
    {
        Write(V1Fixture);
        var store = new LayoutStore(LayoutPath);

        var layout = store.Load();

        Assert.Equal(FileLoadOutcome.Migrated, store.LastLoadOutcome);
        Assert.True(File.Exists(Path.Combine(_directory, "layout.v1.bak.json")));
        Assert.Equal(NotchAnchor.TopCenter, layout.Placement.Anchor);
        Assert.Equal(MonitorMode.Primary, layout.Placement.Monitor.Mode);
        Assert.Equal("media", Assert.Single(Assert.Single(layout.States.Panel.Pages).Placements).WidgetId);
        Assert.Equal(LayoutDocument.CurrentSchemaVersion, layout.SchemaVersion);
    }

    [Fact]
    public void V2_WithoutAPanel_GetsAnEmptyMainPage()
    {
        Write("""{ "schemaVersion": 2, "enabledWidgets": [ "clock" ], "states": { "idle": { "placements": [] } } }""");

        var layout = new LayoutStore(LayoutPath).Load();

        var page = Assert.Single(layout.States.Panel.Pages);
        Assert.Equal("main", page.Id);
        Assert.Empty(page.Placements);
    }

    [Fact]
    public void Upgrade_KeepsUnknownFields()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 2, "custom": 7, "states": { "panel": { "placements": [], "tint": "red" } } }""")!.AsObject();

        LayoutMigrations.Upgrade(root, 2);

        Assert.Equal(7, root["custom"]!.GetValue<int>());
        Assert.Equal("red", root["states"]!["panel"]!["tint"]!.GetValue<string>());
        Assert.Equal(LayoutDocument.CurrentSchemaVersion, root["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Upgrade_KeepsPagesThatAlreadyExist()
    {
        var root = JsonNode.Parse("""{ "states": { "panel": { "pages": [ { "id": "work" } ], "placements": [] } } }""")!.AsObject();

        LayoutMigrations.Upgrade(root, 2);

        var panel = root["states"]!["panel"]!.AsObject();
        Assert.Equal("work", panel["pages"]![0]!["id"]!.GetValue<string>());
        Assert.False(panel.ContainsKey("placements"));
    }

    [Fact]
    public void FailedUpgradeSave_RunsAgainNextTime()
    {
        Write(V2Fixture);
        var store = new LayoutStore(LayoutPath);

        // Read-only: the backup works, the save does not.
        File.SetAttributes(LayoutPath, FileAttributes.ReadOnly);
        try
        {
            var layout = store.Load();
            Assert.Equal(FileLoadOutcome.Migrated, store.LastLoadOutcome);
            Assert.Equal("main", Assert.Single(layout.States.Panel.Pages).Id);
            Assert.Equal(V2Fixture, File.ReadAllText(LayoutPath));
        }
        finally
        {
            // The backup is a copy, so it is read-only too.
            File.SetAttributes(LayoutPath, FileAttributes.Normal);
            File.SetAttributes(Path.Combine(_directory, "layout.v2.bak.json"), FileAttributes.Normal);
        }

        var again = new LayoutStore(LayoutPath);
        again.Load();
        Assert.Equal(FileLoadOutcome.Migrated, again.LastLoadOutcome);
    }

    // The panel page 1.0 wrote for a new install and for the Minimal preset.
    private const string OldDefaultPanel = """
        { "pages": [ { "id": "main", "name": "Main", "placements": [
          { "widgetId": "media", "size": "medium", "order": 0, "slot": "center" },
          { "widgetId": "missed-alerts", "size": "medium", "order": 1, "slot": "center" } ] } ] }
        """;

    [Fact]
    public void V3_UntouchedDefaultPanel_FollowsTheHoverViewAfterTheUpgrade()
    {
        var root = JsonNode.Parse($$"""
            { "schemaVersion": 3, "states": { "panel": {{OldDefaultPanel}} },
              "profiles": [ { "id": "work", "states": { "panel": {{OldDefaultPanel}} } }, { "id": "games" } ] }
            """)!.AsObject();

        LayoutMigrations.Upgrade(root, 3);

        var layout = root.Deserialize<LayoutDocument>(LayoutStore.JsonOptions)!;
        layout.Normalize();
        var expected = DefaultLayout.Create().States.Panel.Pages[0].Placements;
        Assert.Equal(expected, Assert.Single(layout.States.Panel.Pages).Placements);
        Assert.Equal(expected, Assert.Single(layout.Profiles[0].States!.Panel.Pages).Placements);
        Assert.Null(layout.Profiles[1].States);
        Assert.True(PanelContent.FollowsHover(LayoutResolver.Resolve(layout, null).PanelPages));
    }

    [Theory]
    [InlineData("""{ "widgetId": "media", "size": "large", "order": 0, "slot": "center" }""")]
    [InlineData("""{ "widgetId": "clock", "size": "medium", "order": 0, "slot": "center" }""")]
    [InlineData("""{ "widgetId": "media", "size": "medium", "order": 0, "slot": "center", "pinned": true }""")]
    public void V3_PanelTheUserArranged_IsKept(string first)
    {
        var panel = $$"""
            { "pages": [ { "id": "main", "name": "Main", "placements": [ {{first}},
              { "widgetId": "missed-alerts", "size": "medium", "order": 1, "slot": "center" } ] } ] }
            """;
        var root = JsonNode.Parse($$"""{ "schemaVersion": 3, "states": { "panel": {{panel}} } }""")!.AsObject();
        var before = root["states"]!["panel"]!.ToJsonString();

        LayoutMigrations.Upgrade(root, 3);

        Assert.Equal(before, root["states"]!["panel"]!.ToJsonString());
    }

    [Fact]
    public void V3_DefaultPanelWithASecondPage_IsKept()
    {
        var root = JsonNode.Parse($$"""{ "schemaVersion": 3, "states": { "panel": {{OldDefaultPanel}} } }""")!.AsObject();
        root["states"]!["panel"]!["pages"]!.AsArray().Add(new JsonObject { ["id"] = "more", ["placements"] = new JsonArray() });
        var before = root["states"]!["panel"]!.ToJsonString();

        LayoutMigrations.Upgrade(root, 3);

        Assert.Equal(before, root["states"]!["panel"]!.ToJsonString());
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LayoutPath, json);
    }
}
