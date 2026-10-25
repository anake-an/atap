using Atap.Core.Rules;
using static Atap.Core.Tests.Rules.RuleTestData;

namespace Atap.Core.Tests.Rules;

public class TimeWindowConditionTests
{
    [Fact]
    public void Fixture_DaysOfTheWeekAreAsDocumented()
    {
        Assert.Equal(DayOfWeek.Friday, Myt("2026-10-23 12:00").DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, Myt("2026-10-26 12:00").DayOfWeek);
    }

    [Theory]
    [InlineData("2026-10-23 08:00", true)]
    [InlineData("2026-10-23 12:59", true)]
    [InlineData("2026-10-23 13:00", false)]
    [InlineData("2026-10-23 07:59", false)]
    [InlineData("2026-10-24 10:00", false)]
    [InlineData("2026-10-25 10:00", false)]
    [InlineData("2026-10-26 10:00", true)]
    public void Weekdays_OpenOnlyMondayToFriday(string local, bool open)
    {
        var classes = Window("08:00", "13:00", Weekdays);

        Assert.Equal(open, classes.Matches(Context(local)));
    }

    [Theory]
    [InlineData("2026-10-23 23:00", true)]
    [InlineData("2026-10-24 02:00", true)]
    [InlineData("2026-10-24 06:59", true)]
    [InlineData("2026-10-24 07:00", false)]
    [InlineData("2026-10-23 22:59", false)]
    [InlineData("2026-10-23 12:00", false)]
    [InlineData("2026-10-24 00:00", true)]
    public void Overnight_EveryDay_OpenAcrossMidnight(string local, bool open)
    {
        var night = Window("23:00", "07:00");

        Assert.Equal(open, night.Matches(Context(local)));
    }

    [Theory]
    [InlineData("2026-10-23 23:00", true)]   // Friday night
    [InlineData("2026-10-24 01:00", true)]   // Saturday early, Friday's window
    [InlineData("2026-10-24 03:00", false)]
    [InlineData("2026-10-24 22:30", true)]   // Saturday night
    [InlineData("2026-10-25 01:30", true)]   // Sunday early, Saturday's window
    [InlineData("2026-10-25 22:30", false)]  // Sunday is not listed
    [InlineData("2026-10-26 01:00", false)]
    [InlineData("2026-10-23 01:00", false)]  // Thursday is not listed
    public void Overnight_BelongsToTheDayItStarts(string local, bool open)
    {
        var weekendNights = Window("22:00", "02:00", DayOfWeek.Friday, DayOfWeek.Saturday);

        Assert.Equal(open, weekendNights.Matches(Context(local)));
    }

    [Theory]
    [InlineData("2026-10-24 00:00", true)]
    [InlineData("2026-10-24 23:59", true)]
    [InlineData("2026-10-25 00:00", false)]
    [InlineData("2026-10-23 23:59", false)]
    public void EqualTimes_AtMidnight_IsTheWholeDay(string local, bool open)
    {
        var saturday = Window("00:00", "00:00", DayOfWeek.Saturday);

        Assert.Equal(open, saturday.Matches(Context(local)));
    }

    [Theory]
    [InlineData("2026-10-23 18:00", true)]
    [InlineData("2026-10-24 17:59", true)]
    [InlineData("2026-10-24 18:00", false)]
    [InlineData("2026-10-23 17:59", false)]
    public void EqualTimes_IsTwentyFourHoursFromTheStart(string local, bool open)
    {
        var fridayEvening = Window("18:00", "18:00", DayOfWeek.Friday);

        Assert.Equal(open, fridayEvening.Matches(Context(local)));
        Assert.Equal(TimeSpan.FromDays(1), fridayEvening.Length);
    }

    [Fact]
    public void Matches_ReadsTheContextTimeZone()
    {
        var night = Window("22:00", "07:00");
        var fridayNightInMalaysia = new DateTimeOffset(2026, 10, 23, 14, 30, 0, TimeSpan.Zero);

        Assert.True(night.Matches(new RuleContext { Now = fridayNightInMalaysia, TimeZone = MalaysiaZone }));
        Assert.False(night.Matches(new RuleContext { Now = fridayNightInMalaysia, TimeZone = TimeZoneInfo.Utc }));
    }

    [Theory]
    [InlineData("2026-10-23 07:00", "2026-10-23 08:00")]  // before the window
    [InlineData("2026-10-23 08:00", "2026-10-23 13:00")]  // exactly at the start
    [InlineData("2026-10-23 10:00", "2026-10-23 13:00")]  // inside
    [InlineData("2026-10-23 13:00", "2026-10-26 08:00")]  // exactly at the end, on a Friday
    [InlineData("2026-10-23 14:00", "2026-10-26 08:00")]  // the weekend is skipped
    [InlineData("2026-10-24 09:00", "2026-10-26 08:00")]
    [InlineData("2026-10-22 13:30", "2026-10-23 08:00")]  // Thursday afternoon
    public void NextChange_Weekdays(string local, string expected)
    {
        var classes = Window("08:00", "13:00", Weekdays);

        Assert.Equal(Myt(expected), classes.NextChange(Myt(local), MalaysiaZone));
    }

