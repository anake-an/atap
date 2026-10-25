using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class HolidayTests
{
    private static readonly HolidayYear Year2026 = HolidayParser.Parse(HolidayFixtures.Year2026).Value!;
    private static readonly HolidayYear Year2027 = HolidayParser.Parse(HolidayFixtures.Year2027).Value!;
    private static readonly HolidayYear Year2028 = HolidayParser.Parse(HolidayFixtures.Year2028).Value!;

    [Fact]
    public void Parse_ReadsTheFileSortedByDate()
    {
        Assert.Equal(2026, Year2026.Year);
        Assert.Equal(12, Year2026.Holidays.Count);
        Assert.Equal(Year2026.Holidays.OrderBy(h => h.Date).Select(h => h.Date), Year2026.Holidays.Select(h => h.Date));
        Assert.Equal("Christmas Day", Year2026.Holidays[^1].NameEn);
    }

    [Fact]
    public void Parse_ReadsStatesAndTentativeFlags()
    {
        var thaipusam = Year2026.Holidays.Single(h => h.NameEn == "Thaipusam");
        var raya = Year2026.Holidays.Single(h => h.NameEn == "Hari Raya Aidilfitri");
        var labour = Year2026.Holidays.Single(h => h.NameEn == "Labour Day");

        Assert.Contains("SGR", thaipusam.States);
        Assert.False(thaipusam.IsNationwide);
        Assert.True(thaipusam.AppliesTo("sgr"));
        Assert.False(thaipusam.AppliesTo("KTN"));
        Assert.False(thaipusam.AppliesTo(null));
        Assert.True(raya.Tentative);
        Assert.True(raya.IsNationwide);
        Assert.False(labour.Tentative);
        Assert.True(labour.AppliesTo(null));
        Assert.Equal("Hari Pekerja", labour.Name("ms"));
        Assert.Equal("Labour Day", labour.Name("en"));
    }

    [Fact]
    public void Parse_AllSixteenStates_IsNationwide()
    {
        var states = string.Join(", ", MalaysiaStates.All.Select(s => $"\"{s.Code}\""));
        var json = $$"""{"schema":1,"year":2026,"holidays":[{"date":"2026-08-31","name":{"en":"National Day","ms":"Hari Kebangsaan"},"states":[{{states}}]}]}""";

        var holiday = HolidayParser.Parse(json).Value!.Holidays[0];

        Assert.True(holiday.IsNationwide);
    }

    [Fact]
    public void Parse_UnknownState_FailsWithTheCode()
    {
        var json = HolidayFixtures.Year2026.Replace("\"KUL\", \"LBN\", \"PJY\"]", "\"KUL\", \"XYZ\"]", StringComparison.Ordinal);

        var result = HolidayParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("XYZ", result.Detail);
    }

    [Fact]
    public void Parse_DateOutsideTheYear_IsMismatch()
    {
        var json = HolidayFixtures.Year2026.Replace("2026-12-25", "2027-12-25", StringComparison.Ordinal);

        var result = HolidayParser.Parse(json);

        Assert.Equal(ParseError.Mismatch, result.Error);
        Assert.Equal("2027-12-25", result.Detail);
    }

    [Fact]
    public void Parse_NewerSchema_IsUnsupported()
    {
        var json = HolidayFixtures.Year2026.Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal);

        var result = HolidayParser.Parse(json);

        Assert.Equal(ParseError.UnsupportedSchema, result.Error);
        Assert.Equal("2", result.Detail);
    }

    [Fact]
    public void Parse_MissingMalayName_FailsWithFieldName()
    {
        var json = HolidayFixtures.Year2026.Replace(", \"ms\": \"Hari Krismas\"", string.Empty, StringComparison.Ordinal);

        var result = HolidayParser.Parse(json);

        Assert.Equal(ParseError.MissingField, result.Error);
        Assert.Equal("ms", result.Detail);
    }

    [Theory]
    [InlineData("\"tentative\": true", "\"tentative\": \"yes\"", ParseError.BadValue)]
    [InlineData("\"date\": \"2026-05-01\"", "\"date\": \"1 Mei 2026\"", ParseError.BadValue)]
    [InlineData("\"states\": [\"KUL\", \"LBN\", \"PJY\"]", "\"states\": \"KUL\"", ParseError.MissingField)]
    [InlineData("\"en\": \"Labour Day\"", "\"en\": \" \"", ParseError.BadValue)]
    public void Parse_BadValues_Fail(string find, string replacement, ParseError expected)
    {
        var json = EsolatParserTests.ReplaceFirst(HolidayFixtures.Year2026, find, replacement);

        Assert.Equal(expected, HolidayParser.Parse(json).Error);
    }

    [Fact]
    public void Parse_EmptyStateList_IsRefusedNotNationwide()
    {
        var json = EsolatParserTests.ReplaceFirst(HolidayFixtures.Year2026, "\"states\": [\"KUL\", \"LBN\", \"PJY\"]", "\"states\": []");

        var result = HolidayParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("states", result.Detail);
    }

    [Theory]
    [InlineData("", ParseError.Empty)]
    [InlineData("not json", ParseError.NotJson)]
    [InlineData("""{"schema":1,"year":2026,"holidays":[]}""", ParseError.Empty)]
    [InlineData("""{"schema":1,"year":1999,"holidays":[]}""", ParseError.BadValue)]
    [InlineData("""{"year":2026,"holidays":[]}""", ParseError.MissingField)]
    [InlineData("""{"schema":1,"year":2026,"holidays":[1]}""", ParseError.MissingField)]
    public void Parse_BadFiles_FailWithoutThrowing(string json, ParseError expected)
    {
        Assert.Equal(expected, HolidayParser.Parse(json).Error);
    }

    [Fact]
    public void Next_FiltersByState()
    {
        var calendar = new HolidayCalendar([Year2026]);
        var today = new DateOnly(2026, 10, 25);

        var selangor = calendar.Next(today, "SGR");
        var sarawak = calendar.Next(today, "SWK");

        Assert.Equal(HolidayLookupStatus.Found, selangor.Status);
        Assert.Equal("Deepavali", selangor.Holiday?.NameMs);
        Assert.Equal(new DateOnly(2026, 11, 8), selangor.Holiday?.Date);
        Assert.Equal("Christmas Day", sarawak.Holiday?.NameEn);
    }

    [Fact]
    public void Next_WithoutAState_ShowsOnlyNationwideHolidays()
    {
        var calendar = new HolidayCalendar([Year2026]);

        Assert.Equal("Chinese New Year", calendar.Next(new DateOnly(2026, 1, 1), null).Holiday?.NameEn);
        Assert.Equal("Chinese New Year", calendar.Next(new DateOnly(2026, 1, 1), "XYZ").Holiday?.NameEn);
    }

    [Fact]
    public void Next_IncludesToday()
    {
        var calendar = new HolidayCalendar([Year2026]);

        Assert.Equal(new DateOnly(2026, 8, 31), calendar.Next(new DateOnly(2026, 8, 31), "SGR").Holiday?.Date);
    }

    [Fact]
    public void Next_AfterTheLastHolidayOfTheLastYear_DataEnds()
    {
        var calendar = new HolidayCalendar([Year2026]);

        var lookup = calendar.Next(new DateOnly(2026, 12, 26), "SGR");

        Assert.Equal(HolidayLookupStatus.DataEnds, lookup.Status);
        Assert.Null(lookup.Holiday);
        Assert.False(calendar.IsPastData(new DateOnly(2026, 12, 26)));
    }

    [Fact]
    public void Next_AfterTheLastYear_DataEnds()
    {
        var calendar = new HolidayCalendar([Year2026, Year2027]);
        var later = new DateOnly(2028, 1, 5);

        Assert.Equal(HolidayLookupStatus.DataEnds, calendar.Next(later, "SGR").Status);
        Assert.True(calendar.IsPastData(later));
        Assert.Equal(2027, calendar.LastYear);
    }

    [Fact]
    public void Next_CrossesIntoTheNextBundledYear()
    {
        var calendar = new HolidayCalendar([Year2027, Year2026]);
        var boxingDay = new DateOnly(2026, 12, 26);

        Assert.Equal(new DateOnly(2027, 1, 1), calendar.Next(boxingDay, "SGR").Holiday?.Date);
        Assert.Equal(new DateOnly(2027, 2, 6), calendar.Next(boxingDay, "KDH").Holiday?.Date);
        Assert.Equal(2026, calendar.FirstYear);
    }

    [Fact]
    public void Next_StopsAtAMissingYear()
    {
        var calendar = new HolidayCalendar([Year2026, Year2028]);

        Assert.Equal(HolidayLookupStatus.DataEnds, calendar.Next(new DateOnly(2026, 12, 26), "SGR").Status);
    }

    [Fact]
    public void Next_BeforeTheFirstYear_HasNoData()
    {
        Assert.Equal(HolidayLookupStatus.NoData, new HolidayCalendar([Year2026]).Next(new DateOnly(2025, 6, 1), "SGR").Status);
        Assert.Equal(HolidayLookupStatus.NoData, new HolidayCalendar([]).Next(new DateOnly(2026, 6, 1), "SGR").Status);
    }

    [Fact]
    public void Upcoming_ListsInDateOrderAcrossYears()
    {
        var calendar = new HolidayCalendar([Year2026, Year2027]);

        var upcoming = calendar.Upcoming(new DateOnly(2026, 12, 1), "SGR", 3);

        Assert.Equal([new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1), new DateOnly(2027, 2, 6)], upcoming.Select(h => h.Date));
        Assert.Empty(calendar.Upcoming(new DateOnly(2026, 12, 1), "SGR", 0));
    }

    [Fact]
    public void Upcoming_StopsWhereTheDataEnds()
    {
        var calendar = new HolidayCalendar([Year2026]);

        Assert.Equal(2, calendar.Upcoming(new DateOnly(2026, 11, 1), "SGR", 5).Count);
    }

    [Fact]
    public void On_ReturnsEveryHolidayThatDay()
    {
        var calendar = new HolidayCalendar([Year2026]);

        Assert.Equal(["Thaipusam", "Federal Territory Day"], calendar.On(new DateOnly(2026, 2, 1), "KUL").Select(h => h.NameEn));
        Assert.Equal(["Thaipusam"], calendar.On(new DateOnly(2026, 2, 1), "SGR").Select(h => h.NameEn));
        Assert.Empty(calendar.On(new DateOnly(2030, 2, 1), "SGR"));
    }

    [Fact]
    public void Calendar_SameYearTwice_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HolidayCalendar([Year2026, Year2026]));
    }
}
