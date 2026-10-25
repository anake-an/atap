using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class EsolatParserTests
{
    [Theory]
    [InlineData("01-Okt-2026", 2026, 10, 1)]
    [InlineData("31-Dis-2026", 2026, 12, 31)]
    [InlineData("01-Ogos-2026", 2026, 8, 1)]
    [InlineData("15-Mac-2026", 2026, 3, 15)]
    [InlineData("05-Mei-2026", 2026, 5, 5)]
    [InlineData("29-Feb-2028", 2028, 2, 29)]
    [InlineData("01-Oct-2026", 2026, 10, 1)]
    [InlineData(" 01-okt-2026 ", 2026, 10, 1)]
    public void TryParseDate_ReadsMalayMonthAbbreviations(string text, int year, int month, int day)
    {
        Assert.True(EsolatParser.TryParseDate(text, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("31-Feb-2026")]
    [InlineData("29-Feb-2026")]
    [InlineData("01-Xyz-2026")]
    [InlineData("2026-10-01")]
    [InlineData("01-Okt")]
    [InlineData("-1-Okt-2026")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseDate_RejectsOtherShapes(string? text)
    {
        Assert.False(EsolatParser.TryParseDate(text, out _));
    }

    [Theory]
    [InlineData("05:43:00", 5, 43, 0)]
    [InlineData("20:16:00", 20, 16, 0)]
    [InlineData("13:06:30", 13, 6, 30)]
    [InlineData("05:43", 5, 43, 0)]
    public void TryParseTime_Reads24HourTimes(string text, int hour, int minute, int second)
    {
        Assert.True(EsolatParser.TryParseTime(text, out var time));
        Assert.Equal(new TimeOnly(hour, minute, second), time);
    }

    [Theory]
    [InlineData("25:00:00")]
    [InlineData("5.43")]
    [InlineData("05:43 PM")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseTime_RejectsOtherShapes(string? text)
    {
        Assert.False(EsolatParser.TryParseTime(text, out _));
    }

    [Fact]
    public void Parse_RecordedMonth_ReadsEveryDay()
    {
        var result = EsolatParser.Parse(EsolatFixtures.Sgr01October2026, "SGR01");

        Assert.True(result.Succeeded);
        var schedule = result.Value;
        Assert.Equal("SGR01", schedule.Zone);
        Assert.Equal(PrayerSource.Esolat, schedule.Source);
        Assert.False(schedule.IsApproximate);
        Assert.Equal(31, schedule.Days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), schedule.FirstDate);
        Assert.Equal(new DateOnly(2026, 10, 31), schedule.LastDate);
        Assert.True(schedule.Covers(2026, 10));
        Assert.False(schedule.Covers(2026, 11));
        Assert.All(schedule.Days, d => Assert.True(d.IsInOrder));
    }

    [Fact]
    public void Parse_RecordedMonth_KeepsTimesInMalaysianTime()
    {
        var day = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!.DayOf(new DateOnly(2026, 10, 1))!;

        Assert.Equal(new HijriDate(1448, 4, 19), day.Hijri);
        Assert.Equal(Myt.At(2026, 10, 1, 5, 43), day.Imsak);
        Assert.Equal(Myt.At(2026, 10, 1, 5, 53), day.Subuh);
        Assert.Equal(Myt.At(2026, 10, 1, 6, 59), day.Syuruk);
        Assert.Equal(Myt.At(2026, 10, 1, 7, 24), day.Dhuha);
        Assert.Equal(Myt.At(2026, 10, 1, 13, 6), day.Zohor);
        Assert.Equal(Myt.At(2026, 10, 1, 16, 16), day.Asar);
        Assert.Equal(Myt.At(2026, 10, 1, 19, 7), day.Maghrib);
        Assert.Equal(Myt.At(2026, 10, 1, 20, 16), day.Isyak);
        Assert.Equal(TimeSpan.FromHours(8), day.Subuh.Offset);
    }

    [Fact]
    public void Parse_RecordedDecemberAndAugust_ReadDisAndOgos()
    {
        var december = EsolatParser.Parse(EsolatFixtures.Sgr01YearEnd);
        var august = EsolatParser.Parse(EsolatFixtures.Sgr01AugustTrimmed);

        Assert.True(december.Succeeded);
        Assert.Equal([new DateOnly(2026, 12, 30), new DateOnly(2026, 12, 31)], december.Value.Days.Select(d => d.Date));
        Assert.False(december.Value.Covers(2026, 12));
        Assert.True(august.Succeeded);
        Assert.Equal(new DateOnly(2026, 8, 1), august.Value.FirstDate);
        Assert.Equal(new DateOnly(2026, 8, 3), august.Value.LastDate);
    }

    [Fact]
    public void Parse_RecordedNoRecord_FailsWithStatus()
    {
        var result = EsolatParser.Parse(EsolatFixtures.NoRecord);

        Assert.False(result.Succeeded);
        Assert.Equal(ParseError.BadStatus, result.Error);
        Assert.Equal("NO_RECORD!", result.Detail);
    }

    [Theory]
    [InlineData("OK")]
    [InlineData("ok!")]
    [InlineData("")]
    public void Parse_StatusOtherThanOk_Fails(string status)
    {
        var json = EsolatFixtures.Sgr01October2026.Replace("\"status\":\"OK!\"", $"\"status\":\"{status}\"", StringComparison.Ordinal);

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.BadStatus, result.Error);
        Assert.Null(result.Value);
    }

    [Fact]
    public void Parse_MissingStatus_Fails()
    {
        var json = EsolatFixtures.Sgr01October2026.Replace("\"status\":\"OK!\",", string.Empty, StringComparison.Ordinal);

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.MissingField, result.Error);
        Assert.Equal("status", result.Detail);
    }

    [Fact]
    public void Parse_LongStatusWithControlCharacters_IsCutForTheLog()
    {
        var status = "BAD\\r\\nFAKE LOG LINE\\u202E" + new string('x', 300);
        var json = EsolatFixtures.Sgr01October2026.Replace("\"status\":\"OK!\"", $"\"status\":\"{status}\"", StringComparison.Ordinal);

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.BadStatus, result.Error);
        Assert.NotNull(result.Detail);
        Assert.Equal(120, result.Detail.Length);
        Assert.StartsWith("BADFAKE LOG LINExxx", result.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Detail, c => char.IsControl(c) || c == '‮');
    }

    [Fact]
    public void Parse_RecordedHtmlErrorPage_IsNotJson()
    {
        Assert.Equal(ParseError.NotJson, EsolatParser.Parse(EsolatFixtures.ServerErrorPage).Error);
    }

    [Fact]
    public void Parse_TruncatedResponse_IsNotJson()
    {
        Assert.Equal(ParseError.NotJson, EsolatParser.Parse(EsolatFixtures.Sgr01October2026[..500]).Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_NoText_IsEmpty(string? json)
    {
        Assert.Equal(ParseError.Empty, EsolatParser.Parse(json).Error);
    }

    [Fact]
    public void Parse_OtherZone_IsMismatch()
    {
        var result = EsolatParser.Parse(EsolatFixtures.Sgr01October2026, "SGR02");

        Assert.Equal(ParseError.Mismatch, result.Error);
        Assert.Equal("zone", result.Detail);
    }

    [Fact]
    public void Parse_ExpectedZone_IgnoresCase()
    {
        Assert.True(EsolatParser.Parse(EsolatFixtures.Sgr01October2026, "sgr01").Succeeded);
    }

    [Fact]
    public void Parse_ExpectedPeriod_AcceptsTheMonthAskedFor()
    {
        var result = EsolatParser.Parse(EsolatFixtures.Sgr01October2026, "SGR01", 2026, 10);

        Assert.True(result.Succeeded);
        Assert.True(result.Value.Covers(2026, 10));
    }

    [Theory]
    // A January prefetch made in December gets this January back, since e-Solat ignores the year.
    [InlineData(2027, 10)]
    [InlineData(2026, 11)]
    [InlineData(null, 9)]
    [InlineData(2025, null)]
    public void Parse_OtherPeriod_IsMismatch(int? year, int? month)
    {
        var result = EsolatParser.Parse(EsolatFixtures.Sgr01October2026, "SGR01", year, month);

        Assert.Equal(ParseError.Mismatch, result.Error);
        Assert.Equal("period", result.Detail);
    }

    [Fact]
    public void Parse_ExpectedPeriod_ChecksEveryDay()
    {
        // The recorded response for 30 December 2026 to 2 January 2027 holds only the December days.
        Assert.Equal(ParseError.Mismatch, EsolatParser.Parse(EsolatFixtures.Sgr01YearEnd, "SGR01", 2027, 1).Error);
        Assert.True(EsolatParser.Parse(EsolatFixtures.Sgr01YearEnd, "SGR01", 2026, 12).Succeeded);
    }

    [Fact]
    public void Parse_MissingTime_FailsWithFieldName()
    {
        var json = ReplaceFirst(EsolatFixtures.Sgr01October2026, ",\"isha\":\"20:16:00\"", string.Empty);

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.MissingField, result.Error);
        Assert.Equal("isha", result.Detail);
    }

    [Fact]
    public void Parse_BadTime_FailsWithFieldName()
    {
        var json = ReplaceFirst(EsolatFixtures.Sgr01October2026, "\"fajr\":\"05:53:00\"", "\"fajr\":\"5.53\"");

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("fajr", result.Detail);
    }

    [Fact]
    public void Parse_BadDate_Fails()
    {
        var json = ReplaceFirst(EsolatFixtures.Sgr01October2026, "01-Okt-2026", "2026-10-01");

        Assert.Equal(ParseError.BadValue, EsolatParser.Parse(json).Error);
    }

    [Fact]
    public void Parse_TimesOutOfOrder_Fails()
    {
        var json = ReplaceFirst(EsolatFixtures.Sgr01October2026, "\"maghrib\":\"19:07:00\"", "\"maghrib\":\"12:07:00\"");

        var result = EsolatParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("order", result.Detail);
    }

    [Fact]
    public void Parse_BadHijri_KeepsTheTimes()
    {
        var json = ReplaceFirst(EsolatFixtures.Sgr01October2026, "\"hijri\":\"1448-04-19\"", "\"hijri\":\"soon\"");

        var result = EsolatParser.Parse(json);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value.Days[0].Hijri);
        Assert.NotNull(result.Value.Days[1].Hijri);
    }

    [Fact]
    public void Parse_NoDays_IsEmpty()
    {
        var json = """{"prayerTime":[],"status":"OK!","zone":"SGR01"}""";

        Assert.Equal(ParseError.Empty, EsolatParser.Parse(json).Error);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"OK!\"")]
    [InlineData("""{"prayerTime":{"data":[]},"status":"OK!","zone":"SGR01"}""")]
    public void Parse_WrongShape_FailsWithoutThrowing(string json)
    {
        Assert.False(EsolatParser.Parse(json).Succeeded);
    }

    internal static string ReplaceFirst(string text, string find, string replacement)
    {
        var index = text.IndexOf(find, StringComparison.Ordinal);
        Assert.True(index >= 0, $"'{find}' is not in the fixture.");
        return string.Concat(text.AsSpan(0, index), replacement, text.AsSpan(index + find.Length));
    }
}
