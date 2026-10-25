using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class SolatClockTests
{
    private static readonly PrayerSchedule October = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!;
    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static SolatNow At(DateTimeOffset now, RamadanMode ramadan = RamadanMode.Auto, PrayerSchedule? schedule = null) =>
        SolatClock.At(schedule ?? October, now, TenMinutes, ramadan, 0);

    [Fact]
    public void At_BeforeTheLiveWindow_ShowsTheNextPrayerQuietly()
    {
        var now = At(Myt.At(2026, 10, 25, 16, 0));

        Assert.Equal(Prayer.Asar, now.Next?.Prayer);
        Assert.Equal(Prayer.Zohor, now.Current?.Prayer);
        Assert.False(now.IsLive);
        Assert.False(now.IsStale);
    }

    [Fact]
    public void At_InsideTheLiveWindow_IsLive()
    {
        Assert.True(At(Myt.At(2026, 10, 25, 16, 9)).IsLive);
        Assert.True(At(Myt.At(2026, 10, 25, 16, 18, 59)).IsLive);
        Assert.False(At(Myt.At(2026, 10, 25, 16, 8, 59)).IsLive);
    }

    [Fact]
    public void At_JustAfterAPrayerBegins_StaysLiveForFiveMinutes()
    {
        var started = At(Myt.At(2026, 10, 25, 16, 19));

        Assert.True(started.IsLive);
        Assert.Equal(Prayer.Asar, started.Current?.Prayer);
        Assert.Equal(Prayer.Maghrib, started.Next?.Prayer);
        Assert.True(At(Myt.At(2026, 10, 25, 16, 23, 59)).IsLive);
        Assert.False(At(Myt.At(2026, 10, 25, 16, 24)).IsLive);
    }

    [Fact]
    public void At_WithNoSchedule_IsStaleAndQuiet()
    {
        var now = SolatClock.At(null, Myt.At(2026, 10, 25, 16, 15), TenMinutes, RamadanMode.On, 0);

        Assert.True(now.IsStale);
        Assert.False(now.IsLive);
        Assert.Null(now.Next);
        Assert.Null(now.Fast);
    }

    [Fact]
    public void At_PastTheLastDay_IsStaleAndQuiet()
    {
        var now = At(Myt.At(2026, 11, 2, 16, 15));

        Assert.True(now.IsStale);
        Assert.False(now.IsLive);
        Assert.Null(now.Next);
    }

    [Fact]
    public void At_OutsideRamadan_HasNoFastCountdown()
    {
        var now = At(Myt.At(2026, 10, 25, 12, 0));

        Assert.False(now.IsRamadan);
        Assert.Null(now.Fast);
    }

    [Fact]
    public void At_RamadanOn_CountsToImsakThenBerbukaThenTomorrowsImsak()
    {
        Assert.Equal(
            new PrayerTime(Prayer.Imsak, Myt.At(2026, 10, 25, 5, 39), new DateOnly(2026, 10, 25)),
            At(Myt.At(2026, 10, 25, 4, 0), RamadanMode.On).Fast);
        Assert.Equal(
            new PrayerTime(Prayer.Maghrib, Myt.At(2026, 10, 25, 18, 59), new DateOnly(2026, 10, 25)),
            At(Myt.At(2026, 10, 25, 12, 0), RamadanMode.On).Fast);
        Assert.Equal(
            new PrayerTime(Prayer.Imsak, Myt.At(2026, 10, 26, 5, 38), new DateOnly(2026, 10, 26)),
            At(Myt.At(2026, 10, 25, 20, 0), RamadanMode.On).Fast);
    }

    [Fact]
    public void At_RamadanAuto_FollowsTheHijriMonth()
    {
        var day = October.DayOf(new DateOnly(2026, 10, 25))!;
        var ramadan = new PrayerSchedule("SGR01", PrayerSource.Esolat, [day with { Hijri = new HijriDate(1448, 9, 1) }]);

        var now = At(Myt.At(2026, 10, 25, 12, 0), RamadanMode.Auto, ramadan);

        Assert.True(now.IsRamadan);
        Assert.Equal(Prayer.Maghrib, now.Fast?.Prayer);
        Assert.False(At(Myt.At(2026, 10, 25, 12, 0), RamadanMode.Off, ramadan).IsRamadan);
    }

    [Fact]
    public void At_RamadanAuto_AfterMaghrib_FollowsTomorrowsHijriDay()
    {
        // 25 October is the last day of Sha'ban and 26 October is 1 Ramadan.
        var eve = October.DayOf(new DateOnly(2026, 10, 25))! with { Hijri = new HijriDate(1448, 8, 30) };
        var first = October.DayOf(new DateOnly(2026, 10, 26))! with { Hijri = new HijriDate(1448, 9, 1) };
        var schedule = new PrayerSchedule("SGR01", PrayerSource.Esolat, [eve, first]);

        Assert.False(At(Myt.At(2026, 10, 25, 18, 58), RamadanMode.Auto, schedule).IsRamadan);
        var evening = At(Myt.At(2026, 10, 25, 20, 0), RamadanMode.Auto, schedule);
        Assert.True(evening.IsRamadan);
        Assert.Equal(new PrayerTime(Prayer.Imsak, Myt.At(2026, 10, 26, 5, 38), new DateOnly(2026, 10, 26)), evening.Fast);
    }

    [Fact]
    public void At_RamadanAuto_LastEveningOfRamadan_HasNoImsakCountdown()
    {
        // 25 October is 30 Ramadan and 26 October is 1 Syawal.
        var last = October.DayOf(new DateOnly(2026, 10, 25))! with { Hijri = new HijriDate(1448, 9, 30) };
        var eid = October.DayOf(new DateOnly(2026, 10, 26))! with { Hijri = new HijriDate(1448, 10, 1) };
        var schedule = new PrayerSchedule("SGR01", PrayerSource.Esolat, [last, eid]);

        Assert.Equal(Prayer.Maghrib, At(Myt.At(2026, 10, 25, 12, 0), RamadanMode.Auto, schedule).Fast?.Prayer);
        var evening = At(Myt.At(2026, 10, 25, 20, 0), RamadanMode.Auto, schedule);
        Assert.False(evening.IsRamadan);
        Assert.Null(evening.Fast);
    }

    [Fact]
    public void NextChange_BeforeTheWindow_IsWhenTheWindowOpens()
    {
        Assert.Equal(Myt.At(2026, 10, 25, 16, 9), SolatClock.NextChange(October, Myt.At(2026, 10, 25, 16, 0), TenMinutes));
    }

    [Fact]
    public void NextChange_InsideTheWindow_IsThePrayer()
    {
        Assert.Equal(Myt.At(2026, 10, 25, 16, 19), SolatClock.NextChange(October, Myt.At(2026, 10, 25, 16, 10), TenMinutes));
    }

    [Fact]
    public void NextChange_AtThePrayer_IsWhenItStopsBeingLive()
    {
        Assert.Equal(Myt.At(2026, 10, 25, 16, 24), SolatClock.NextChange(October, Myt.At(2026, 10, 25, 16, 19), TenMinutes));
    }

    [Fact]
    public void NextChange_AfterIsyak_IsMidnight()
    {
        Assert.Equal(Myt.At(2026, 10, 26, 0, 0), SolatClock.NextChange(October, Myt.At(2026, 10, 25, 21, 0), TenMinutes));
    }

    [Fact]
    public void NextChange_BeforeImsak_IsImsak()
    {
        Assert.Equal(Myt.At(2026, 10, 25, 5, 39), SolatClock.NextChange(October, Myt.At(2026, 10, 25, 5, 0), TenMinutes));
    }

    [Fact]
    public void NextChange_WithNoSchedule_IsMidnight()
    {
        Assert.Equal(Myt.At(2026, 10, 26, 0, 0), SolatClock.NextChange(null, Myt.At(2026, 10, 25, 9, 0), TenMinutes));
    }
}
