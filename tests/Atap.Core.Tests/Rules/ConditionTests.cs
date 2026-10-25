using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class ConditionTests
{
    private const string Now = "2026-10-23 21:00";

    [Theory]
    [InlineData("valorant.exe", "VALORANT.exe")]
    [InlineData("VALORANT.EXE", "valorant.exe")]
    [InlineData("Valorant", "VALORANT.exe")]
    [InlineData("valorant.exe", "valorant")]
    [InlineData(@"C:\Riot Games\VALORANT\live\VALORANT.exe", "valorant.exe")]
    [InlineData("valorant.exe", @"C:\Riot Games\VALORANT\live\VALORANT.exe")]
    [InlineData(@"""C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE""", "powerpnt.exe")]
    [InlineData("  ms-teams.exe ", "ms-teams.exe")]
    [InlineData("C:/Tools/code.exe", "Code.exe")]
    public void ForegroundApp_SameExecutable_Matches(string configured, string process)
    {
        Assert.True(App(configured).Matches(Context(Now, app: process)));
    }

    [Theory]
    [InlineData("code.exe", "vscode.exe")]
    [InlineData("valorant.exe", "VALORANT-Win64-Shipping.exe")]
    [InlineData("teams", "ms-teams.exe")]
    [InlineData("steam.exe", "steam.exe.bak")]
    [InlineData("chrome.exe", @"C:\Program Files\Google\chrome.exe\other.exe")]
    public void ForegroundApp_OtherExecutable_DoesNotMatch(string configured, string process)
    {
        Assert.False(App(configured).Matches(Context(Now, app: process)));
    }

    [Fact]
    public void ForegroundApp_MatchesAnyListedApp()
    {
        var condition = App("spotify.exe", "VALORANT-Win64-Shipping.exe", "steam.exe");

        Assert.True(condition.Matches(Context(Now, app: "Steam.exe")));
        Assert.False(condition.Matches(Context(Now, app: "explorer.exe")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForegroundApp_NoForegroundProcess_DoesNotMatch(string? process)
    {
        Assert.False(App("valorant.exe").Matches(Context(Now, app: process)));
    }

    [Fact]
    public void ForegroundApp_WithoutUsableNames_IsInvalidAndNeverMatches()
    {
        var empty = App();
        var blank = App("", "  ", ".exe", @"C:\Games\");

        Assert.False(empty.IsValid);
        Assert.False(blank.IsValid);
        Assert.False(blank.Matches(Context(Now, app: ".exe")));
        Assert.True(App("", "steam.exe").IsValid);
    }

    [Fact]
    public void Wifi_ExactName_Matches()
    {
        Assert.True(Wifi("Rumah_unifi").Matches(Context(Now, ssid: "Rumah_unifi")));
        Assert.True(Wifi("eduroam", "Rumah_unifi").Matches(Context(Now, ssid: "eduroam")));
    }

    [Theory]
    [InlineData("rumah_unifi")]
    [InlineData("RUMAH_UNIFI")]
    [InlineData("Rumah_unifi ")]
    [InlineData(" Rumah_unifi")]
    [InlineData("Rumah_unifi_5G")]
    [InlineData("Rumah")]
    public void Wifi_AnyDifference_DoesNotMatch(string connected)
    {
        Assert.False(Wifi("Rumah_unifi").Matches(Context(Now, ssid: connected)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Wifi_NoNetworkOrNameHidden_DoesNotMatch(string? connected)
    {
        Assert.False(Wifi("Rumah_unifi").Matches(Context(Now, ssid: connected)));
        Assert.False(Wifi("").Matches(Context(Now, ssid: connected)));
    }

    [Fact]
    public void Wifi_WithoutNames_IsInvalid()
    {
        Assert.False(Wifi().IsValid);
        Assert.False(Wifi("").IsValid);
        Assert.True(Wifi("eduroam").IsValid);
    }

    [Theory]
    [InlineData(PowerConditionState.OnBattery, null, true, 80, false, true)]
    [InlineData(PowerConditionState.OnBattery, null, false, 80, false, false)]
    [InlineData(PowerConditionState.OnBattery, null, false, null, false, false)]
    [InlineData(PowerConditionState.PluggedIn, null, false, 80, false, true)]
    [InlineData(PowerConditionState.PluggedIn, null, false, null, false, true)]
    [InlineData(PowerConditionState.PluggedIn, null, true, 80, false, false)]
    [InlineData(PowerConditionState.EnergySaver, null, true, 15, true, true)]
    [InlineData(PowerConditionState.EnergySaver, null, false, 90, true, true)]
    [InlineData(PowerConditionState.EnergySaver, null, true, 15, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 20, true, 19, false, true)]
    [InlineData(PowerConditionState.BatteryBelow, 20, true, 20, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 20, true, 0, false, true)]
    [InlineData(PowerConditionState.BatteryBelow, 20, false, 12, false, true)]
    [InlineData(PowerConditionState.BatteryBelow, 20, false, null, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 20, true, null, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 100, true, 99, false, true)]
    [InlineData(PowerConditionState.BatteryBelow, null, true, 5, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 0, true, 0, false, false)]
    [InlineData(PowerConditionState.BatteryBelow, 101, true, 5, false, false)]
    public void Power_TruthTable(PowerConditionState state, int? below, bool onBattery, int? percent, bool energySaver, bool expected)
    {
        var condition = new PowerCondition { State = state, Percent = below };

        Assert.Equal(expected, condition.Matches(Context(Now, onBattery: onBattery, percent: percent, energySaver: energySaver)));
    }

    [Fact]
    public void Power_BatteryBelowNeedsALevelFromOneTo100()
    {
        Assert.True(new PowerCondition { State = PowerConditionState.BatteryBelow, Percent = 1 }.IsValid);
        Assert.True(new PowerCondition { State = PowerConditionState.BatteryBelow, Percent = 100 }.IsValid);
        Assert.False(new PowerCondition { State = PowerConditionState.BatteryBelow }.IsValid);
        Assert.False(new PowerCondition { State = PowerConditionState.BatteryBelow, Percent = 0 }.IsValid);
        Assert.False(new PowerCondition { State = PowerConditionState.BatteryBelow, Percent = 101 }.IsValid);
        Assert.True(new PowerCondition { State = PowerConditionState.OnBattery, Percent = 500 }.IsValid);
        Assert.False(new PowerCondition { State = (PowerConditionState)42 }.IsValid);
    }
}
