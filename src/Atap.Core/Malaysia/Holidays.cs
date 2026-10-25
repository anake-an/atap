using System.Globalization;
using System.Text.Json;

namespace Atap.Core.Malaysia;

/// <summary>A public holiday.</summary>
/// <param name="States">State codes that observe it. Empty means every state.</param>
/// <param name="Tentative">True when the date depends on moon sighting and may still move.</param>
public sealed record Holiday(DateOnly Date, string NameEn, string NameMs, IReadOnlyList<string> States, bool Tentative)
{
    public bool IsNationwide => States.Count == 0;

    /// <summary>The name for a UI language ("en" or "ms"). Anything else gets English.</summary>
    public string Name(string language) =>
        string.Equals(language, "ms", StringComparison.OrdinalIgnoreCase) ? NameMs : NameEn;

    /// <summary>
    /// True when the holiday is observed in <paramref name="stateCode"/>. With no state picked,
    /// only nationwide holidays apply.
    /// </summary>
    public bool AppliesTo(string? stateCode) =>
        IsNationwide
        || (MalaysiaStates.Find(stateCode) is { } state && States.Contains(state.Code, StringComparer.Ordinal));
}

/// <summary>The holidays of one bundled year file, sorted by date.</summary>
public sealed record HolidayYear(int Year, IReadOnlyList<Holiday> Holidays);

/// <summary>
/// Parses a bundled <c>holidays-&lt;year&gt;.json</c> file. The format, schema 1:
/// <code>
/// {
///   "schema": 1,
///   "year": 2026,
///   "holidays": [
///     { "date": "2026-11-08", "name": { "en": "Deepavali", "ms": "Deepavali" }, "states": ["JHR", "SGR"], "tentative": false }
///   ]
/// }
/// </code>
/// "states" may be left out (or null) for a nationwide holiday. An empty list is refused.
/// "tentative" may be left out when false. Every state code must be one of the 16 in
/// <see cref="MalaysiaStates"/>.
/// </summary>
public static class HolidayParser
{
    public const int Schema = 1;

    public static ParseResult<HolidayYear> Parse(string? json) => MalaysiaJson.Run(json, Read);

    private static HolidayYear Read(JsonElement root)
    {
        var schema = MalaysiaJson.Int32(root, "schema");
        if (schema != Schema)
            throw MalaysiaJson.Fail(ParseError.UnsupportedSchema, schema.ToString(CultureInfo.InvariantCulture));

        var year = MalaysiaJson.Int32(root, "year");
        if (year is < 2000 or > 2100)
            throw MalaysiaJson.Fail(ParseError.BadValue, "year");

        var holidays = new List<Holiday>();
        foreach (var entry in MalaysiaJson.Array(root, "holidays").EnumerateArray())
            holidays.Add(ReadHoliday(entry, year));
        if (holidays.Count == 0)
            throw MalaysiaJson.Fail(ParseError.Empty, "holidays");

        // A stable sort keeps holidays on the same date in file order.
        return new HolidayYear(year, [.. holidays.OrderBy(h => h.Date)]);
    }

    private static Holiday ReadHoliday(JsonElement entry, int year)
    {
        var dateText = MalaysiaJson.String(entry, "date");
        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw MalaysiaJson.Fail(ParseError.BadValue, "date");
        if (date.Year != year)
            throw MalaysiaJson.Fail(ParseError.Mismatch, dateText);

        var name = MalaysiaJson.Object(entry, "name");
        var nameEn = MalaysiaJson.String(name, "en").Trim();
        var nameMs = MalaysiaJson.String(name, "ms").Trim();
        if (nameEn.Length == 0 || nameMs.Length == 0)
            throw MalaysiaJson.Fail(ParseError.BadValue, "name");

        return new Holiday(date, nameEn, nameMs, ReadStates(entry), ReadTentative(entry));
    }

