using Atap.Core.Malaysia;

namespace Atap.Core.Tests.Malaysia;

public class WeatherTests
{
    [Fact]
    public void Forecast_RecordedResponse_ReadsSevenDays()
    {
        var result = ForecastParser.Parse(DataGovFixtures.ForecastGombak);

        Assert.True(result.Succeeded);
        var days = result.Value.ForLocation("Ds055");
        Assert.Equal(7, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 25), days[0].Date);
        Assert.Equal(new DateOnly(2026, 10, 31), days[^1].Date);
        Assert.Equal([("Ds055", "Gombak")], result.Value.Locations);
    }

    [Fact]
    public void Forecast_RecordedDay_BreaksPhrasesIntoParts()
    {
        var day = ForecastParser.Parse(DataGovFixtures.ForecastGombak).Value!.DayOf("ds055", new DateOnly(2026, 10, 25))!;

        Assert.Equal("Gombak", day.LocationName);
        Assert.Equal("Ribut petir di beberapa tempat", day.Summary.Text);
        Assert.Equal(WeatherCondition.Thunderstorm, day.Summary.Condition);
        Assert.Equal(WeatherExtent.Scattered, day.Summary.Extent);
        Assert.Equal(WeatherArea.Everywhere, day.Summary.Area);
        Assert.Equal(WeatherCondition.Rain, day.Afternoon.Condition);
        Assert.Equal(WeatherExtent.Scattered, day.Afternoon.Extent);
        Assert.Equal(WeatherCondition.NoRain, day.Night.Condition);
        Assert.Equal(ForecastPeriods.Morning | ForecastPeriods.Afternoon, day.SummaryWhen);
        Assert.Equal(23, day.MinTemperature);
        Assert.Equal(30, day.MaxTemperature);
        Assert.True(day.Summary.IsWet);
    }

    [Fact]
    public void Forecast_RecordedDryMorning()
    {
        var day = ForecastParser.Parse(DataGovFixtures.ForecastGombak).Value!.DayOf("Ds055", new DateOnly(2026, 10, 31))!;

        Assert.Equal(WeatherCondition.NoRain, day.Morning.Condition);
        Assert.False(day.Morning.IsWet);
        Assert.Equal(ForecastPeriods.Afternoon, day.SummaryWhen);
        Assert.Equal(33, day.MaxTemperature);
    }

    // Every distinct phrase in the full forecast recorded on 2026-10-25.
    [Theory]
    [InlineData("Tiada Hujan", WeatherCondition.NoRain, WeatherExtent.None, WeatherArea.Everywhere)]
    [InlineData("Hujan", WeatherCondition.Rain, WeatherExtent.None, WeatherArea.Everywhere)]
    [InlineData("Hujan di beberapa tempat", WeatherCondition.Rain, WeatherExtent.Scattered, WeatherArea.Everywhere)]
    [InlineData("Hujan di kebanyakan tempat", WeatherCondition.Rain, WeatherExtent.Widespread, WeatherArea.Everywhere)]
    [InlineData("Hujan menyeluruh", WeatherCondition.Rain, WeatherExtent.General, WeatherArea.Everywhere)]
    [InlineData("Hujan di beberapa tempat di kawasan pedalaman", WeatherCondition.Rain, WeatherExtent.Scattered, WeatherArea.Inland)]
    [InlineData("Hujan di beberapa tempat di kawasan pantai", WeatherCondition.Rain, WeatherExtent.Scattered, WeatherArea.Coastal)]
    [InlineData("Hujan di kebanyakan tempat di kawasan pedalaman", WeatherCondition.Rain, WeatherExtent.Widespread, WeatherArea.Inland)]
    [InlineData("Ribut petir", WeatherCondition.Thunderstorm, WeatherExtent.None, WeatherArea.Everywhere)]
    [InlineData("Ribut petir di beberapa tempat", WeatherCondition.Thunderstorm, WeatherExtent.Scattered, WeatherArea.Everywhere)]
    [InlineData("Ribut petir di kebanyakan tempat", WeatherCondition.Thunderstorm, WeatherExtent.Widespread, WeatherArea.Everywhere)]
    [InlineData("Ribut petir menyeluruh", WeatherCondition.Thunderstorm, WeatherExtent.General, WeatherArea.Everywhere)]
    [InlineData("Ribut petir di beberapa tempat di kawasan pedalaman", WeatherCondition.Thunderstorm, WeatherExtent.Scattered, WeatherArea.Inland)]
    [InlineData("Ribut petir di beberapa tempat di kawasan pantai", WeatherCondition.Thunderstorm, WeatherExtent.Scattered, WeatherArea.Coastal)]
    [InlineData("Ribut petir menyeluruh di kawasan pantai", WeatherCondition.Thunderstorm, WeatherExtent.General, WeatherArea.Coastal)]
    [InlineData("Ribut petir di kebanyakan tempat di kawasan pantai", WeatherCondition.Thunderstorm, WeatherExtent.Widespread, WeatherArea.Coastal)]
    [InlineData("Ribut petir di kebanyakan tempat di kawasan pedalaman", WeatherCondition.Thunderstorm, WeatherExtent.Widespread, WeatherArea.Inland)]
    [InlineData("Mendung", WeatherCondition.Cloudy, WeatherExtent.None, WeatherArea.Everywhere)]
    [InlineData("Hujan di satu dua tempat", WeatherCondition.Rain, WeatherExtent.Isolated, WeatherArea.Everywhere)]
    [InlineData("Berjerebu", WeatherCondition.Hazy, WeatherExtent.None, WeatherArea.Everywhere)]
    public void Phrase_KnownForms(string text, WeatherCondition condition, WeatherExtent extent, WeatherArea area)
    {
        var phrase = ForecastPhrase.Parse(text);

        Assert.Equal(text, phrase.Text);
        Assert.Equal(condition, phrase.Condition);
        Assert.Equal(extent, phrase.Extent);
        Assert.Equal(area, phrase.Area);
    }

    [Theory]
    [InlineData("Salji")]
    [InlineData("")]
    [InlineData(null)]
    public void Phrase_UnknownForms_KeepTheText(string? text)
    {
        var phrase = ForecastPhrase.Parse(text);

        Assert.Equal(WeatherCondition.Unknown, phrase.Condition);
        Assert.Equal(text ?? string.Empty, phrase.Text);
    }

    [Theory]
    [InlineData("Pagi", ForecastPeriods.Morning)]
    [InlineData("Petang", ForecastPeriods.Afternoon)]
    [InlineData("Malam", ForecastPeriods.Night)]
    [InlineData("Pagi dan Petang", ForecastPeriods.Morning | ForecastPeriods.Afternoon)]
    [InlineData("Petang dan Malam", ForecastPeriods.Afternoon | ForecastPeriods.Night)]
    [InlineData("Pagi dan Malam", ForecastPeriods.Morning | ForecastPeriods.Night)]
    [InlineData("Sepanjang Hari", ForecastPeriods.AllDay)]
    [InlineData("Tiada", ForecastPeriods.None)]
    [InlineData(null, ForecastPeriods.None)]
    public void Periods_KnownForms(string? text, ForecastPeriods expected)
    {
        Assert.Equal(expected, ForecastParser.ParsePeriods(text));
    }

    [Theory]
    [InlineData("[]", ParseError.Empty)]
    [InlineData("{}", ParseError.MissingField)]
    [InlineData("<html></html>", ParseError.NotJson)]
    [InlineData("""[{"date":"2026-10-25"}]""", ParseError.MissingField)]
    [InlineData("""[{"location":{"location_id":"Ds055"},"date":"25-10-2026"}]""", ParseError.BadValue)]
    public void Forecast_BadInput_FailsWithoutThrowing(string json, ParseError expected)
    {
        Assert.Equal(expected, ForecastParser.Parse(json).Error);
    }

    [Fact]
    public void Forecast_MissingTextAndTemperatures_AreTolerated()
    {
        var result = ForecastParser.Parse("""[{"location":{"location_id":"St008","location_name":"Selangor"},"date":"2026-10-25","morning_forecast":null}]""");

        Assert.True(result.Succeeded);
        Assert.Equal(WeatherCondition.Unknown, result.Value.Days[0].Morning.Condition);
        Assert.Null(result.Value.Days[0].MaxTemperature);
    }

    [Fact]
    public void Warnings_RecordedResponse_ReadsEveryEntry()
    {
        var result = WarningParser.Parse(DataGovFixtures.Warnings);

        Assert.True(result.Succeeded);
        Assert.Equal(
            [WarningKind.WindAndSea, WarningKind.WindAndSea, WarningKind.Thunderstorm, WarningKind.NoAdvisory],
            result.Value.Select(w => w.Kind));
    }

    [Fact]
    public void Warnings_RecordedThunderstorm_HasTimesTextAndStates()
    {
        var storm = WarningParser.Parse(DataGovFixtures.Warnings).Value!.Single(w => w.Kind == WarningKind.Thunderstorm);

        Assert.Equal(Myt.At(2026, 10, 25, 1, 25), storm.Issued);
        Assert.Equal(Myt.At(2026, 10, 25, 1, 0), storm.ValidFrom);
        Assert.Equal(Myt.At(2026, 10, 25, 6, 0), storm.ValidTo);
        Assert.Equal("Amaran Ribut Petir", storm.Title("ms"));
        Assert.Equal("Thunderstorms Warning", storm.Title("en"));
        Assert.StartsWith("Ribut petir, hujan lebat dan angin kencang", storm.Text("ms"), StringComparison.Ordinal);
        Assert.StartsWith("Thunderstorms, heavy rain and strong winds", storm.Text("en"), StringComparison.Ordinal);
        Assert.Contains("Labuk & Sugut", storm.Text("en"), StringComparison.Ordinal);
        Assert.Null(storm.Instruction("en"));
        Assert.True(storm.IsLandWarning);
        // This warning named every state and territory.
        Assert.Equal(MalaysiaStates.All.Select(s => s.Code), storm.States);
        Assert.True(storm.AffectsState("sgr"));
    }

    [Fact]
    public void Warnings_IsActiveOnlyInsideTheValidity()
    {
        var storm = WarningParser.Parse(DataGovFixtures.Warnings).Value!.Single(w => w.Kind == WarningKind.Thunderstorm);

        Assert.False(storm.IsActive(Myt.At(2026, 10, 25, 0, 59)));
        Assert.True(storm.IsActive(Myt.At(2026, 10, 25, 1, 0)));
        Assert.True(storm.IsActive(Myt.At(2026, 10, 25, 5, 59)));
        Assert.False(storm.IsActive(Myt.At(2026, 10, 25, 6, 0)));
    }

    [Fact]
    public void Warnings_SeaWarningsNameNoStates()
    {
        var sea = WarningParser.Parse(DataGovFixtures.Warnings).Value!.Where(w => w.Kind == WarningKind.WindAndSea).ToList();

        Assert.All(sea, w =>
        {
            Assert.Contains("Selangor", w.TextEn, StringComparison.Ordinal);
            Assert.Empty(w.States);
            Assert.False(w.AffectsState("SGR"));
            Assert.False(w.IsLandWarning);
            Assert.Equal("This condition of strong winds and rough seas is dangerous.", w.Instruction("en"));
        });
    }

    [Fact]
    public void Warnings_NoAdvisoryHasOpenValidity()
    {
        var none = WarningParser.Parse(DataGovFixtures.Warnings).Value!.Single(w => w.Kind == WarningKind.NoAdvisory);

        Assert.Null(none.ValidFrom);
        Assert.Null(none.ValidTo);
        Assert.Equal("Tiada Nasihat", none.Title("ms"));
    }

    [Fact]
    public void Warnings_IdsAreStableAndUnique()
    {
        var first = WarningParser.Parse(DataGovFixtures.Warnings).Value!;
        var second = WarningParser.Parse(DataGovFixtures.Warnings).Value!;

        Assert.Equal(first.Select(w => w.Id), second.Select(w => w.Id));
        Assert.Equal(first.Count, first.Select(w => w.Id).Distinct().Count());
        Assert.All(first, w => Assert.Matches("^[0-9a-f]{16}$", w.Id));
    }

    [Fact]
    public void Warnings_StateFilterUsesOnlyTheStatesNamed()
    {
        // The recorded thunderstorm warning, cut down to its Kelantan and Terengganu parts.
        var json = """
            [{"warning_issue":{"issued":"2026-10-25T01:25:00","title_bm":"Amaran Ribut Petir","title_en":"Thunderstorms Warning"},"valid_from":"2026-10-25T01:00:00","valid_to":"2026-10-25T06:00:00","heading_en":"THUNDERSTORMS WARNING","text_en":"Thunderstorms, heavy rain and strong winds are expected over the states of Kelantan (Tumpat, Kota Bharu and Bachok) • Terengganu (Kemaman, Dungun and Hulu Terengganu) until 6:00 AM; Sunday, 25 October 2026.","instruction_en":null,"heading_bm":"AMARAN RIBUT PETIR","text_bm":"Ribut petir, hujan lebat dan angin kencang dijangka di negeri Kelantan (Tumpat, Kota Bharu dan Bachok) • Terengganu (Kemaman, Dungun dan Hulu Terengganu) sehingga 6:00 Pagi; Ahad, 25 Oktober 2026.","instruction_bm":null}]
            """;

        var storm = WarningParser.Parse(json).Value!.Single();

        Assert.Equal(["KTN", "TRG"], storm.States);
        Assert.True(storm.AffectsState("TRG"));
        Assert.False(storm.AffectsState("SGR"));
        Assert.False(storm.AffectsState(null));
    }

    [Theory]
    [InlineData("Perlis • Kedah • Penang", new[] { "KDH", "PLS", "PNG" })]
    [InlineData("W.P. Kuala Lumpur • W.P. Putrajaya", new[] { "KUL", "PJY" })]
    [InlineData("N. Sembilan dan Melaka", new[] { "MLK", "NSN" })]
    [InlineData("Hulu Selangor", new[] { "SGR" })]
    [InlineData("Perakan Sabahan", new string[0])]
    [InlineData("", new string[0])]
    public void StatesIn_MatchesWholeNamesInEitherLanguage(string text, string[] expected)
    {
        Assert.Equal(expected.Order(), WarningParser.StatesIn(text).Order());
    }

    [Theory]
    [InlineData("Thunderstorms Warning", "Amaran Ribut Petir", WarningKind.Thunderstorm)]
    [InlineData("Strong Winds and Rough Seas Warning", "Amaran Angin Kencang dan Laut Bergelora", WarningKind.WindAndSea)]
    [InlineData("No Advisory", "Tiada Nasihat", WarningKind.NoAdvisory)]
    [InlineData("Continuous Rain Warning", "Amaran Hujan Berterusan", WarningKind.Rain)]
    [InlineData("Tropical Cyclone Advisory", "Nasihat Siklon Tropika", WarningKind.TropicalCyclone)]
    [InlineData("", "Amaran Hujan Lebat", WarningKind.Rain)]
    [InlineData("Earthquake Information", "Maklumat Gempa Bumi", WarningKind.Other)]
    public void KindOf_ReadsTheTitle(string titleEn, string titleMs, WarningKind expected)
    {
        Assert.Equal(expected, WarningParser.KindOf(titleEn, titleMs));
    }

    [Fact]
    public void Warnings_EmptyList_IsAValidAnswer()
    {
        var result = WarningParser.Parse("[]");

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("{}", ParseError.MissingField)]
    [InlineData("<html></html>", ParseError.NotJson)]
    [InlineData("""[{"valid_from":null}]""", ParseError.MissingField)]
    [InlineData("""[{"warning_issue":{"issued":"soon","title_en":"Thunderstorms Warning"}}]""", ParseError.BadValue)]
    [InlineData("""[{"warning_issue":{"issued":"2026-10-25T01:25:00"}}]""", ParseError.MissingField)]
    [InlineData("""[{"warning_issue":{"issued":"2026-10-25T01:25:00","title_en":"Thunderstorms Warning"},"valid_to":"tomorrow"}]""", ParseError.BadValue)]
    [InlineData("""[{"warning_issue":{"issued":"0001-01-01T00:00:00","title_en":"Thunderstorms Warning"}}]""", ParseError.BadValue)]
    public void Warnings_BadInput_FailsWithoutThrowing(string json, ParseError expected)
    {
        Assert.Equal(expected, WarningParser.Parse(json).Error);
    }

    [Fact]
    public void Warnings_TimeWithOffset_IsTakenAsWritten()
    {
        var json = """[{"warning_issue":{"issued":"2026-10-24T17:25:00Z","title_en":"Thunderstorms Warning"}}]""";

        var warning = WarningParser.Parse(json).Value!.Single();

        Assert.Equal(Myt.At(2026, 10, 25, 1, 25), warning.Issued);
        Assert.Equal("Thunderstorms Warning", warning.Title("ms"));
    }

    [Fact]
    public void Warnings_RecordedResponse_SkipsNothing()
    {
        var result = WarningParser.Parse(DataGovFixtures.Warnings);

        Assert.Equal(0, result.Value!.Skipped);
        Assert.Equal(ParseError.None, result.Value.SkipError);
        Assert.Null(result.Value.SkipDetail);
    }

    [Fact]
    public void Warnings_BadEntries_AreSkippedAndTheRestKept()
    {
        var json = """
            [{"warning_issue":{"issued":null,"title_en":"Some New Warning"}},
             {"warning_issue":{"issued":"2026-10-25T01:25:00","title_en":"Thunderstorms Warning"},"valid_to":"2026-10-25T06:00:00","text_en":"Thunderstorms over Selangor."},
             {"warning_issue":{"issued":"2026-10-25T01:25:00","title_en":"Continuous Rain Warning"},"valid_to":"next week"}]
            """;

        var result = WarningParser.Parse(json);

        Assert.True(result.Succeeded);
        var storm = Assert.Single(result.Value);
        Assert.Equal(WarningKind.Thunderstorm, storm.Kind);
        Assert.Equal(["SGR"], storm.States);
        Assert.Equal(2, result.Value.Skipped);
        Assert.Equal(ParseError.MissingField, result.Value.SkipError);
        Assert.Equal("issued", result.Value.SkipDetail);
    }

    [Theory]
    [InlineData("2026-10-25 01:25:00.123")]
    [InlineData("2026-10-25T01:25:00.5")]
    [InlineData("2026-10-25T01:25:00+08:00")]
    [InlineData("2026-10-24T17:25:00.000Z")]
    public void Warnings_TimeFormats_ReadAsTheRightInstant(string issued)
    {
        var json = $$$"""[{"warning_issue":{"issued":"{{{issued}}}","title_en":"Thunderstorms Warning"}}]""";

        var warning = WarningParser.Parse(json).Value!.Single();

        var expected = Myt.At(2026, 10, 25, 1, 25);
        Assert.InRange(warning.Issued, expected, expected.AddSeconds(1));
        Assert.Equal(MalaysiaTime.Offset, warning.Issued.Offset);
    }

    [Theory]
    // Without a Z or an offset these would once have been read as UTC, 8 hours off.
    [InlineData("Sun, 25 Oct 2026 01:25:00")]
    [InlineData("2026-10-25T01:25:00 MYT")]
    [InlineData("10/25/2026 01:25:00 AM")]
    public void Warnings_TimeWithoutAClearOffset_IsRefused(string issued)
    {
        var json = $$$"""[{"warning_issue":{"issued":"{{{issued}}}","title_en":"Thunderstorms Warning"}}]""";

        var result = WarningParser.Parse(json);

        Assert.Equal(ParseError.BadValue, result.Error);
        Assert.Equal("issued", result.Detail);
    }
}
