using System.Globalization;

namespace Atap.Core.Calendar;

public enum Frequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>A BYDAY entry: MO, or 2TU for the second Tuesday, or -1FR for the last Friday.</summary>
public readonly record struct WeekdayRule(int Ordinal, DayOfWeek Day);

/// <summary>
/// The parts of RRULE that calendars use for meetings: FREQ, INTERVAL, COUNT, UNTIL, BYDAY, BYMONTHDAY and BYMONTH.
/// A rule with other parts (BYSETPOS, hourly) is marked unsupported and only its first instance is shown.
/// </summary>
public sealed record RecurrenceRule
{
    private const int MaxPeriods = 20_000;

    public Frequency Frequency { get; init; }

    public int Interval { get; init; } = 1;

    public int? Count { get; init; }

    public DateTimeOffset? Until { get; init; }

    public IReadOnlyList<WeekdayRule> ByDay { get; init; } = [];

    public IReadOnlyList<int> ByMonthDay { get; init; } = [];

    public IReadOnlyList<int> ByMonth { get; init; } = [];

    public bool Unsupported { get; init; }

    public static RecurrenceRule? Parse(string value, TimeZoneInfo floating)
    {
        Frequency? frequency = null;
        var rule = new RecurrenceRule();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
                continue;
            var name = part[..equals].ToUpperInvariant();
            var text = part[(equals + 1)..];
            switch (name)
            {
                case "FREQ":
                    frequency = text.ToUpperInvariant() switch
                    {
                        "DAILY" => Frequency.Daily,
                        "WEEKLY" => Frequency.Weekly,
                        "MONTHLY" => Frequency.Monthly,
                        "YEARLY" => Frequency.Yearly,
                        _ => null,
                    };
                    if (frequency is null)
                        rule = rule with { Unsupported = true };
                    break;
                case "INTERVAL" when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var interval) && interval > 0:
                    rule = rule with { Interval = interval };
                    break;
                case "COUNT" when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0:
                    rule = rule with { Count = count };
                    break;
                case "UNTIL":
                    if (CalendarTime.Parse(new IcsProperty("UNTIL", new Dictionary<string, string>(), text), floating, new Dictionary<string, TimeZoneInfo>()) is { } until)
                    {
                        // A date-only UNTIL includes that whole day.
                        var end = until.IsDate ? until.Wall.AddDays(1).AddTicks(-1) : until.Wall;
                        rule = rule with { Until = CalendarTime.ToOffset(end, until.Zone) };
                    }
                    break;
                case "BYDAY":
                    var days = new List<WeekdayRule>();
                    foreach (var day in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (ParseWeekday(day) is { } parsed)
                            days.Add(parsed);
                        else
                            rule = rule with { Unsupported = true };
                    }
                    rule = rule with { ByDay = days };
                    break;
                case "BYMONTHDAY":
                    rule = rule with { ByMonthDay = Numbers(text) };
                    break;
                case "BYMONTH":
                    rule = rule with { ByMonth = Numbers(text) };
                    break;
                case "WKST":
                    break;
                default:
                    rule = rule with { Unsupported = true };
                    break;
            }
        }
        return frequency is { } f ? rule with { Frequency = f } : null;
    }

    /// <summary>
    /// Instance starts, as wall-clock time in the event's zone, in order, from <paramref name="start"/> until one
    /// is past <paramref name="to"/>. COUNT counts from the first instance, so earlier ones are walked too.
    /// </summary>
    public IEnumerable<DateTime> Expand(DateTime start, TimeZoneInfo zone, DateTimeOffset to)
    {
        if (Unsupported)
        {
            yield return start;
            yield break;
        }
        var produced = 0;
        for (var period = 0; period < MaxPeriods; period++)
        {
            var any = false;
            foreach (var candidate in Candidates(start, period))
            {
                any = true;
                if (candidate < start)
                    continue;
                var offset = CalendarTime.ToOffset(candidate, zone);
                if (Until is { } until && offset > until)
                    yield break;
                if (offset > to)
                    yield break;
                yield return candidate;
                produced++;
                if (Count is { } count && produced >= count)
                    yield break;
            }
            if (!any && PeriodStart(start, period) is { } periodStart && CalendarTime.ToOffset(periodStart, zone) > to)
                yield break;
        }
    }

    private DateTime? PeriodStart(DateTime start, int period) => Frequency switch
    {
        Frequency.Daily => start.AddDays((double)period * Interval),
        Frequency.Weekly => start.AddDays(7.0 * period * Interval),
        Frequency.Monthly => start.AddMonths(period * Interval),
        _ => start.Year + (period * Interval) <= 9998 ? start.AddYears(period * Interval) : null,
    };

    private List<DateTime> Candidates(DateTime start, int period)
    {
        var time = start.TimeOfDay;
        var list = new List<DateTime>();
        switch (Frequency)
        {
            case Frequency.Daily:
            {
                var day = start.Date.AddDays((double)period * Interval);
                if (MatchesFilters(day))
                    list.Add(day + time);
                break;
            }
            case Frequency.Weekly:
            {
                // Weeks start on Monday, the RFC default.
                var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7)).AddDays(7.0 * period * Interval);
                IEnumerable<DayOfWeek> days = ByDay.Count > 0 ? ByDay.Select(d => d.Day) : [start.DayOfWeek];
                foreach (var day in days.Distinct().OrderBy(d => ((int)d + 6) % 7))
                {
                    var date = weekStart.AddDays(((int)day + 6) % 7);
                    if (ByMonth.Count == 0 || ByMonth.Contains(date.Month))
                        list.Add(date + time);
                }
                break;
            }
            case Frequency.Monthly:
            {
                var month = new DateTime(start.Year, start.Month, 1).AddMonths(period * Interval);
                if (ByMonth.Count == 0 || ByMonth.Contains(month.Month))
                    list.AddRange(DaysInMonth(month, start).Select(d => d + time));
                break;
            }
            case Frequency.Yearly:
            {
                var year = start.Year + (period * Interval);
                if (year > 9998)
                    break;
                IEnumerable<int> months = ByMonth.Count > 0 ? ByMonth.Where(m => m is >= 1 and <= 12).Order() : [start.Month];
                foreach (var m in months)
                {
                    var month = new DateTime(year, m, 1);
                    if (ByDay.Count == 0 && ByMonthDay.Count == 0)
                    {
                        if (start.Day <= DateTime.DaysInMonth(year, m))
                            list.Add(new DateTime(year, m, start.Day) + time);
                    }
                    else
                    {
                        list.AddRange(DaysInMonth(month, start).Select(d => d + time));
                    }
                }
                break;
            }
        }
        return list;
    }

    private IEnumerable<DateTime> DaysInMonth(DateTime month, DateTime start)
    {
        var length = DateTime.DaysInMonth(month.Year, month.Month);
        var days = new SortedSet<int>();
        if (ByMonthDay.Count > 0)
        {
            foreach (var d in ByMonthDay)
            {
                var day = d > 0 ? d : length + d + 1;
                if (day >= 1 && day <= length)
                    days.Add(day);
            }
        }
        else if (ByDay.Count > 0)
        {
            foreach (var rule in ByDay)
            {
                var matches = Enumerable.Range(1, length).Where(d => month.AddDays(d - 1).DayOfWeek == rule.Day).ToList();
                if (rule.Ordinal == 0)
                    days.UnionWith(matches);
                else if (rule.Ordinal > 0 && rule.Ordinal <= matches.Count)
                    days.Add(matches[rule.Ordinal - 1]);
                else if (rule.Ordinal < 0 && -rule.Ordinal <= matches.Count)
                    days.Add(matches[matches.Count + rule.Ordinal]);
            }
        }
        else if (start.Day <= length)
        {
            days.Add(start.Day);
        }
        return days.Select(d => month.AddDays(d - 1));
    }

    private bool MatchesFilters(DateTime day) =>
        (ByMonth.Count == 0 || ByMonth.Contains(day.Month))
        && (ByMonthDay.Count == 0 || ByMonthDay.Contains(day.Day) || ByMonthDay.Contains(day.Day - DateTime.DaysInMonth(day.Year, day.Month) - 1))
        && (ByDay.Count == 0 || ByDay.Any(d => d.Day == day.DayOfWeek));

    private static WeekdayRule? ParseWeekday(string text)
    {
        if (text.Length < 2)
            return null;
        DayOfWeek? day = text[^2..].ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => null,
        };
        if (day is null)
            return null;
        var ordinal = 0;
        if (text.Length > 2 && !int.TryParse(text[..^2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal))
            return null;
        return new WeekdayRule(ordinal, day.Value);
    }

    private static List<int> Numbers(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => int.TryParse(n, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(v => v != 0)
            .ToList();
}