    private static IReadOnlyList<string> ReadStates(JsonElement entry)
    {
        if (!entry.TryGetProperty("states", out var states) || states.ValueKind == JsonValueKind.Null)
            return [];
        if (states.ValueKind != JsonValueKind.Array)
            throw MalaysiaJson.Fail(ParseError.MissingField, "states");
        // Only a missing field means nationwide. An empty list is likely a slip that would
        // show a state holiday everywhere.
        if (states.GetArrayLength() == 0)
            throw MalaysiaJson.Fail(ParseError.BadValue, "states");

        var codes = new List<string>();
        foreach (var item in states.EnumerateArray())
        {
            var text = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            var state = MalaysiaStates.Find(text) ?? throw MalaysiaJson.Fail(ParseError.BadValue, text ?? "states");
            if (!codes.Contains(state.Code))
                codes.Add(state.Code);
        }

        // Listing all 16 states is the same as nationwide.
        return codes.Count == MalaysiaStates.All.Count ? [] : codes;
    }

    private static bool ReadTentative(JsonElement entry)
    {
        if (!entry.TryGetProperty("tentative", out var value))
            return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False or JsonValueKind.Null => false,
            _ => throw MalaysiaJson.Fail(ParseError.BadValue, "tentative"),
        };
    }
}

public enum HolidayLookupStatus
{
    /// <summary>A holiday was found.</summary>
    Found,

    /// <summary>The bundled data ends before the next holiday. The UI asks the user to update Atap.</summary>
    DataEnds,

    /// <summary>No data covers the date at all, for example a clock set years back.</summary>
    NoData,
}

public sealed record HolidayLookup(HolidayLookupStatus Status, Holiday? Holiday);

/// <summary>The bundled holiday years, queried by date and state.</summary>
public sealed class HolidayCalendar
{
    private readonly SortedDictionary<int, HolidayYear> _years = [];

    /// <exception cref="ArgumentException">Two files are for the same year.</exception>
    public HolidayCalendar(IEnumerable<HolidayYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        foreach (var year in years)
        {
            if (!_years.TryAdd(year.Year, year))
                throw new ArgumentException($"Holiday data for {year.Year} was given twice.", nameof(years));
        }
    }

    public IReadOnlyCollection<int> Years => _years.Keys;

    public int? FirstYear => _years.Count > 0 ? _years.Keys.First() : null;

    public int? LastYear => _years.Count > 0 ? _years.Keys.Last() : null;

    /// <summary>True when <paramref name="date"/> is after the last bundled year.</summary>
    public bool IsPastData(DateOnly date) => LastYear is not { } last || date.Year > last;

    /// <summary>
    /// The first holiday on or after <paramref name="today"/> in <paramref name="stateCode"/>.
    /// The search stops at the first year with no data, so a missing year never skips ahead.
    /// </summary>
    public HolidayLookup Next(DateOnly today, string? stateCode)
    {
        var first = Upcoming(today, stateCode, 1, out var status);
        return new HolidayLookup(status, first.Count > 0 ? first[0] : null);
    }

    /// <summary>Up to <paramref name="count"/> holidays on or after <paramref name="today"/>.</summary>
    public IReadOnlyList<Holiday> Upcoming(DateOnly today, string? stateCode, int count) =>
        count > 0 ? Upcoming(today, stateCode, count, out _) : [];

    /// <summary>Holidays on <paramref name="date"/> in <paramref name="stateCode"/>.</summary>
    public IReadOnlyList<Holiday> On(DateOnly date, string? stateCode) =>
        _years.TryGetValue(date.Year, out var year)
            ? year.Holidays.Where(h => h.Date == date && h.AppliesTo(stateCode)).ToList()
            : [];

    private List<Holiday> Upcoming(DateOnly today, string? stateCode, int count, out HolidayLookupStatus status)
    {
        var found = new List<Holiday>();
        if (FirstYear is not { } firstYear || today.Year < firstYear)
        {
            status = HolidayLookupStatus.NoData;
            return found;
        }

        for (var y = today.Year; found.Count < count; y++)
        {
            if (!_years.TryGetValue(y, out var year))
            {
                status = found.Count > 0 ? HolidayLookupStatus.Found : HolidayLookupStatus.DataEnds;
                return found;
            }

            foreach (var holiday in year.Holidays)
            {
                if (holiday.Date >= today && holiday.AppliesTo(stateCode))
                {
                    found.Add(holiday);
                    if (found.Count == count)
                        break;
                }
            }
        }

        status = HolidayLookupStatus.Found;
        return found;
    }
}
