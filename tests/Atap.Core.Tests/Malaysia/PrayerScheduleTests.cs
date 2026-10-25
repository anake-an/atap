using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class PrayerScheduleTests
{
    private static readonly PrayerSchedule October = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!;
    private static readonly PrayerSchedule November = EsolatParser.Parse(EsolatFixtures.Sgr01November2026).Value!;

    [Fact]
    public void Next_DuringTheDay_IsTheNextObligatoryPrayer()
    {
        var next = October.Next(Myt.At(2026, 10, 25, 16, 0));

        Assert.Equal(new PrayerTime(Prayer.Asar, Myt.At(2026, 10, 25, 16, 19), new DateOnly(2026, 10, 25)), next);
    }

    [Fact]
    public void Next_AtTheExactMinute_MovesToTheFollowingPrayer()
    {
        var asar = Myt.At(2026, 10, 25, 16, 19);

        Assert.Equal(Prayer.Maghrib, October.Next(asar)?.Prayer);
        Assert.Equal(Myt.At(2026, 10, 25, 18, 59), October.Next(asar)?.Time);
        Assert.Equal(Prayer.Asar, October.Current(asar)?.Prayer);
    }

    [Fact]
    public void Next_OneSecondBefore_IsThatPrayer()
    {
        var justBefore = Myt.At(2026, 10, 25, 16, 18, 59);

        Assert.Equal(Prayer.Asar, October.Next(justBefore)?.Prayer);
        Assert.Equal(Prayer.Zohor, October.Current(justBefore)?.Prayer);
    }

    [Fact]
    public void Next_AfterIsyak_IsTomorrowsSubuh()
    {
        var next = October.Next(Myt.At(2026, 10, 25, 21, 30));

        Assert.Equal(new PrayerTime(Prayer.Subuh, Myt.At(2026, 10, 26, 5, 48), new DateOnly(2026, 10, 26)), next);
    }

    [Fact]
    public void Next_AtIsyak_IsTomorrowsSubuh()
    {
        var isyak = Myt.At(2026, 10, 25, 20, 10);

        Assert.Equal(Prayer.Subuh, October.Next(isyak)?.Prayer);
        Assert.Equal(new DateOnly(2026, 10, 26), October.Next(isyak)?.Date);
        Assert.Equal(Prayer.Isyak, October.Current(isyak)?.Prayer);
    }

    [Fact]
    public void Next_JustAfterMidnight_IsTodaysSubuhAndCurrentIsLastNightsIsyak()
    {
        var now = Myt.At(2026, 10, 26, 0, 30);

        Assert.Equal(Myt.At(2026, 10, 26, 5, 48), October.Next(now)?.Time);
        Assert.Equal(new PrayerTime(Prayer.Isyak, Myt.At(2026, 10, 25, 20, 10), new DateOnly(2026, 10, 25)), October.Current(now));
    }

    [Fact]
    public void Next_SameInstantInUtc_GivesTheSameAnswer()
    {
        var local = Myt.At(2026, 10, 25, 21, 30);

        Assert.Equal(October.Next(local), October.Next(local.ToUniversalTime()));
    }

    [Fact]
    public void Next_AcrossTheMonthEnd_UsesTheMergedNextMonth()
    {
        var merged = October.Merge(November);

        var next = merged.Next(Myt.At(2026, 10, 31, 21, 0));

        Assert.Equal(new PrayerTime(Prayer.Subuh, Myt.At(2026, 11, 1, 5, 48), new DateOnly(2026, 11, 1)), next);
    }

    [Fact]
    public void Next_AfterTheLastIsyak_IsNullWithoutNextMonth()
    {
        Assert.Null(October.Next(Myt.At(2026, 10, 31, 21, 0)));
        Assert.Equal(Prayer.Isyak, October.Current(Myt.At(2026, 10, 31, 21, 0))?.Prayer);
    }

    [Fact]
    public void Next_SkipsNoGapInTheData()
    {
        var gappy = new PrayerSchedule("SGR01", PrayerSource.Esolat, [October.Days[0], October.Days[2]]);

        Assert.Null(gappy.Next(Myt.At(2026, 10, 1, 21, 0)));
    }

    [Fact]
    public void Next_ForImsakAndMaghrib_GivesRamadanCountdowns()
    {
        Prayer[] imsak = [Prayer.Imsak];
        Prayer[] iftar = [Prayer.Maghrib];

        Assert.Equal(Myt.At(2026, 10, 26, 5, 38), October.Next(Myt.At(2026, 10, 25, 19, 30), imsak)?.Time);
        Assert.Equal(Myt.At(2026, 10, 25, 18, 59), October.Next(Myt.At(2026, 10, 25, 12, 0), iftar)?.Time);
    }

    [Fact]
    public void Next_DoesNotTreatSyurukAsAPrayer()
    {
        Assert.Equal(Prayer.Zohor, October.Next(Myt.At(2026, 10, 25, 6, 0))?.Prayer);
    }

    [Fact]
    public void IsStale_WhenTodayIsMissing()
    {
        Assert.False(October.IsStale(Myt.At(2026, 10, 15, 12, 0)));
        Assert.True(October.IsStale(Myt.At(2026, 11, 1, 0, 0)));
        Assert.True(October.IsStale(Myt.At(2026, 9, 30, 23, 59)));
    }

    [Fact]
    public void DayAt_UsesMalaysianDates()
    {
        Assert.Equal(new DateOnly(2026, 10, 25), October.DayAt(Myt.At(2026, 10, 25, 23, 59, 59))?.Date);
        Assert.Equal(new DateOnly(2026, 10, 26), October.DayAt(new DateTimeOffset(2026, 10, 25, 16, 0, 0, TimeSpan.Zero))?.Date);
    }

    [Fact]
    public void Constructor_SortsAndKeepsTheLaterCopyOfADate()
    {
        var replacement = October.Days[0] with { Hijri = null };

        var schedule = new PrayerSchedule("SGR01", PrayerSource.Esolat, [October.Days[2], October.Days[0], replacement]);

        Assert.Equal([1, 3], schedule.Days.Select(d => d.Date.Day));
        Assert.Null(schedule.Days[0].Hijri);
    }

    [Fact]
    public void Merge_NewerWinsAndKeepsEveryDay()
    {
        // The mirror's October is the same as e-Solat's, so move one time to tell them apart.
        var date = new DateOnly(2026, 10, 15);
        var recorded = MirrorParser.Parse(MirrorFixtures.Sgr01October2026).Value!;
        var mirror = new PrayerSchedule(
            recorded.Zone,
            recorded.Source,
            recorded.Days.Select(d => d.Date == date ? d with { Zohor = d.Zohor.AddMinutes(1) } : d));
        var changed = mirror.DayOf(date)!.Zohor;
        Assert.NotEqual(October.DayOf(date)!.Zohor, changed);

        var merged = October.Merge(November).Merge(mirror);

        Assert.Equal(61, merged.Days.Count);
        Assert.Equal(PrayerSource.Mirror, merged.Source);
        Assert.Equal(changed, merged.DayOf(date)!.Zohor);
        Assert.Equal(October.DayOf(date.AddDays(1)), merged.DayOf(date.AddDays(1)));
        Assert.True(merged.Covers(2026, 10));
        Assert.True(merged.Covers(2026, 11));
    }

    [Fact]
    public void Merge_OtherZone_Throws()
    {
        var other = new PrayerSchedule("WLY01", PrayerSource.Esolat, October.Days);

        Assert.Throws<ArgumentException>(() => October.Merge(other));
    }

    [Fact]
    public void DropBefore_TrimsOldDays()
    {
        var trimmed = October.Merge(November).DropBefore(new DateOnly(2026, 11, 1));

        Assert.Equal(new DateOnly(2026, 11, 1), trimmed.FirstDate);
        Assert.Equal(30, trimmed.Days.Count);
    }

    [Theory]
    [InlineData(2026, 13)]
    [InlineData(0, 10)]
    public void Covers_InvalidMonth_IsFalse(int year, int month)
    {
        Assert.False(October.Covers(year, month));
    }

    [Fact]
    public void EmptySchedule_AnswersNothing()
    {
        var empty = new PrayerSchedule("SGR01", PrayerSource.Esolat, []);

        Assert.Null(empty.FirstDate);
        Assert.Null(empty.Next(Myt.At(2026, 10, 25, 12, 0)));
        Assert.Null(empty.Current(Myt.At(2026, 10, 25, 12, 0)));
        Assert.True(empty.IsStale(Myt.At(2026, 10, 25, 12, 0)));
    }

    [Fact]
    public void Prayers_ListsAreInDayOrder()
    {
        Assert.Equal([Prayer.Subuh, Prayer.Zohor, Prayer.Asar, Prayer.Maghrib, Prayer.Isyak], Prayers.Obligatory);
        Assert.All(Prayers.Obligatory, p => Assert.True(p.IsObligatory()));
        Assert.False(Prayer.Syuruk.IsObligatory());
        Assert.Equal(7, Prayers.Shown.Count);
        Assert.Equal(Prayers.Shown.OrderBy(p => p), Prayers.Shown);
    }
}
