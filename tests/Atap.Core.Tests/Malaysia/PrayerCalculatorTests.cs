using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class PrayerCalculatorTests
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(2);
    private static readonly GeoPoint ShahAlam = new(3.073, 101.519);
    private static readonly GeoPoint KualaLumpur = new(3.139, 101.687);

    // Every recorded SGR01 response: October, November, the end of December and early August 2026.
    private static readonly string[] Sgr01Recorded =
    [
        EsolatFixtures.Sgr01October2026,
        EsolatFixtures.Sgr01November2026,
        EsolatFixtures.Sgr01YearEnd,
        EsolatFixtures.Sgr01AugustTrimmed,
    ];

    [Fact]
    public void Calculate_MatchesEveryRecordedESolatTimeForShahAlam()
    {
        var days = Sgr01Recorded.SelectMany(json => EsolatParser.Parse(json).Value!.Days).ToList();
        Assert.Equal(66, days.Count);

        foreach (var day in days)
        {
            var calculated = PrayerCalculator.Calculate(day.Date, ShahAlam, TimeSpan.FromHours(8));
            Assert.NotNull(calculated);
            foreach (var prayer in Enum.GetValues<Prayer>())
                AssertClose(day.TimeOf(prayer), calculated.TimeOf(prayer), $"{day.Date} {prayer}");
        }
    }

    [Fact]
    public void ImsakAndDhuhaOffsets_MatchEveryRecordedESolatDay()
    {
        var days = Sgr01Recorded.Append(EsolatFixtures.Wly01Today).SelectMany(json => EsolatParser.Parse(json).Value!.Days);

        Assert.All(days, d =>
        {
            Assert.Equal(PrayerCalculator.ImsakBeforeSubuh, d.Subuh - d.Imsak);
            Assert.Equal(PrayerCalculator.DhuhaAfterSyuruk, d.Dhuha - d.Syuruk);
        });
    }

    [Fact]
    public void Calculate_MatchesRecordedESolatTimesForKualaLumpur()
    {
        var official = EsolatParser.Parse(EsolatFixtures.Wly01Today).Value!.Days[0];

        var calculated = PrayerCalculator.Calculate(official.Date, KualaLumpur, TimeSpan.FromHours(8))!;

        foreach (var prayer in Enum.GetValues<Prayer>())
            AssertClose(official.TimeOf(prayer), calculated.TimeOf(prayer), prayer.ToString());
    }

    [Fact]
    public void Calculate_FollowsESolatConventions()
    {
        var day = PrayerCalculator.Calculate(new DateOnly(2026, 10, 25), KualaLumpur, TimeSpan.FromHours(8))!;

        Assert.Equal(TimeSpan.FromMinutes(10), day.Subuh - day.Imsak);
        Assert.Equal(TimeSpan.FromMinutes(25), day.Dhuha - day.Syuruk);
        Assert.Null(day.Hijri);
        Assert.True(day.IsInOrder);
        Assert.All(Enum.GetValues<Prayer>(), p => Assert.Equal(0, day.TimeOf(p).Second));
    }

    [Fact]
    public void Calculate_UsesTheGivenOffset()
    {
        var london = new GeoPoint(51.507, -0.128);

        var day = PrayerCalculator.Calculate(new DateOnly(2026, 12, 21), london, TimeSpan.Zero)!;

        Assert.Equal(TimeSpan.Zero, day.Zohor.Offset);
        // Solar noon in London in late December is close to 11:58.
        Assert.InRange(day.Zohor, new DateTimeOffset(2026, 12, 21, 11, 55, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 21, 12, 5, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Calculate_HighLatitudeSummer_FallsBackToAShareOfTheNight()
    {
        var london = new GeoPoint(51.507, -0.128);

        var day = PrayerCalculator.Calculate(new DateOnly(2026, 6, 21), london, TimeSpan.FromHours(1));

        Assert.NotNull(day);
        Assert.True(day.IsInOrder);
        Assert.True(day.Isyak.Date == day.Maghrib.Date || day.Isyak.Hour < 2, $"Isyak at {day.Isyak}");
    }

    [Fact]
    public void Calculate_WhereTheSunNeverSets_ReturnsNull()
    {
        var tromso = new GeoPoint(69.65, 18.96);

        Assert.Null(PrayerCalculator.Calculate(new DateOnly(2026, 6, 21), tromso, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Calculate_InvalidPoint_ReturnsNull()
    {
        Assert.Null(PrayerCalculator.Calculate(new DateOnly(2026, 10, 25), new GeoPoint(double.NaN, 101), TimeSpan.FromHours(8)));
        Assert.Null(PrayerCalculator.Calculate(new DateOnly(2026, 10, 25), new GeoPoint(95, 101), TimeSpan.FromHours(8)));
    }

    [Fact]
    public void Schedule_IsApproximateAndHasNoZone()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");

        var schedule = PrayerCalculator.Schedule(KualaLumpur, new DateOnly(2026, 10, 25), 7, timeZone);

        Assert.Null(schedule.Zone);
        Assert.Equal(PrayerSource.Calculated, schedule.Source);
        Assert.True(schedule.IsApproximate);
        Assert.Equal(7, schedule.Days.Count);
        Assert.Equal(Prayer.Subuh, schedule.Next(Myt.At(2026, 10, 25, 21, 0))?.Prayer);
    }

    [Fact]
    public void Schedule_LeavesOutDaysWithoutSunrise()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

        var schedule = PrayerCalculator.Schedule(new GeoPoint(69.65, 18.96), new DateOnly(2026, 6, 20), 3, timeZone);

        Assert.Empty(schedule.Days);
    }

    private static void AssertClose(DateTimeOffset expected, DateTimeOffset actual, string what)
    {
        var difference = (actual - expected).Duration();
        Assert.True(difference <= Tolerance, $"{what}: expected {expected:HH:mm}, calculated {actual:HH:mm}");
    }
}
