using System.Globalization;
using System.Text;

namespace Atap.Core.Calendar;

/// <summary>
/// One VEVENT. <see cref="Start"/> is wall-clock time in <see cref="Zone"/>, so a weekly 9:00 stays at 9:00.
/// An override of one instance of a recurring event carries <see cref="RecurrenceId"/>.
/// </summary>
public sealed record CalendarEvent
{
    public required string Uid { get; init; }

    public string Summary { get; init; } = "";

    public string? Location { get; init; }

    public string? Description { get; init; }

    public string? Url { get; init; }

    /// <summary>Links from X-GOOGLE-CONFERENCE and the Teams meeting properties.</summary>
    public IReadOnlyList<string> ConferenceUrls { get; init; } = [];

    public required DateTime Start { get; init; }

    public required TimeZoneInfo Zone { get; init; }

    public TimeSpan Duration { get; init; }

    public bool AllDay { get; init; }

    public bool Cancelled { get; init; }

    public RecurrenceRule? Rule { get; init; }

    public IReadOnlyList<DateTimeOffset> ExDates { get; init; } = [];

    public DateTimeOffset? RecurrenceId { get; init; }

    public DateTimeOffset StartOffset => CalendarTime.ToOffset(Start, Zone);
}

/// <summary>
/// Reads an iCalendar (.ics) feed as Google, Outlook and iCloud publish it. Only what a next-meeting view needs:
/// events, their times and zones, RRULE, EXDATE, RECURRENCE-ID and STATUS. Pure, never throws on bad input.
/// </summary>
public static class IcsParser
{
    /// <param name="text">The feed.</param>
    /// <param name="floating">The zone for times without TZID or Z, normally the PC's own.</param>
    public static IReadOnlyList<CalendarEvent> Parse(string text, TimeZoneInfo floating)
    {
        var lines = Unfold(text);
        var customZones = ReadCustomZones(lines);
        var events = new List<CalendarEvent>();
        List<IcsProperty>? current = null;
        var nested = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            if (Is(line, "BEGIN:VEVENT"))
            {
                current = [];
                nested = 0;
                continue;
            }
            if (current is null)
                continue;
            if (line.StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
            {
                nested++;
                continue;
            }
            if (Is(line, "END:VEVENT"))
            {
                if (Build(current, floating, customZones) is { } built)
                    events.Add(built);
                current = null;
                continue;
            }
            if (line.StartsWith("END:", StringComparison.OrdinalIgnoreCase))
            {
                nested = Math.Max(0, nested - 1);
                continue;
            }
            if (nested == 0 && IcsProperty.TryParse(line, out var property))
                current.Add(property);
        }
        return events;
    }

