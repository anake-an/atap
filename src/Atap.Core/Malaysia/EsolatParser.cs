using System.Globalization;
using System.Text.Json;

namespace Atap.Core.Malaysia;

/// <summary>
/// Parses the e-Solat TakwimSolat response:
/// <c>{"prayerTime":[{"hijri":"1448-04-19","date":"01-Okt-2026","imsak":"05:43:00",...}],"status":"OK!","zone":"SGR01",...}</c>.
/// Dates use Malay month abbreviations and times are Malaysian wall-clock time.
/// </summary>
public static class EsolatParser
{
    /// <summary>The only status e-Solat sends with real data.</summary>
    public const string OkStatus = "OK!";

    private static readonly Dictionary<string, int> Months = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Jan"] = 1, ["Feb"] = 2, ["Mac"] = 3, ["Mar"] = 3, ["Apr"] = 4, ["Mei"] = 5, ["May"] = 5,
        ["Jun"] = 6, ["Jul"] = 7, ["Ogos"] = 8, ["Ogo"] = 8, ["Aug"] = 8, ["Sep"] = 9, ["Sept"] = 9,
        ["Okt"] = 10, ["Oct"] = 10, ["Nov"] = 11, ["Dis"] = 12, ["Dec"] = 12,
    };

    private static readonly string[] TimeFormats = ["HH:mm:ss", "HH:mm"];

    /// <param name="expectedZone">When set, data for another zone is refused as a mismatch.</param>
    /// <param name="expectedYear">
    /// When set, a day in another year is refused as a mismatch. e-Solat ignores any year and
    /// answers for the current one, so a January asked for in December comes back as this January.
    /// </param>
    /// <param name="expectedMonth">When set, a day in another month is refused as a mismatch.</param>
    public static ParseResult<PrayerSchedule> Parse(string? json, string? expectedZone = null, int? expectedYear = null, int? expectedMonth = null) =>
        MalaysiaJson.Run(json, root => Read(root, expectedZone, expectedYear, expectedMonth));

    /// <summary>Parses "01-Okt-2026". English abbreviations are accepted too.</summary>
    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        var parts = text?.Trim().Split('-');
        if (parts is not { Length: 3 }
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day)
            || !Months.TryGetValue(parts[1], out var month)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            || year is < 2000 or > 2100
            || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateOnly(year, month, day);
        return true;
    }

    /// <summary>Parses "05:43:00" or "05:43" as a 24-hour time.</summary>
    public static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static PrayerSchedule Read(JsonElement root, string? expectedZone, int? expectedYear, int? expectedMonth)
    {
        var status = MalaysiaJson.String(root, "status");
        if (!string.Equals(status, OkStatus, StringComparison.Ordinal))
            throw MalaysiaJson.Fail(ParseError.BadStatus, MalaysiaJson.Clip(status));

        var zone = MalaysiaJson.String(root, "zone");
        if (expectedZone is not null && !string.Equals(zone, expectedZone, StringComparison.OrdinalIgnoreCase))
            throw MalaysiaJson.Fail(ParseError.Mismatch, "zone");

        var days = new List<PrayerDay>();
        foreach (var entry in MalaysiaJson.Array(root, "prayerTime").EnumerateArray())
            days.Add(ReadDay(entry));
        if (days.Count == 0)
            throw MalaysiaJson.Fail(ParseError.Empty, "prayerTime");
        if (days.Any(d => (expectedYear is { } y && d.Date.Year != y) || (expectedMonth is { } m && d.Date.Month != m)))
            throw MalaysiaJson.Fail(ParseError.Mismatch, "period");

        return new PrayerSchedule(zone.ToUpperInvariant(), PrayerSource.Esolat, days);
    }

    private static PrayerDay ReadDay(JsonElement entry)
    {
        var dateText = MalaysiaJson.String(entry, "date");
        if (!TryParseDate(dateText, out var date))
            throw MalaysiaJson.Fail(ParseError.BadValue, "date");

        // The Hijri date is a bonus. Prayer times stay usable without it.
        HijriDate? hijri = HijriDate.TryParse(MalaysiaJson.OptionalString(entry, "hijri"), out var parsed) ? parsed : null;

        var day = new PrayerDay(
            date,
            hijri,
            Time(entry, "imsak", date),
            Time(entry, "fajr", date),
            Time(entry, "syuruk", date),
            Time(entry, "dhuha", date),
            Time(entry, "dhuhr", date),
            Time(entry, "asr", date),
            Time(entry, "maghrib", date),
            Time(entry, "isha", date));
        return day.IsInOrder ? day : throw MalaysiaJson.Fail(ParseError.BadValue, "order");
    }

    private static DateTimeOffset Time(JsonElement entry, string name, DateOnly date) =>
        TryParseTime(MalaysiaJson.String(entry, name), out var time)
            ? MalaysiaTime.At(date, time)
            : throw MalaysiaJson.Fail(ParseError.BadValue, name);
}
