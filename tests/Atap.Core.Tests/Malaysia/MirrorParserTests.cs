using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class MirrorParserTests
{
    [Fact]
    public void Parse_RecordedMonth_ReadsEveryDay()
    {
        var result = MirrorParser.Parse(MirrorFixtures.Sgr01October2026, "SGR01");

        Assert.True(result.Succeeded);
        Assert.Equal("SGR01", result.Value.Zone);
        Assert.Equal(PrayerSource.Mirror, result.Value.Source);
        Assert.Equal(31, result.Value.Days.Count);
        Assert.True(result.Value.Covers(2026, 10));
        Assert.All(result.Value.Days, d => Assert.Equal(TimeSpan.FromHours(8), d.Subuh.Offset));
    }

    [Fact]
    public void Parse_RecordedMonth_MatchesESolatForTheSameMonth()
    {
        var mirror = MirrorParser.Parse(MirrorFixtures.Sgr01October2026).Value!;
        var esolat = EsolatParser.Parse(EsolatFixtures.Sgr01October2026).Value!;

        Assert.Equal(esolat.Days.Count, mirror.Days.Count);
        for (var i = 0; i < esolat.Days.Count; i++)
            Assert.Equal(esolat.Days[i], mirror.Days[i]);
    }

    [Fact]
    public void Parse_RecordedNotPublished_FailsWithTheMessage()
    {
        var result = MirrorParser.Parse(MirrorFixtures.NotPublished);

        Assert.Equal(ParseError.BadStatus, result.Error);
        Assert.Contains("No data found", result.Detail);
    }

    [Fact]
    public void Parse_LongMessageWithControlCharacters_IsCutForTheLog()
    {
        var json = $$"""{"message":"No data\r\nfound{{new string('x', 300)}}"}""";

        var result = MirrorParser.Parse(json);

        Assert.Equal(ParseError.BadStatus, result.Error);
        Assert.NotNull(result.Detail);
        Assert.Equal(120, result.Detail.Length);
        Assert.StartsWith("No datafoundxxx", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_OtherZone_IsMismatch()
    {
        Assert.Equal(ParseError.Mismatch, MirrorParser.Parse(MirrorFixtures.Sgr01October2026, "WLY01").Error);
    }

    [Theory]
    [InlineData(2027, 10)]
    [InlineData(2026, 11)]
    [InlineData(null, 1)]
    public void Parse_OtherPeriod_IsMismatch(int? year, int? month)
    {
        var result = MirrorParser.Parse(MirrorFixtures.Sgr01October2026, "SGR01", year, month);

        Assert.Equal(ParseError.Mismatch, result.Error);
        Assert.Equal("period", result.Detail);
    }

    [Fact]
    public void Parse_ExpectedPeriod_AcceptsTheMonthAskedFor()
    {
        Assert.True(MirrorParser.Parse(MirrorFixtures.Sgr01October2026, "SGR01", 2026, 10).Succeeded);
    }

    [Fact]
    public void Parse_DayNumberThatDoesNotMatchTheTimes_IsMismatch()
    {
        var json = EsolatParserTests.ReplaceFirst(MirrorFixtures.Sgr01October2026, "{\"day\":1,", "{\"day\":2,");

        var result = MirrorParser.Parse(json);

        Assert.Equal(ParseError.Mismatch, result.Error);
        Assert.Equal("day", result.Detail);
    }

    [Fact]
    public void Parse_TimestampOutOfRange_IsBadValue()
    {
        var json = EsolatParserTests.ReplaceFirst(MirrorFixtures.Sgr01October2026, "\"fajr\":1790805180", "\"fajr\":-5");

        var result = MirrorParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("fajr", result.Detail);
    }

    [Fact]
    public void Parse_TimeAsText_IsMissingField()
    {
        var json = EsolatParserTests.ReplaceFirst(MirrorFixtures.Sgr01October2026, "\"isha\":1790856960", "\"isha\":\"20:16\"");

        var result = MirrorParser.Parse(json);

        Assert.Equal(ParseError.MissingField, result.Error);
        Assert.Equal("isha", result.Detail);
    }

    [Fact]
    public void Parse_BadMonthNumber_IsBadValue()
    {
        var json = EsolatParserTests.ReplaceFirst(MirrorFixtures.Sgr01October2026, "\"month_number\":10", "\"month_number\":13");

        Assert.Equal(ParseError.BadValue, MirrorParser.Parse(json).Error);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>", ParseError.NotJson)]
    [InlineData("", ParseError.Empty)]
    [InlineData("""{"zone":"SGR01","year":2026,"month_number":10,"prayers":[]}""", ParseError.Empty)]
    [InlineData("""{"zone":"SGR01","year":2026,"prayers":[]}""", ParseError.MissingField)]
    [InlineData("[]", ParseError.MissingField)]
    public void Parse_BadInput_FailsWithoutThrowing(string json, ParseError expected)
    {
        Assert.Equal(expected, MirrorParser.Parse(json).Error);
    }
}