    private static bool Is(string line, string value) => line.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);

    /// <summary>RFC 5545 folds long lines: a line starting with a space or tab continues the one before.</summary>
    internal static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        var builder = new StringBuilder();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                builder.Append(line, 1, line.Length - 1);
                continue;
            }
            if (builder.Length > 0)
                lines.Add(builder.ToString());
            builder.Clear().Append(line);
        }
        if (builder.Length > 0)
            lines.Add(builder.ToString());
        return lines;
    }

    // Outlook can name zones like "(UTC+08:00) Kuala Lumpur, Singapore". Those get a fixed zone from the
    // VTIMEZONE's standard offset when Windows does not know the name.
    private static Dictionary<string, TimeZoneInfo> ReadCustomZones(List<string> lines)
    {
        var zones = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
        string? id = null;
        var inStandard = false;
        foreach (var line in lines)
        {
            if (Is(line, "BEGIN:VTIMEZONE"))
                id = null;
            else if (Is(line, "BEGIN:STANDARD"))
                inStandard = true;
            else if (Is(line, "END:STANDARD"))
                inStandard = false;
            else if (IcsProperty.TryParse(line, out var property))
            {
                if (property.Name == "TZID")
                    id = property.Value.Trim('"');
                else if (inStandard && property.Name == "TZOFFSETTO" && id is not null && !zones.ContainsKey(id) && ParseOffset(property.Value) is { } offset)
                    zones[id] = TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id);
            }
        }
        return zones;
    }

    private static TimeSpan? ParseOffset(string value)
    {
        value = value.Trim();
        if (value.Length < 5 || (value[0] != '+' && value[0] != '-'))
            return null;
        if (!int.TryParse(value.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || !int.TryParse(value.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
            return null;
        var offset = new TimeSpan(hours, minutes, 0);
        return value[0] == '-' ? -offset : offset;
    }

    private static CalendarEvent? Build(List<IcsProperty> properties, TimeZoneInfo floating, Dictionary<string, TimeZoneInfo> customZones)
    {
        IcsProperty? Find(string name) => properties.Find(p => p.Name == name);

        var uid = Find("UID")?.Value;
        if (Find("DTSTART") is not { } startProperty || CalendarTime.Parse(startProperty, floating, customZones) is not { } start)
            return null;

        var duration = TimeSpan.Zero;
        if (Find("DTEND") is { } endProperty && CalendarTime.Parse(endProperty, floating, customZones) is { } end)
            duration = CalendarTime.ToOffset(end.Wall, end.Zone) - CalendarTime.ToOffset(start.Wall, start.Zone);
        else if (Find("DURATION") is { } durationProperty && ParseDuration(durationProperty.Value) is { } parsed)
            duration = parsed;
        else if (start.IsDate)
            duration = TimeSpan.FromDays(1);
        if (duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;

        var exDates = new List<DateTimeOffset>();
        foreach (var ex in properties.Where(p => p.Name == "EXDATE"))
        {
            foreach (var part in ex.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (CalendarTime.Parse(ex with { Value = part }, floating, customZones) is { } date)
                    exDates.Add(CalendarTime.ToOffset(date.Wall, date.Zone));
            }
        }

        DateTimeOffset? recurrenceId = null;
        if (Find("RECURRENCE-ID") is { } idProperty && CalendarTime.Parse(idProperty, floating, customZones) is { } id)
            recurrenceId = CalendarTime.ToOffset(id.Wall, id.Zone);

        string[] conferenceNames = ["X-GOOGLE-CONFERENCE", "X-MICROSOFT-SKYPETEAMSMEETINGURL", "X-MICROSOFT-ONLINEMEETINGURL"];

        return new CalendarEvent
        {
            Uid = string.IsNullOrWhiteSpace(uid) ? Guid.NewGuid().ToString("N") : uid.Trim(),
            Summary = Unescape(Find("SUMMARY")?.Value ?? "").Trim(),
            Location = Find("LOCATION") is { } location ? Unescape(location.Value) : null,
            Description = Find("DESCRIPTION") is { } description ? Unescape(description.Value) : null,
            Url = Find("URL")?.Value.Trim(),
            ConferenceUrls = properties.Where(p => conferenceNames.Contains(p.Name)).Select(p => p.Value.Trim()).ToList(),
            Start = start.Wall,
            Zone = start.Zone,
            Duration = duration,
            AllDay = start.IsDate,
            Cancelled = string.Equals(Find("STATUS")?.Value.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase),
            Rule = Find("RRULE") is { } rule ? RecurrenceRule.Parse(rule.Value, floating) : null,
            ExDates = exDates,
            RecurrenceId = recurrenceId,
        };
    }

    /// <summary>"PT1H30M", "P1D", "P1W". Negative durations come back as their length.</summary>
    internal static TimeSpan? ParseDuration(string value)
    {
        value = value.Trim().TrimStart('+', '-');
        if (value.Length < 2 || value[0] != 'P')
            return null;
        var total = TimeSpan.Zero;
        var number = 0;
        var inTime = false;
        var any = false;
        foreach (var c in value.AsSpan(1))
        {
            if (char.IsAsciiDigit(c))
            {
                number = (number * 10) + (c - '0');
                continue;
            }
            switch (c)
            {
                case 'T':
                    inTime = true;
                    continue;
                case 'W' when !inTime:
                    total += TimeSpan.FromDays(7 * number);
                    break;
                case 'D' when !inTime:
                    total += TimeSpan.FromDays(number);
                    break;
                case 'H' when inTime:
                    total += TimeSpan.FromHours(number);
                    break;
                case 'M' when inTime:
                    total += TimeSpan.FromMinutes(number);
                    break;
                case 'S' when inTime:
                    total += TimeSpan.FromSeconds(number);
                    break;
                default:
                    return null;
            }
            any = true;
            number = 0;
        }
        return any ? total : null;
    }

    internal static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                builder.Append(value[i] switch
                {
                    'n' or 'N' => '\n',
                    _ => value[i],
                });
            }
            else
            {
                builder.Append(value[i]);
            }
        }
        return builder.ToString();
    }
}

/// <summary>NAME;PARAM=value:VALUE. Parameter values may be quoted and hold colons.</summary>
internal sealed record IcsProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value)
{
    public static bool TryParse(string line, out IcsProperty property)
    {
        property = null!;
        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
                quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon <= 0)
            return false;
        var head = line[..colon].Split(';');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in head.Skip(1))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
                parameters[part[..equals].Trim()] = part[(equals + 1)..].Trim().Trim('"');
        }
        property = new IcsProperty(head[0].Trim().ToUpperInvariant(), parameters, line[(colon + 1)..]);
        return true;
    }
}

/// <summary>Turns iCalendar date and time values into wall-clock time in a zone.</summary>
internal static class CalendarTime
{
    public readonly record struct Parsed(DateTime Wall, TimeZoneInfo Zone, bool IsDate);

    public static DateTimeOffset ToOffset(DateTime wall, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    public static Parsed? Parse(IcsProperty property, TimeZoneInfo floating, IReadOnlyDictionary<string, TimeZoneInfo> customZones)
    {
        var value = property.Value.Trim();
        var isDate = value.Length == 8 || (property.Parameters.TryGetValue("VALUE", out var kind) && kind.Equals("DATE", StringComparison.OrdinalIgnoreCase));
        if (isDate)
        {
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? new Parsed(date, floating, true)
                : null;
        }
        var utc = value.EndsWith('Z');
        var text = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(text, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
            return null;
        if (utc)
            return new Parsed(wall, TimeZoneInfo.Utc, false);
        var zone = property.Parameters.TryGetValue("TZID", out var id) ? FindZone(id, customZones) ?? floating : floating;
        return new Parsed(wall, zone, false);
    }

    /// <summary>IANA ("Asia/Kuala_Lumpur") or Windows ("Singapore Standard Time") names, then the feed's own.</summary>
    public static TimeZoneInfo? FindZone(string id, IReadOnlyDictionary<string, TimeZoneInfo> customZones)
    {
        id = id.Trim().Trim('"').TrimStart('/');
        if (id.Length == 0)
            return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return customZones.TryGetValue(id, out var custom) ? custom : null;
        }
    }
}