    [Theory]
    [InlineData("2026-10-23 22:00", "2026-10-23 23:00")]
    [InlineData("2026-10-23 23:30", "2026-10-24 07:00")]
    [InlineData("2026-10-24 03:00", "2026-10-24 07:00")]
    [InlineData("2026-10-24 07:00", "2026-10-24 23:00")]
    public void NextChange_OvernightEveryDay(string local, string expected)
    {
        var night = Window("23:00", "07:00");

        Assert.Equal(Myt(expected), night.NextChange(Myt(local), MalaysiaZone));
    }

    [Theory]
    [InlineData("2026-10-25 01:00", "2026-10-25 02:00")]  // Saturday's window ends on Sunday
    [InlineData("2026-10-25 03:00", "2026-10-30 22:00")]  // next Friday
    [InlineData("2026-10-24 01:59", "2026-10-24 02:00")]
    [InlineData("2026-10-24 02:00", "2026-10-24 22:00")]
    public void NextChange_OvernightOnChosenDays(string local, string expected)
    {
        var weekendNights = Window("22:00", "02:00", DayOfWeek.Friday, DayOfWeek.Saturday);

        Assert.Equal(Myt(expected), weekendNights.NextChange(Myt(local), MalaysiaZone));
    }

    [Fact]
    public void NextChange_TouchingWindows_CountAsOne()
    {
        var weekend = Window("00:00", "00:00", DayOfWeek.Saturday, DayOfWeek.Sunday);

        // Saturday ends where Sunday starts, so nothing changes until Monday 00:00.
        Assert.Equal(Myt("2026-10-26 00:00"), weekend.NextChange(Myt("2026-10-24 10:00"), MalaysiaZone));
        Assert.Equal(Myt("2026-10-31 00:00"), weekend.NextChange(Myt("2026-10-26 00:00"), MalaysiaZone));
    }

    [Fact]
    public void NextChange_AlwaysOpen_IsNull()
    {
        Assert.Null(Window("00:00", "00:00").NextChange(Myt("2026-10-23 10:00"), MalaysiaZone));
        Assert.Null(Window("18:00", "18:00").NextChange(Myt("2026-10-23 10:00"), MalaysiaZone));
        Assert.Null(Window("06:30", "06:30", [.. Enum.GetValues<DayOfWeek>()]).NextChange(Myt("2026-10-23 10:00"), MalaysiaZone));
    }

    [Fact]
    public void NextChange_IsStrictlyAfterNow()
    {
        var classes = Window("08:00", "13:00", Weekdays);
        var justBefore = Myt("2026-10-23 08:00").AddMilliseconds(-1);

        Assert.Equal(Myt("2026-10-23 08:00"), classes.NextChange(justBefore, MalaysiaZone));
        Assert.Equal(Myt("2026-10-23 13:00"), classes.NextChange(Myt("2026-10-23 08:00"), MalaysiaZone));
    }

    [Fact]
    public void NextChange_UsesTheOffsetOnTheDayOfTheChange()
    {
        var office = Window("09:00", "17:00");
        var saturdayEvening = new DateTimeOffset(2026, 3, 28, 18, 0, 0, TimeSpan.Zero);

        // Daylight saving starts overnight, so 09:00 on Sunday is 08:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 8, 0, 0, TimeSpan.Zero), office.NextChange(saturdayEvening, Daylight));
    }

    [Fact]
    public void NextChange_StartSkippedByDaylightSaving_HappensWhenTheClockJumps()
    {
        var window = Window("02:30", "05:00");
        var midnight = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero);

        // 02:30 does not exist that night: the clock goes from 02:00 straight to 03:00.
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 3, 0, 0, TimeSpan.FromHours(1)), window.NextChange(midnight, Daylight));
    }

    [Fact]
    public void NextChange_TimeThatOccursTwice_GivesTheNextOccurrence()
    {
        var window = Window("01:30", "04:00");

        // 01:30 happens at 00:30 UTC and again at 01:30 UTC when the clock goes back.
        var beforeBoth = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(1));
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), window.NextChange(beforeBoth, Daylight));

        var betweenThem = new DateTimeOffset(2026, 10, 25, 1, 10, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 1, 30, 0, TimeSpan.Zero), window.NextChange(betweenThem, Daylight));
    }
}
