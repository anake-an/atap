using System.Globalization;
using System.Text.Json;

namespace Atap.Core.Malaysia;

/// <summary>What a MET Malaysia forecast phrase describes. The widget maps these to its own strings.</summary>
public enum WeatherCondition
{
    /// <summary>A phrase this build does not know. Show the original text.</summary>
    Unknown,
    NoRain,
    Fair,
    Cloudy,
    Hazy,
    Windy,
    Rain,
    Thunderstorm,
}

/// <summary>How much of the area the condition covers.</summary>
public enum WeatherExtent
{
    /// <summary>No qualifier, as in "Hujan".</summary>
    None,

    /// <summary>"di satu dua tempat"</summary>
    Isolated,

    /// <summary>"di beberapa tempat"</summary>
    Scattered,

    /// <summary>"di kebanyakan tempat"</summary>
    Widespread,

    /// <summary>"menyeluruh"</summary>
    General,
}

/// <summary>Where in the location the condition applies.</summary>
public enum WeatherArea
{
    Everywhere,

    /// <summary>"di kawasan pantai"</summary>
    Coastal,

    /// <summary>"di kawasan pedalaman"</summary>
    Inland,
}

/// <summary>Parts of the day in a forecast summary.</summary>
[Flags]
public enum ForecastPeriods
{
    None = 0,
    Morning = 1,
    Afternoon = 2,
    Night = 4,
    AllDay = Morning | Afternoon | Night,
}

/// <summary>A forecast phrase, kept as sent (always Bahasa Melayu) and broken into parts.</summary>
/// <param name="Text">The original phrase, for example "Ribut petir di beberapa tempat".</param>
public sealed record ForecastPhrase(string Text, WeatherCondition Condition, WeatherExtent Extent, WeatherArea Area)
{
    private static readonly (string Phrase, WeatherCondition Condition)[] Conditions =
    [
        ("tiada hujan", WeatherCondition.NoRain),
        ("ribut petir", WeatherCondition.Thunderstorm),
        ("hujan", WeatherCondition.Rain),
        ("mendung", WeatherCondition.Cloudy),
        ("berjerebu", WeatherCondition.Hazy),
        ("berangin", WeatherCondition.Windy),
        ("cerah", WeatherCondition.Fair),
    ];

    private static readonly (string Phrase, WeatherExtent Extent)[] Extents =
    [
        ("di satu dua tempat", WeatherExtent.Isolated),
        ("di beberapa tempat", WeatherExtent.Scattered),
        ("di kebanyakan tempat", WeatherExtent.Widespread),
        ("menyeluruh", WeatherExtent.General),
    ];

    public static ForecastPhrase Parse(string? text)
    {
        var original = text?.Trim() ?? string.Empty;
        var normal = string.Join(' ', original.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        var condition = WeatherCondition.Unknown;
        foreach (var (phrase, value) in Conditions)
        {
            if (normal.StartsWith(phrase, StringComparison.Ordinal))
            {
                condition = value;
                break;
            }
        }

        var extent = WeatherExtent.None;
        foreach (var (phrase, value) in Extents)
        {
            if (normal.Contains(phrase, StringComparison.Ordinal))
            {
                extent = value;
                break;
            }
        }

        var area = normal.Contains("di kawasan pantai", StringComparison.Ordinal) ? WeatherArea.Coastal
            : normal.Contains("di kawasan pedalaman", StringComparison.Ordinal) ? WeatherArea.Inland
            : WeatherArea.Everywhere;

        return new ForecastPhrase(original, condition, extent, area);
    }

    /// <summary>True when the phrase speaks of rain or thunderstorms anywhere.</summary>
    public bool IsWet => Condition is WeatherCondition.Rain or WeatherCondition.Thunderstorm;
}

/// <summary>One day of forecast for one location.</summary>
public sealed record ForecastDay(
    string LocationId,
    string LocationName,
    DateOnly Date,
    ForecastPhrase Morning,
    ForecastPhrase Afternoon,
    ForecastPhrase Night,
    ForecastPhrase Summary,
    ForecastPeriods SummaryWhen,
    int? MinTemperature,
    int? MaxTemperature);

/// <summary>A data.gov.my forecast response, sorted by location and date.</summary>
public sealed class WeatherForecast
{
    public WeatherForecast(IEnumerable<ForecastDay> days)
    {
        Days = [.. days.OrderBy(d => d.LocationId, StringComparer.Ordinal).ThenBy(d => d.Date)];
    }

