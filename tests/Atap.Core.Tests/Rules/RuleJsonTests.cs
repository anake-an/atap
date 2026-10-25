using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class RuleJsonTests
{
    [Fact]
    public void Parse_SettingsFixture_ReadsEveryKind()
    {
        var rules = ParseSettingsRules();

        Assert.Equal(new[] { "r1", "r2", "r3", "r4", "r5" }, rules.Select(r => r.Id));
        Assert.Equal("Bateri lemah", rules[3].Name);

        var gaming = Assert.IsType<ForegroundAppCondition>(Assert.Single(rules[0].When));
        Assert.Equal(new[] { "VALORANT.exe", "VALORANT-Win64-Shipping.exe", "steam.exe" }, gaming.Apps);
        Assert.Equal("gaming", rules[0].Then.Profile);
        Assert.True(rules[0].Then.Slim);
        Assert.True(rules[0].Then.QuietAlerts);
        Assert.False(rules[0].Then.Hide);

        var classes = Assert.IsType<TimeWindowCondition>(rules[1].When[0]);
        Assert.Equal(Weekdays, classes.Days);
        Assert.Equal(new TimeOnly(8, 0), classes.From);
        Assert.Equal(new TimeOnly(13, 0), classes.To);
        Assert.Equal(new[] { "eduroam" }, Assert.IsType<WifiCondition>(rules[1].When[1]).Ssids);

        var night = Assert.IsType<TimeWindowCondition>(Assert.Single(rules[2].When));
        Assert.Empty(night.Days);
        Assert.Null(rules[2].Then.Profile);

        var battery = Assert.IsType<PowerCondition>(Assert.Single(rules[3].When));
        Assert.Equal(PowerConditionState.BatteryBelow, battery.State);
        Assert.Equal(20, battery.Percent);

        Assert.False(rules[4].Enabled);
        Assert.True(rules[4].Then.Hide);
        Assert.All(rules, rule => Assert.Null(RuleChecker.Check(rule, Profiles)));
    }

    [Fact]
    public void SerializeThenParse_KeepsTheSettingsFixture()
    {
        var written = RuleJson.Serialize(ParseSettingsRules());

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SettingsRules), JsonNode.Parse(written)), written);
    }

    [Fact]
    public void Serialize_WritesKindFirstAndOnlyTheFlagsThatAreOn()
    {
        Rule[] rules =
        [
            Rule("r1", new RuleActions { Profile = "kelas", QuietAlerts = true }, Window("22:00", "07:00", DayOfWeek.Sunday, DayOfWeek.Friday)),
            Rule("r2", new RuleActions { Slim = true }, new PowerCondition { State = PowerConditionState.EnergySaver }, Wifi("Rumah_unifi")),
        ];

        var written = JsonNode.Parse(RuleJson.Serialize(rules))!.AsArray();

        var expected = JsonNode.Parse("""
            [
              { "id": "r1", "name": "r1", "enabled": true,
                "when": [{ "kind": "timeWindow", "days": ["sun", "fri"], "from": "22:00", "to": "07:00" }],
                "then": { "profile": "kelas", "quietAlerts": true } },
              { "id": "r2", "name": "r2", "enabled": true,
                "when": [{ "kind": "power", "state": "energySaver" }, { "kind": "wifi", "ssids": ["Rumah_unifi"] }],
                "then": { "slim": true } }
            ]
            """);
        Assert.True(JsonNode.DeepEquals(expected, written), written.ToJsonString());
        foreach (var condition in written.SelectMany(rule => rule!["when"]!.AsArray()))
            Assert.Equal("kind", condition!.AsObject().First().Key);
    }

    [Fact]
    public void Parse_KindMayComeAfterTheOtherFields()
    {
        var rules = RuleJson.Parse("""
            [{ "then": { "slim": true }, "when": [{ "apps": ["steam.exe"], "kind": "foregroundApp" }], "id": "r1" }]
            """)!;

        var condition = Assert.IsType<ForegroundAppCondition>(Assert.Single(Assert.Single(rules).When));
        Assert.Equal(new[] { "steam.exe" }, condition.Apps);
    }

    [Fact]
    public void Parse_ValuesIgnoreCaseAndFullDayNamesWork()
    {
        var rules = RuleJson.Parse("""
            [{ "id": "r1", "when": [
                 { "kind": "TimeWindow", "days": ["Monday", "WED", "fri"], "from": "8:00", "to": "24:00" },
                 { "kind": "POWER", "state": "PluggedIn" }],
               "then": { "slim": true } }]
            """)!;

        var window = Assert.IsType<TimeWindowCondition>(rules[0].When[0]);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, window.Days);
        Assert.Equal(new TimeOnly(8, 0), window.From);
        Assert.Equal(TimeOnly.MinValue, window.To);
        Assert.Equal(TimeSpan.FromHours(16), window.Length);
        Assert.Equal(PowerConditionState.PluggedIn, Assert.IsType<PowerCondition>(rules[0].When[1]).State);
    }

    [Fact]
    public void Parse_MissingEnabled_MeansEnabled()
    {
        var rules = RuleJson.Parse("""[{ "id": "r1", "when": [], "then": {} }]""")!;

        Assert.True(Assert.Single(rules).Enabled);
    }

    [Fact]
    public void UnknownKind_IsKeptAndWrittenBackUnchanged()
    {
        const string json = """
            [{ "id": "r1", "name": "Headphones", "enabled": true,
               "when": [{ "kind": "bluetoothDevice", "address": "F8:4E:17:2A:9C:01", "battery": { "below": 15 } }],
               "then": { "quietAlerts": true } }]
            """;

        var rules = RuleJson.Parse(json)!;
        var unsupported = Assert.IsType<UnsupportedCondition>(Assert.Single(rules[0].When));

        Assert.Equal("bluetoothDevice", unsupported.KindName);
        Assert.False(unsupported.IsKnownKind);
        Assert.False(unsupported.Matches(Context("2026-10-23 10:00")));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(RuleJson.Serialize(rules))));
    }

    [Fact]
    public void UnknownFields_RoundTripOnRulesActionsAndConditions()
    {
        const string json = """
            [{ "id": "r1", "name": "Gaming", "enabled": true, "colour": "#A78BFA",
               "when": [{ "kind": "foregroundApp", "apps": ["valorant.exe"], "matchTitle": "VALORANT" }],
               "then": { "profile": "gaming", "volume": 30 } }]
            """;

        var rules = RuleJson.Parse(json)!;

        Assert.True(rules[0].ExtensionData!.ContainsKey("colour"));
        Assert.True(rules[0].Then.ExtensionData!.ContainsKey("volume"));
        Assert.True(rules[0].When[0].ExtensionData!.ContainsKey("matchTitle"));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(RuleJson.Serialize(rules))));
    }

    [Theory]
    [InlineData("""{ "kind": "timeWindow", "from": "25:00", "to": "07:00" }""")]
    [InlineData("""{ "kind": "timeWindow", "from": "8 pagi", "to": "07:00" }""")]
    [InlineData("""{ "kind": "timeWindow", "to": "07:00" }""")]
    [InlineData("""{ "kind": "timeWindow", "from": 800, "to": "07:00" }""")]
    [InlineData("""{ "kind": "timeWindow", "from": "08:00", "to": "07:00", "days": ["isnin"] }""")]
    [InlineData("""{ "kind": "timeWindow", "from": "08:00", "to": "07:00", "days": "mon" }""")]
    [InlineData("""{ "kind": "timeWindow", "from": "08:00", "to": "07:00", "days": [1] }""")]
    [InlineData("""{ "kind": "foregroundApp", "apps": "valorant.exe" }""")]
    [InlineData("""{ "kind": "foregroundApp", "apps": ["valorant.exe", 7] }""")]
    [InlineData("""{ "kind": "wifi", "ssids": [null] }""")]
    [InlineData("""{ "kind": "power", "state": "charging" }""")]
    [InlineData("""{ "kind": "power" }""")]
    [InlineData("""{ "kind": "power", "state": "batteryBelow", "percent": "20" }""")]
    [InlineData("""{ "kind": "power", "state": "batteryBelow", "percent": 20.5 }""")]
    public void InvalidValues_BecomeAnUnsupportedConditionThatRoundTrips(string condition)
    {
        var json = $$"""[{ "id": "r1", "name": "", "enabled": true, "when": [{{condition}}], "then": { "slim": true } }]""";

        var rules = RuleJson.Parse(json)!;
        var unsupported = Assert.IsType<UnsupportedCondition>(Assert.Single(rules[0].When));

        Assert.True(unsupported.IsKnownKind);
        Assert.Equal(RuleIssue.InvalidCondition, RuleChecker.Check(rules[0], Profiles));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(RuleJson.Serialize(rules))));
    }

    [Theory]
    [InlineData("""{ "kind": 3 }""")]
    [InlineData("""{ }""")]
    [InlineData(""" "foregroundApp" """)]
    [InlineData("""[1, 2]""")]
    [InlineData("""42""")]
    public void ConditionsThatAreNotReadable_AreKeptAsUnknown(string condition)
    {
        var json = $$"""[{ "id": "r1", "name": "", "enabled": true, "when": [{{condition}}], "then": { "slim": true } }]""";

        var rules = RuleJson.Parse(json)!;

        Assert.IsType<UnsupportedCondition>(Assert.Single(rules[0].When));
        Assert.Equal(RuleIssue.UnknownCondition, RuleChecker.Check(rules[0], Profiles));
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(RuleJson.Serialize(rules))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("\"rules\"")]
    [InlineData("[")]
    [InlineData("[{\"id\": \"r1\",")]
    [InlineData("[1, 2, 3]")]
    [InlineData("[\"r1\"]")]
    [InlineData("[{\"when\": 5}]")]
    [InlineData("[{\"when\": {\"kind\": \"wifi\"}}]")]
    [InlineData("[{\"then\": []}]")]
    [InlineData("[{\"enabled\": \"yes\"}]")]
    [InlineData("[{\"id\": 7}]")]
    [InlineData("[{\"then\": {\"slim\": \"true\"}}]")]
    [InlineData("[{\"id\": \"r1\"} // comment\n]")]
    [InlineData("[{\"id\": \"r1\",}]")]
    [InlineData("[{\"id\": \"\\ud800\"}]")]
    [InlineData("\u0000")]
    public void Parse_MalformedInput_ReturnsNullWithoutThrowing(string? json)
    {
        Assert.Null(RuleJson.Parse(json));
    }

    [Fact]
    public void Parse_TextThatIsNotValidUtf16_ReturnsNull()
    {
        // Built at run time: an attribute argument cannot hold a lone surrogate.
        var loneSurrogate = "[{\"id\": \"" + (char)0xD800 + "\"}]";

        Assert.Null(RuleJson.Parse(loneSurrogate));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[{\"when\": null, \"then\": null}]")]
    [InlineData("[{\"when\": [null, {}, [], 1, \"x\", {\"kind\": null}]}]")]
    [InlineData("[{\"id\": \"r1\", \"id\": \"r2\", \"when\": [{\"kind\": \"wifi\", \"kind\": \"power\", \"state\": \"onBattery\"}]}]")]
    [InlineData("[{\"when\": [{\"kind\": \"timeWindow\", \"from\": \"23:59\", \"to\": \"00:00\", \"days\": null}]}]")]
    public void Parse_OddButValidInput_NeverBreaksTheResolver(string json)
    {
        var rules = RuleJson.Parse(json);

        Assert.NotNull(rules);
        Assert.All(rules, rule => Assert.NotNull(rule));
        var outcome = RuleResolver.Resolve(rules, Context("2026-10-23 23:59", onBattery: true), "kerja", Profiles);
        Assert.Equal("kerja", outcome.ProfileId);
        RuleJson.Serialize(rules);
    }

    [Fact]
    public void Parse_NestedDeeperThan32Levels_ReturnsNull()
    {
        var deep = new string('[', 40) + new string(']', 40);
        var json = $$"""[{ "id": "r1", "when": [{ "kind": "wifi", "ssids": ["a"], "extra": {{deep}} }] }]""";

        Assert.Null(RuleJson.Parse(json));
    }

    [Fact]
    public void ReflectionSerializer_UsesTheSameFormat()
    {
        var rules = JsonSerializer.Deserialize<List<Rule>>(SettingsRules)!;

        Assert.IsType<PowerCondition>(rules[3].When[0]);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SettingsRules), JsonNode.Parse(JsonSerializer.Serialize(rules))));
    }

    [Fact]
    public void SourceGeneratedSettingsDocument_RoundTripsRules()
    {
        // The settings.json example from the architecture plan, read the way a settings store would.
        const string settings = """
            {
              "schemaVersion": 1,
              "values": { "language.choice": "system", "gestures.wheelVolume": true, "localApi.enabled": false, "malaysia.state": "SGR" },
              "rules": [{ "id": "r1", "name": "Gaming", "enabled": true,
                          "when": [{ "kind": "foregroundApp", "apps": ["valorant.exe"] }],
                          "then": { "profile": "gaming", "slim": true, "quietAlerts": true } }]
            }
            """;

        var document = JsonSerializer.Deserialize(settings, SettingsFixtureContext.Default.SettingsFixture)!;

        var rule = Assert.Single(document.Rules);
        Assert.Equal(new[] { "valorant.exe" }, Assert.IsType<ForegroundAppCondition>(Assert.Single(rule.When)).Apps);
        var written = JsonSerializer.Serialize(document, SettingsFixtureContext.Default.SettingsFixture);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(settings), JsonNode.Parse(written)), written);
    }
}

/// <summary>Stands in for the settings document of the settings store.</summary>
public sealed class SettingsFixture
{
    public int SchemaVersion { get; set; }

    public Dictionary<string, JsonElement> Values { get; set; } = [];

    public List<Rule> Rules { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SettingsFixture))]
internal sealed partial class SettingsFixtureContext : JsonSerializerContext
{
}
