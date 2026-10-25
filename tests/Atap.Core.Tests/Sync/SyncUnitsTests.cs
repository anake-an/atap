using Atap.Core.Sync;
using static Atap.Core.Tests.Sync.SyncTestData;

namespace Atap.Core.Tests.Sync;

public sealed class SyncUnitsTests
{
    private const string LayoutV3 = """
        {
          "schemaVersion": 3,
          "placement": { "anchor": "topCenter", "offset": 0, "monitor": { "mode": "primary" } },
          "enabledWidgets": ["media", "clock", "json-url#nas"],
          "states": {
            "idle": { "placements": [{ "widgetId": "clock", "size": "small" }] },
            "compact": { "placements": [] },
            "expanded": { "placements": [] },
            "panel": { "pages": [{ "id": "main", "name": "Main", "placements": [] }] }
          },
          "compactPriority": ["focus-timer", "media", "battery", "next-meeting", "push", "waktu-solat", "next-class"],
          "widgetSettings": { "clock": { "use24Hour": true }, "next-meeting": { "icsUrl": "secret:7f2a" }, "bad:id": {}, "media": 5 },
          "profiles": [{ "id": "gaming", "name": "Gaming", "enabledWidgets": ["media"], "compactPriority": ["media"] }]
        }
        """;

    private const string SettingsV1 = """
        {
          "schemaVersion": 1,
          "values": { "language.choice": "system", "gestures.wheelVolume": true, "localApi.enabled": false, "malaysia.state": "SGR", "clock.style": null },
          "rules": [{ "id": "r1", "name": "Gaming", "enabled": true,
                      "when": [{ "kind": "foregroundApp", "apps": ["valorant.exe"] }],
                      "then": { "profile": "gaming", "slim": true, "quietAlerts": true } }]
        }
        """;

    [Fact]
    public void FromLayout_SplitsTheDocumentAndNeverIncludesPlacement()
    {
        var units = SyncUnits.FromLayout(Json(LayoutV3));

        Assert.Equal(
            [
                SyncUnitNames.CompactPriority,
                SyncUnitNames.EnabledWidgets,
                SyncUnitNames.Profiles,
                SyncUnitNames.States,
                "layout:widgetSettings:clock",
                "layout:widgetSettings:next-meeting",
            ],
            units.Keys.Order(StringComparer.Ordinal));
        Assert.True(SameJson("""["media","clock","json-url#nas"]""", units[SyncUnitNames.EnabledWidgets]));
        Assert.True(SameJson("""{"use24Hour":true}""", units["layout:widgetSettings:clock"]));
    }

    [Fact]
    public void FromLayout_WithoutEnabledWidgets_StillHasTheSentinel()
    {
        var units = SyncUnits.FromLayout(Json("""{ "schemaVersion": 2, "placement": {} }"""));

        Assert.True(SameJson("[]", Assert.Single(units).Value));
        Assert.Equal(SyncUnitNames.EnabledWidgets, units.Keys.Single());
    }

    [Fact]
    public void FromSettings_SplitsValuesAndRules()
    {
        var units = SyncUnits.FromSettings(Json(SettingsV1));

        // localApi.enabled is listed here and dropped by the scope. A null value counts as unset.
        Assert.Equal(
            ["settings:gestures.wheelVolume", "settings:language.choice", "settings:localApi.enabled", "settings:malaysia.state", SyncUnitNames.Rules],
            units.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("valorant.exe", units[SyncUnitNames.Rules][0].GetProperty("when")[0].GetProperty("apps")[0].GetString());
    }

    [Fact]
    public void FromSettings_WithoutRules_StillHasTheSentinel()
    {
        var units = SyncUnits.FromSettings(Json("""{ "schemaVersion": 1, "values": {} }"""));

        Assert.True(SameJson("[]", units[SyncUnitNames.Rules]));
    }

    [Fact]
    public void From_CombinesBothDocumentsAndSkipsOneThatWasNotRead()
    {
        var both = SyncUnits.From(Json(LayoutV3), Json(SettingsV1));
        var layoutOnly = SyncUnits.From(Json(LayoutV3), null);

        Assert.Equal(11, both.Count);
        Assert.DoesNotContain(SyncUnitNames.Rules, layoutOnly.Keys);
        Assert.Contains(SyncUnitNames.EnabledWidgets, layoutOnly.Keys);
    }

    [Fact]
    public void From_DocumentsThatAreNotObjects_GiveNoUnits()
    {
        Assert.Empty(SyncUnits.FromLayout(Json("[]")));
        Assert.Empty(SyncUnits.FromSettings(Json("\"x\"")));
    }
}
