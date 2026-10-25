using System.Text.Json;

namespace Atap.Core.Malaysia;

/// <summary>
/// Parses the api.waktusolat.app v2 month response, the fallback when e-Solat fails:
/// <c>{"zone":"SGR01","year":2026,"month_number":10,"prayers":[{"day":1,"hijri":"1448-04-19","fajr":1790805180,...}]}</c>.
/// Times are Unix seconds.
/// </summary>
public static class MirrorParser
{
    /// <param name="expectedZone">When set, data for another zone is refused as a mismatch.</param>
    /// <param name="expectedYear">When set, a month of another year is refused as a mismatch.</param>
    /// <param name="expectedMonth">When set, another month is refused as a mismatch.</param>
    public static ParseResult<PrayerSchedule> Parse(string? json, string? expectedZone = null, int? expectedYear = null, int? expectedMonth = null) =>
        MalaysiaJson.Run(json, root => Read(root, expectedZone, expectedYear, expectedMonth));

    private static PrayerSchedule Read(JsonElement root, string? expectedZone, int? expectedYear, int? expectedMonth)
    {
        // Errors come back as {"message":"No data found for zone: ..."}.
        if (root.ValueKind == JsonValueKind.Object
            && !root.TryGetProperty("prayers", out _)
            && MalaysiaJson.OptionalString(root, "message") is { } message)
            throw MalaysiaJson.Fail(ParseError.BadStatus, MalaysiaJson.Clip(message));

        var zone = MalaysiaJson.String(root, "zone");
        if (expectedZone is not null && !string.Equals(zone, expectedZone, StringComparison.OrdinalIgnoreCase))
            throw MalaysiaJson.Fail(ParseError.Mismatch, "zone");

        var year = MalaysiaJson.Int32(root, "year");
        var month = MalaysiaJson.Int32(root, "month_number");
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            throw MalaysiaJson.Fail(ParseError.BadValue, "month_number");
        if ((expectedYear is { } y && y != year) || (expectedMonth is { } m && m != month))
            throw MalaysiaJson.Fail(ParseError.Mismatch, "period");

        var days = new List<PrayerDay>();
        foreach (var entry in MalaysiaJson.Array(root, "prayers").EnumerateArray())
            days.Add(ReadDay(entry, year, month));
        if (days.Count == 0)
            throw MalaysiaJson.Fail(ParseError.Empty, "prayers");

        return new PrayerSchedule(zone.ToUpperInvariant(), PrayerSource.Mirror, days);
    }

    private static PrayerDay ReadDay(JsonElement entry, int year, int month)
    {
        var dayOfMonth = MalaysiaJson.Int32(entry, "day");
        if (dayOfMonth < 1 || dayOfMonth > DateTime.DaysInMonth(year, month))
            throw MalaysiaJson.Fail(ParseError.BadValue, "day");
        var date = new DateOnly(year, month, dayOfMonth);

        HijriDate? hijri = HijriDate.TryParse(MalaysiaJson.OptionalString(entry, "hijri"), out var parsed) ? parsed : null;

        var day = new PrayerDay(
            date,
            hijri,
            Time(entry, "imsak"),
            Time(entry, "fajr"),
            Time(entry, "syuruk"),
            Time(entry, "dhuha"),
            Time(entry, "dhuhr"),
            Time(entry, "asr"),
            Time(entry, "maghrib"),
            Time(entry, "isha"));

        if (!day.IsInOrder)
            throw MalaysiaJson.Fail(ParseError.BadValue, "order");
        // A timestamp from another day means the mirror's numbering is off. Refuse the month.
        if (MalaysiaTime.DateOf(day.Imsak) != date || MalaysiaTime.DateOf(day.Isyak) != date)
            throw MalaysiaJson.Fail(ParseError.Mismatch, "day");
        return day;
    }

    private static DateTimeOffset Time(JsonElement entry, string name)
    {
        var seconds = MalaysiaJson.Int64(entry, name);
        // Accept only 2000 to 2100. Anything else is not a prayer time.
        if (seconds is < 946_684_800 or > 4_102_444_800)
            throw MalaysiaJson.Fail(ParseError.BadValue, name);
        return DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(MalaysiaTime.Offset);
    }
}
