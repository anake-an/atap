namespace Atap.Core.Tests.Productivity;

/// <summary>Time zones built in code, so results never depend on the zone data of the test machine.</summary>
internal static class TestZones
{
    private static readonly TimeSpan MalaysiaOffset = TimeSpan.FromHours(8);

    /// <summary>UTC+8 with no daylight saving, as in Malaysia.</summary>
    public static readonly TimeZoneInfo MalaysiaZone =
        TimeZoneInfo.CreateCustomTimeZone("Test/Malaysia", MalaysiaOffset, "Malaysia (test)", "MYT");

    /// <summary>UTC+12 with no daylight saving, to test a zone already in tomorrow.</summary>
    public static readonly TimeZoneInfo PlusTwelve =
        TimeZoneInfo.CreateCustomTimeZone("Test/PlusTwelve", TimeSpan.FromHours(12), "Plus twelve (test)", "P12");

    /// <summary>
    /// UTC+0 with daylight saving. Clocks go from 01:00 to 02:00 on the last Sunday of March
    /// (2026-03-29) and from 02:00 back to 01:00 on the last Sunday of October (2026-10-25).
    /// </summary>
    public static readonly TimeZoneInfo Seasonal = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Seasonal",
        TimeSpan.Zero,
        "Seasonal (test)",
        "STD",
        "DST",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2000, 1, 1),
                new DateTime(2099, 12, 31),
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday)),
        ]);

    /// <summary>A Malaysian wall-clock time as an instant.</summary>
    public static DateTimeOffset My(int year, int month, int day, int hour, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, MalaysiaOffset);

    public static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
