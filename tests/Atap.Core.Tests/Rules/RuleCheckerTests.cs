using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class RuleCheckerTests
{
    [Fact]
    public void UsableRule_HasNoIssue()
    {
        var rule = Rule("r1", new RuleActions { Profile = "gaming", Slim = true }, App("valorant.exe"));

        Assert.Null(RuleChecker.Check(rule, Profiles));
    }

    [Fact]
    public void DisabledRule_IsStillChecked()
    {
        var rule = Rule("r1", new RuleActions { Profile = "dipadam" }, App("valorant.exe"));
        rule.Enabled = false;

        Assert.Equal(RuleIssue.MissingProfile, RuleChecker.Check(rule, Profiles));
    }

    [Fact]
    public void NoConditions_IsFlagged()
    {
        Assert.Equal(RuleIssue.NoConditions, RuleChecker.Check(Rule("r1", new RuleActions { Slim = true }), Profiles));
        Assert.Equal(RuleIssue.NoConditions, RuleChecker.Check(new Rule { When = null!, Then = new RuleActions { Slim = true } }, Profiles));
    }

    [Fact]
    public void IncompleteCondition_IsFlagged()
    {
        Assert.Equal(RuleIssue.InvalidCondition, RuleChecker.Check(Rule("r1", new RuleActions { Slim = true }, App()), Profiles));
        Assert.Equal(RuleIssue.InvalidCondition, RuleChecker.Check(Rule("r2", new RuleActions { Slim = true }, Wifi("")), Profiles));
        Assert.Equal(
            RuleIssue.InvalidCondition,
            RuleChecker.Check(Rule("r3", new RuleActions { Slim = true }, new PowerCondition { State = PowerConditionState.BatteryBelow }), Profiles));
    }

    [Fact]
    public void ConditionsFromTheFile_AreFlaggedByCause()
    {
        var rules = RuleJson.Parse("""
            [
              { "id": "newer", "when": [{ "kind": "bluetoothDevice", "name": "WH-1000XM4" }], "then": { "slim": true } },
              { "id": "bad-time", "when": [{ "kind": "timeWindow", "from": "8 pagi", "to": "13:00" }], "then": { "slim": true } },
              { "id": "no-kind", "when": [{ "apps": ["steam.exe"] }], "then": { "slim": true } }
            ]
            """)!;

        Assert.Equal(RuleIssue.UnknownCondition, RuleChecker.Check(rules[0], Profiles));
        Assert.Equal(RuleIssue.InvalidCondition, RuleChecker.Check(rules[1], Profiles));
        Assert.Equal(RuleIssue.UnknownCondition, RuleChecker.Check(rules[2], Profiles));
    }

    [Fact]
    public void NoActions_IsFlagged()
    {
        Assert.Equal(RuleIssue.NoActions, RuleChecker.Check(Rule("r1", new RuleActions(), App("steam.exe")), Profiles));
        Assert.Equal(RuleIssue.NoActions, RuleChecker.Check(Rule("r2", new RuleActions { Profile = "  " }, App("steam.exe")), Profiles));
        Assert.Equal(RuleIssue.NoActions, RuleChecker.Check(Rule("r3", null!, App("steam.exe")), Profiles));
    }

    [Fact]
    public void MissingProfile_IsFlagged()
    {
        Assert.Equal(RuleIssue.MissingProfile, RuleChecker.Check(Rule("r1", new RuleActions { Profile = "dipadam" }, App("steam.exe")), Profiles));
        Assert.Equal(RuleIssue.MissingProfile, RuleChecker.Check(Rule("r2", new RuleActions { Profile = "Gaming" }, App("steam.exe")), Profiles));
    }

    [Fact]
    public void DefaultProfile_AlwaysExists()
    {
        var rule = Rule("r1", new RuleActions { Profile = RuleResolver.DefaultProfileId }, App("steam.exe"));

        Assert.Null(RuleChecker.Check(rule, new HashSet<string>()));
    }
}
