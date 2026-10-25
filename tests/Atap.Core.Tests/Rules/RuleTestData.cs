using System.Globalization;
using Atap.Core.Rules;

namespace Atap.Core.Tests.Rules;

/// <summary>Shared times, zones and rules. October 2026: Friday 23, Saturday 24, Sunday 25, Monday 26.</summary>
internal static class RuleTestData
{
    public static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);

    public static readonly TimeZoneInfo MalaysiaZone =
        TimeZoneInfo.CreateCustomTimeZone("Atap Test Malaysia", MalaysiaOffset, "Malaysia Time", "Malaysia Time");

    /// <summary>
    /// UTC+0 with daylight saving from 29 March 02:00 (clocks jump to 03:00) to 25 October 02:00
    /// daylight time (clocks go back to 01:00).
    /// </summary>
    public static readonly TimeZoneInfo Daylight = TimeZoneInfo.CreateCustomTimeZone(
        "Atap Test Daylight",
        TimeSpan.Zero,
        "Test Time",
        "Test Standard Time",
        "Test Daylight Time",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 29),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 25)),
        ]);

    public static readonly IReadOnlySet<string> Profiles = new HashSet<string>(StringComparer.Ordinal) { "gaming", "kelas", "kerja" };

    /// <summary>A Malaysia wall-clock time such as "2026-10-23 22:30".</summary>
    public static DateTimeOffset Myt(string local) =>
        new(DateTime.ParseExact(local, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), MalaysiaOffset);

    public static RuleContext Context(
        string local,
        string? app = null,
        string? ssid = null,
        bool onBattery = false,
        int? percent = null,
        bool energySaver = false) => new()
        {
            Now = Myt(local),
            TimeZone = MalaysiaZone,
            ForegroundProcessName = app,
            WifiSsid = ssid,
            OnBattery = onBattery,
            BatteryPercent = percent,
            EnergySaverOn = energySaver,
        };

    public static Rule Rule(string id, RuleActions then, params RuleCondition[] when) =>
        new() { Id = id, Name = id, When = [.. when], Then = then };

    public static ForegroundAppCondition App(params string[] apps) => new() { Apps = [.. apps] };

    public static WifiCondition Wifi(params string[] ssids) => new() { Ssids = [.. ssids] };

    public static TimeWindowCondition Window(string from, string to, params DayOfWeek[] days) => new()
    {
        From = TimeOnly.ParseExact(from, "HH:mm", CultureInfo.InvariantCulture),
        To = TimeOnly.ParseExact(to, "HH:mm", CultureInfo.InvariantCulture),
        Days = [.. days],
    };

    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    /// <summary>
    /// A realistic <c>rules</c> array from settings.json: a gaming rule on the VALORANT launcher and game
    /// process, a weekday class rule on the campus network, a night rule across midnight, a low battery
    /// rule and a disabled presentation rule.
    /// </summary>
    public const string SettingsRules = """
        [
          {
            "id": "r1",
            "name": "Gaming",
            "enabled": true,
            "when": [{ "kind": "foregroundApp", "apps": ["VALORANT.exe", "VALORANT-Win64-Shipping.exe", "steam.exe"] }],
            "then": { "profile": "gaming", "slim": true, "quietAlerts": true }
          },
          {
            "id": "r2",
            "name": "Kelas",
            "enabled": true,
            "when": [
              { "kind": "timeWindow", "days": ["mon", "tue", "wed", "thu", "fri"], "from": "08:00", "to": "13:00" },
              { "kind": "wifi", "ssids": ["eduroam"] }
            ],
            "then": { "profile": "kelas", "quietAlerts": true }
          },
          {
            "id": "r3",
            "name": "Malam",
            "enabled": true,
            "when": [{ "kind": "timeWindow", "from": "23:00", "to": "07:00" }],
            "then": { "slim": true, "quietAlerts": true }
          },
          {
            "id": "r4",
            "name": "Bateri lemah",
            "enabled": true,
            "when": [{ "kind": "power", "state": "batteryBelow", "percent": 20 }],
            "then": { "slim": true }
          },
          {
            "id": "r5",
            "name": "Pembentangan",
            "enabled": false,
            "when": [{ "kind": "foregroundApp", "apps": ["POWERPNT.EXE"] }],
            "then": { "hide": true }
          }
        ]
        """;

    public static List<Rule> ParseSettingsRules() =>
        RuleJson.Parse(SettingsRules) ?? throw new InvalidOperationException("The rules fixture did not parse.");
}
