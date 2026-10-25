using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class RuleResolverTests
{
    private const string FridayEvening = "2026-10-23 20:00";

    private static readonly RuleActions GamingProfile = new() { Profile = "gaming" };

    [Fact]
    public void NoRules_UsesTheDefaultProfile()
    {
        var outcome = RuleResolver.Resolve([], Context(FridayEvening), manualProfileId: null, Profiles);

        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
        Assert.False(outcome.Hide);
        Assert.False(outcome.Slim);
        Assert.False(outcome.QuietAlerts);
        Assert.Null(outcome.NextTimeBoundary);
        Assert.Empty(outcome.InputsUsed);
    }

    [Fact]
    public void NoRuleMatches_ManualPickWins()
    {
        Rule[] rules = [Rule("r1", GamingProfile, App("valorant.exe"))];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "explorer.exe"), "kerja", Profiles);

        Assert.Equal("kerja", outcome.ProfileId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("lama")]
    [InlineData("Kerja")]
    public void ManualPickMissingOrEmpty_FallsBackToDefault(string? manual)
    {
        var outcome = RuleResolver.Resolve([], Context(FridayEvening), manual, Profiles);

        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
    }

    [Fact]
    public void ManualPickOfDefault_IsKept()
    {
        var outcome = RuleResolver.Resolve([], Context(FridayEvening), RuleResolver.DefaultProfileId, new HashSet<string>());

        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
    }

    [Fact]
    public void MatchingRuleProfile_BeatsTheManualPick()
    {
        Rule[] rules = [Rule("r1", GamingProfile, App("valorant.exe"))];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "VALORANT.exe"), "kerja", Profiles);

        Assert.Equal("gaming", outcome.ProfileId);
    }

    [Fact]
    public void RuleNamingTheDefaultProfile_BeatsTheManualPick()
    {
        Rule[] rules = [Rule("r1", new RuleActions { Profile = RuleResolver.DefaultProfileId }, Wifi("Rumah_unifi"))];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, ssid: "Rumah_unifi"), "kerja", Profiles);

        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
    }

    [Fact]
    public void FirstMatchingRuleThatNamesAProfile_Wins()
    {
        Rule[] rules =
        [
            Rule("slim-only", new RuleActions { Slim = true }, App("steam.exe")),
            Rule("not-matching", new RuleActions { Profile = "kerja" }, App("outlook.exe")),
            Rule("gaming", GamingProfile, App("steam.exe")),
            Rule("kelas", new RuleActions { Profile = "kelas" }, App("steam.exe")),
        ];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "steam.exe"), null, Profiles);

        Assert.Equal("gaming", outcome.ProfileId);
        Assert.True(outcome.Slim);
    }

    [Fact]
    public void Flags_AreOredOverEveryMatchingRule()
    {
        Rule[] rules =
        [
            Rule("slim", new RuleActions { Slim = true }, App("steam.exe")),
            Rule("quiet", new RuleActions { QuietAlerts = true }, new PowerCondition { State = PowerConditionState.OnBattery }),
            Rule("hide", new RuleActions { Hide = true }, App("powerpnt.exe")),
            Rule("not-slim", new RuleActions { Slim = false, Profile = "kerja" }, App("steam.exe")),
        ];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "steam.exe", onBattery: true), null, Profiles);

        Assert.True(outcome.Slim);
        Assert.True(outcome.QuietAlerts);
        Assert.False(outcome.Hide);
        Assert.Equal("kerja", outcome.ProfileId);
    }

    [Fact]
    public void RuleNamingAMissingProfile_IsIgnoredWithItsFlags()
    {
        Rule[] rules =
        [
            Rule("deleted", new RuleActions { Profile = "dipadam", Hide = true }, App("valorant.exe")),
            Rule("gaming", GamingProfile, App("valorant.exe")),
        ];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "valorant.exe"), "kerja", Profiles);

        Assert.Equal("gaming", outcome.ProfileId);
        Assert.False(outcome.Hide);
    }

    [Fact]
    public void RuleNamingAMissingProfile_LeavesTheManualPick()
    {
        Rule[] rules = [Rule("deleted", new RuleActions { Profile = "dipadam", Slim = true }, App("valorant.exe"))];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "valorant.exe"), "kerja", Profiles);

        Assert.Equal("kerja", outcome.ProfileId);
        Assert.False(outcome.Slim);
        Assert.Empty(outcome.InputsUsed);
    }

    [Fact]
    public void DisabledRule_IsIgnored()
    {
        var rule = Rule("r1", new RuleActions { Profile = "gaming", Hide = true }, App("valorant.exe"));
        rule.Enabled = false;

        var outcome = RuleResolver.Resolve([rule], Context(FridayEvening, app: "valorant.exe"), null, Profiles);

        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
        Assert.False(outcome.Hide);
        Assert.Empty(outcome.InputsUsed);
    }

    [Fact]
    public void AllConditionsMustMatch()
    {
        Rule[] rules = [Rule("kelas", new RuleActions { Profile = "kelas" }, Window("08:00", "13:00", Weekdays), Wifi("eduroam"))];

        Assert.Equal("kelas", RuleResolver.Resolve(rules, Context("2026-10-23 09:00", ssid: "eduroam"), null, Profiles).ProfileId);
        Assert.Equal("default", RuleResolver.Resolve(rules, Context("2026-10-23 09:00", ssid: "Rumah_unifi"), null, Profiles).ProfileId);
        Assert.Equal("default", RuleResolver.Resolve(rules, Context("2026-10-24 09:00", ssid: "eduroam"), null, Profiles).ProfileId);
    }

    [Fact]
    public void RuleWithoutConditions_NeverApplies()
    {
        Rule[] rules = [Rule("always", new RuleActions { Hide = true, Profile = "gaming" })];

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening), null, Profiles);

        Assert.False(outcome.Hide);
        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
    }

    [Fact]
    public void RuleWithAnUnknownCondition_NeverApplies()
    {
        var rules = RuleJson.Parse("""
            [{ "id": "r1", "name": "Headphones", "enabled": true,
               "when": [{ "kind": "foregroundApp", "apps": ["spotify.exe"] }, { "kind": "bluetoothDevice", "name": "WH-1000XM4" }],
               "then": { "profile": "gaming", "slim": true } }]
            """)!;

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "spotify.exe"), null, Profiles);

        Assert.False(outcome.Slim);
        Assert.Equal(RuleResolver.DefaultProfileId, outcome.ProfileId);
        Assert.Empty(outcome.InputsUsed);
    }

    [Fact]
    public void NullEntriesFromAHandEditedFile_AreSkipped()
    {
        var rules = RuleJson.Parse("""
            [null, { "id": "r1", "when": [null, { "kind": "foregroundApp", "apps": ["steam.exe"] }], "then": { "slim": true } }]
            """)!;

        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening, app: "steam.exe"), null, Profiles);

        Assert.True(outcome.Slim);
    }

    [Fact]
    public void InputsUsed_ComeFromEveryUsableEnabledRule()
    {
        var disabled = Rule("disabled", new RuleActions { Slim = true }, Wifi("eduroam"));
        disabled.Enabled = false;
        Rule[] rules =
        [
            Rule("gaming", GamingProfile, App("valorant.exe")),
            Rule("night", new RuleActions { QuietAlerts = true }, Window("23:00", "07:00")),
            Rule("missing", new RuleActions { Profile = "dipadam" }, new PowerCondition { State = PowerConditionState.OnBattery }),
            disabled,
        ];

        // Nothing matches, yet the app and the time are still watched.
        var outcome = RuleResolver.Resolve(rules, Context(FridayEvening), null, Profiles);

        Assert.Equal(new[] { ConditionKind.ForegroundApp, ConditionKind.TimeWindow }, outcome.InputsUsed.Order());
    }

    [Fact]
    public void SettingsFixture_ResolvesLikeTheUserExpects()
    {
        var rules = ParseSettingsRules();

        var gaming = RuleResolver.Resolve(rules, Context("2026-10-23 21:00", app: "VALORANT-Win64-Shipping.exe", onBattery: true, percent: 64), "kerja", Profiles);
        Assert.Equal("gaming", gaming.ProfileId);
        Assert.True(gaming.Slim);
        Assert.True(gaming.QuietAlerts);
        Assert.False(gaming.Hide);

        var kelas = RuleResolver.Resolve(rules, Context("2026-10-23 10:00", app: "POWERPNT.EXE", ssid: "eduroam"), "kerja", Profiles);
        Assert.Equal("kelas", kelas.ProfileId);
        Assert.True(kelas.QuietAlerts);
        Assert.False(kelas.Hide);
        Assert.False(kelas.Slim);

        var lowBatteryAtNight = RuleResolver.Resolve(rules, Context("2026-10-24 01:00", onBattery: true, percent: 12), "kerja", Profiles);
        Assert.Equal("kerja", lowBatteryAtNight.ProfileId);
        Assert.True(lowBatteryAtNight.Slim);
        Assert.True(lowBatteryAtNight.QuietAlerts);

        Assert.Equal(
            new[] { ConditionKind.ForegroundApp, ConditionKind.TimeWindow, ConditionKind.Wifi, ConditionKind.Power },
            gaming.InputsUsed.Order());
    }

    [Fact]
    public void SameInputs_GiveEqualOutcomes()
    {
        var rules = ParseSettingsRules();
        var context = Context("2026-10-23 10:00", ssid: "eduroam");

        var first = RuleResolver.Resolve(rules, context, "kerja", Profiles);
        var second = RuleResolver.Resolve(ParseSettingsRules(), context, "kerja", Profiles);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, second with { Slim = !second.Slim });
        Assert.NotEqual(first, second with { InputsUsed = new HashSet<ConditionKind>() });
    }
}