    public IReadOnlyList<ForecastDay> Days { get; }

    /// <summary>Distinct locations in the response, for a location picker.</summary>
    public IReadOnlyList<(string Id, string Name)> Locations =>
        Days.Select(d => (d.LocationId, d.LocationName)).Distinct().ToList();

    public IReadOnlyList<ForecastDay> ForLocation(string locationId) =>
        Days.Where(d => string.Equals(d.LocationId, locationId, StringComparison.OrdinalIgnoreCase)).ToList();

    public ForecastDay? DayOf(string locationId, DateOnly date) =>
        Days.FirstOrDefault(d => d.Date == date && string.Equals(d.LocationId, locationId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Parses <c>https://api.data.gov.my/weather/forecast/</c>, an array of
/// <c>{"location":{"location_id":"Ds055","location_name":"Gombak"},"date":"2026-10-25","morning_forecast":"...",...}</c>.
/// Forecast text is always in Bahasa Melayu.
/// </summary>
public static class ForecastParser
{
    public static ParseResult<WeatherForecast> Parse(string? json) => MalaysiaJson.Run(json, Read);

    /// <summary>Reads "Pagi", "Petang dan Malam", "Sepanjang Hari" and the like.</summary>
    public static ForecastPeriods ParsePeriods(string? text)
    {
        var normal = text?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normal == "sepanjang hari")
            return ForecastPeriods.AllDay;

        var periods = ForecastPeriods.None;
        foreach (var part in normal.Split(" dan ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            periods |= part switch
            {
                "pagi" => ForecastPeriods.Morning,
                "petang" => ForecastPeriods.Afternoon,
                "malam" => ForecastPeriods.Night,
                _ => ForecastPeriods.None,
            };
        }
        return periods;
    }

    private static WeatherForecast Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw MalaysiaJson.Fail(ParseError.MissingField, "[]");

        var days = new List<ForecastDay>();
        foreach (var entry in root.EnumerateArray())
            days.Add(ReadDay(entry));
        if (days.Count == 0)
            throw MalaysiaJson.Fail(ParseError.Empty);
        return new WeatherForecast(days);
    }

    private static ForecastDay ReadDay(JsonElement entry)
    {
        var location = MalaysiaJson.Object(entry, "location");
        var id = MalaysiaJson.String(location, "location_id");
        if (id.Length == 0)
            throw MalaysiaJson.Fail(ParseError.BadValue, "location_id");
        var name = MalaysiaJson.OptionalString(location, "location_name")?.Trim() ?? id;

        if (!DateOnly.TryParseExact(MalaysiaJson.String(entry, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw MalaysiaJson.Fail(ParseError.BadValue, "date");

        return new ForecastDay(
            id,
            name,
            date,
            ForecastPhrase.Parse(MalaysiaJson.OptionalString(entry, "morning_forecast")),
            ForecastPhrase.Parse(MalaysiaJson.OptionalString(entry, "afternoon_forecast")),
            ForecastPhrase.Parse(MalaysiaJson.OptionalString(entry, "night_forecast")),
            ForecastPhrase.Parse(MalaysiaJson.OptionalString(entry, "summary_forecast")),
            ParsePeriods(MalaysiaJson.OptionalString(entry, "summary_when")),
            MalaysiaJson.OptionalInt32(entry, "min_temp"),
            MalaysiaJson.OptionalInt32(entry, "max_temp"));
    }
}
