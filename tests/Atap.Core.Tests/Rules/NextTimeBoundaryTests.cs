using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class NextTimeBoundaryTests
{
    private static readonly RuleActions Quiet = new() { QuietAlerts = true };

    private static DateTimeOffset? BoundaryOf(IReadOnlyList<Rule> rules, RuleContext context) =>
        RuleResolver.Resolve(rules, context, manualProfileId: null, Profiles).NextTimeBoundary;

    [Fact]
    public void NoTimeWindows_HasNoBoundary()
    {
        Rule[] rules =
        [
            Rule("gaming", new RuleActions { Profile = "gaming" }, App("valorant.exe")),
            Rule("battery", Quiet, new PowerCondition { State = PowerConditionState.OnBattery }),
        ];

        Assert.Null(BoundaryOf(rules, Context("2026-10-23 10:00", app: "valorant.exe", onBattery: true)));
    }

    [Fact]
    public void ClosedWindow_BoundaryIsItsNextStart()
    {
        Rule[] rules = [Rule("night", Quiet, Window("23:00", "07:00"))];

        Assert.Equal(Myt("2026-10-23 23:00"), BoundaryOf(rules, Context("2026-10-23 10:00")));
    }

    [Fact]
    public void OpenOvernightWindow_BoundaryIsTheNextMorning()
    {
        Rule[] rules = [Rule("night", Quiet, Window("23:00", "07:00"))];

        Assert.Equal(Myt("2026-10-24 07:00"), BoundaryOf(rules, Context("2026-10-23 23:15")));
    }

    [Fact]
    public void FridayAfterClass_BoundarySkipsTheWeekend()
    {
        Rule[] rules = [Rule("kelas", new RuleActions { Profile = "kelas" }, Window("08:00", "13:00", Weekdays))];

        Assert.Equal(Myt("2026-10-26 08:00"), BoundaryOf(rules, Context("2026-10-23 13:00")));
    }

    [Fact]
    public void SeveralRules_GiveTheEarliestBoundary()
    {
        Rule[] rules =
        [
            Rule("night", Quiet, Window("23:00", "07:00")),
            Rule("lunch", new RuleActions { Slim = true }, Window("12:30", "14:00", Weekdays)),
            Rule("kelas", new RuleActions { Profile = "kelas" }, Window("08:00", "13:00", Weekdays)),
        ];

        Assert.Equal(Myt("2026-10-23 12:30"), BoundaryOf(rules, Context("2026-10-23 10:00")));
        Assert.Equal(Myt("2026-10-23 13:00"), BoundaryOf(rules, Context("2026-10-23 12:45")));
    }

    [Fact]
    public void DisabledRule_GivesNoBoundary()
    {
        var night = Rule("night", Quiet, Window("23:00", "07:00"));
        night.Enabled = false;

        Assert.Null(BoundaryOf([night], Context("2026-10-23 10:00")));
    }

    [Fact]
    public void RuleHeldBackByAnotherInput_GivesNoBoundary()
    {
        // Gaming late at night only counts while the game is in front. Without it, the 22:00 start
        // changes nothing, and opening the game resolves again anyway.
        Rule[] rules = [Rule("late-gaming", new RuleActions { Profile = "gaming" }, Window("22:00", "02:00"), App("valorant.exe"))];

        Assert.Null(BoundaryOf(rules, Context("2026-10-23 20:00", app: "explorer.exe")));
        Assert.Equal(Myt("2026-10-23 22:00"), BoundaryOf(rules, Context("2026-10-23 20:00", app: "valorant.exe")));
        Assert.Equal(Myt("2026-10-24 02:00"), BoundaryOf(rules, Context("2026-10-23 23:00", app: "valorant.exe")));
    }

    [Fact]
    public void ClassRuleOffCampus_GivesNoBoundary()
    {
        var rules = ParseSettingsRules();

        // Off campus the class rule is held back by Wi-Fi, so only the night rule at 23:00 is left.
        Assert.Equal(Myt("2026-10-23 23:00"), BoundaryOf(rules, Context("2026-10-23 10:00", ssid: "Rumah_unifi")));
        Assert.Equal(Myt("2026-10-23 13:00"), BoundaryOf(rules, Context("2026-10-23 10:00", ssid: "eduroam")));
    }

    [Fact]
    public void TwoWindowsInOneRule_WhenItApplies_BoundaryIsTheFirstToClose()
    {
        Rule[] rules = [Rule("office-lunch", Quiet, Window("09:00", "17:00", Weekdays), Window("12:00", "14:00"))];

        Assert.Equal(Myt("2026-10-23 14:00"), BoundaryOf(rules, Context("2026-10-23 12:30")));
    }

    [Fact]
    public void TwoWindowsInOneRule_WhenOneIsClosed_BoundaryIsWhenItOpens()
    {
        Rule[] rules = [Rule("office-lunch", Quiet, Window("09:00", "17:00", Weekdays), Window("12:00", "14:00"))];

        // The office window closing at 17:00 cannot make the rule apply, so it is not a boundary.
        Assert.Equal(Myt("2026-10-23 12:00"), BoundaryOf(rules, Context("2026-10-23 10:00")));
    }

    [Fact]
    public void Boundary_IsAlwaysAfterNow_SoTheSchedulerCannotSpin()
    {
        Rule[] rules = [Rule("night", Quiet, Window("23:00", "07:00")), Rule("kelas", Quiet, Window("08:00", "13:00", Weekdays))];
        var now = Myt("2026-10-23 08:00");

        for (var step = 0; step < 200; step++)
        {
            var context = new RuleContext { Now = now, TimeZone = MalaysiaZone };
            var boundary = BoundaryOf(rules, context);

            // Fails for null as well.
            Assert.True(boundary > now, $"Boundary {boundary} is not after {now}.");
            now = boundary.GetValueOrDefault();
        }
    }
}
