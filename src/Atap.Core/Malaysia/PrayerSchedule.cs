namespace Atap.Core.Malaysia;

/// <summary>
/// Prayer times for one zone over a run of days, usually one or two months. Every query takes
/// <c>now</c> as a parameter, so the widget decides when to ask and arms one timer for the answer.
/// </summary>
public sealed class PrayerSchedule
{
    private static readonly Prayer[] DayOrder = Enum.GetValues<Prayer>();

    /// <param name="zone">JAKIM zone code, or null for a schedule calculated outside Malaysia.</param>
    /// <param name="days">Days in any order. When a date repeats, the later entry wins.</param>
    public PrayerSchedule(string? zone, PrayerSource source, IEnumerable<PrayerDay> days)
    {
        Zone = zone;
        Source = source;

        var byDate = new SortedDictionary<DateOnly, PrayerDay>();
        foreach (var day in days)
            byDate[day.Date] = day;
        Days = [.. byDate.Values];
    }

    public string? Zone { get; }

    public PrayerSource Source { get; }

    /// <summary>One entry per date, oldest first.</summary>
    public IReadOnlyList<PrayerDay> Days { get; }

    /// <summary>True for calculated times, which the UI labels approximate.</summary>
    public bool IsApproximate => Source == PrayerSource.Calculated;

    public DateOnly? FirstDate => Days.Count > 0 ? Days[0].Date : null;

    public DateOnly? LastDate => Days.Count > 0 ? Days[^1].Date : null;

    public PrayerDay? DayOf(DateOnly date)
    {
        foreach (var day in Days)
        {
            if (day.Date == date)
                return day;
        }
        return null;
    }

    /// <summary>The day that <paramref name="now"/> falls on, in the schedule's local time.</summary>
    public PrayerDay? DayAt(DateTimeOffset now)
    {
        foreach (var day in Days)
        {
            if (day.LocalDateOf(now) == day.Date)
                return day;
        }
        return null;
    }

    /// <summary>
    /// True when there is no data for today. The UI then shows "Data out of date" and no
    /// reminder may fire from this schedule.
    /// </summary>
    public bool IsStale(DateTimeOffset now) => DayAt(now) is null;

    /// <summary>True when every day of the month is present, for example to skip a prefetch.</summary>
    public bool Covers(int year, int month)
    {
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return false;
        var dates = Days.Select(d => d.Date).ToHashSet();
        var count = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= count; day++)
        {
            if (!dates.Contains(new DateOnly(year, month, day)))
                return false;
        }
        return true;
    }

    /// <summary>The next obligatory prayer strictly after <paramref name="now"/>.</summary>
    /// <remarks>At the exact time of a prayer, that prayer is current and the next one is returned.</remarks>
    public PrayerTime? Next(DateTimeOffset now) => Next(now, Prayers.Obligatory);

    /// <summary>
    /// The first of <paramref name="prayers"/> strictly after <paramref name="now"/>, looking at
    /// today and tomorrow only. Null when that data is missing, so a gap in the data never
    /// produces a countdown to a day further ahead.
    /// </summary>
    public PrayerTime? Next(DateTimeOffset now, IReadOnlyCollection<Prayer> prayers)
    {
        foreach (var day in Days)
        {
            var today = day.LocalDateOf(now);
            if (day.Date < today || day.Date > today.AddDays(1))
                continue;

            foreach (var prayer in DayOrder)
            {
                if (!prayers.Contains(prayer))
                    continue;
                var time = day.TimeOf(prayer);
                if (time > now)
                    return new PrayerTime(prayer, time, day.Date);
            }
        }
        return null;
    }

    /// <summary>
    /// The obligatory prayer whose time has come most recently, at or before <paramref name="now"/>.
    /// Before Subuh this is yesterday's Isyak. Null when that data is missing.
    /// </summary>
    public PrayerTime? Current(DateTimeOffset now)
    {
        PrayerTime? latest = null;
        foreach (var day in Days)
        {
            var today = day.LocalDateOf(now);
            if (day.Date < today.AddDays(-1) || day.Date > today)
                continue;

            foreach (var prayer in Prayers.Obligatory)
            {
                var time = day.TimeOf(prayer);
                if (time <= now)
                    latest = new PrayerTime(prayer, time, day.Date);
            }
        }
        return latest;
    }

    /// <summary>
    /// This schedule with the days of <paramref name="newer"/> added. Days in both come from
    /// <paramref name="newer"/>, and so does the source.
    /// </summary>
    public PrayerSchedule Merge(PrayerSchedule newer)
    {
        ArgumentNullException.ThrowIfNull(newer);
        if (!string.Equals(Zone, newer.Zone, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Schedules for different zones cannot be merged.", nameof(newer));
        return new PrayerSchedule(newer.Zone, newer.Source, Days.Concat(newer.Days));
    }

    /// <summary>A copy without days before <paramref name="date"/>, to keep the cache small.</summary>
    public PrayerSchedule DropBefore(DateOnly date) =>
        new(Zone, Source, Days.Where(d => d.Date >= date));
}
