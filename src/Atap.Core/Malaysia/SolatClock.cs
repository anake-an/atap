namespace Atap.Core.Malaysia;

/// <summary>When the waktu solat widget counts down to imsak and berbuka.</summary>
public enum RamadanMode
{
    /// <summary>During Ramadan, from the Hijri month.</summary>
    Auto,

    On,

    Off,
}

/// <summary>What the waktu solat widget shows at one moment.</summary>
/// <param name="Next">The next obligatory prayer, or null when the data does not reach it.</param>
/// <param name="Current">The prayer whose time came most recently.</param>
/// <param name="Today">Today's times, or null when the data is out of date.</param>
/// <param name="IsLive">Close to the next prayer, or a prayer has just begun: the notch shows it.</param>
/// <param name="IsRamadan">Imsak and berbuka countdowns are on.</param>
/// <param name="Fast">During Ramadan, the next imsak or berbuka (Maghrib), else null.</param>
public sealed record SolatNow(PrayerTime? Next, PrayerTime? Current, PrayerDay? Today, bool IsLive, bool IsRamadan, PrayerTime? Fast)
{
    /// <summary>No data for today: the widget says "Data out of date" and gives no reminders.</summary>
    public bool IsStale => Today is null;
}

/// <summary>
/// Works out <see cref="SolatNow"/> from a schedule and the time, and when it next changes, so the widget wakes
/// only at those moments. Pure: the time comes in as a parameter.
/// </summary>
public static class SolatClock
{
    /// <summary>A prayer that has just begun stays live this long.</summary>
    public static readonly TimeSpan JustStarted = TimeSpan.FromMinutes(5);

    /// <summary>The Hijri month of Ramadan.</summary>
    public const int RamadanMonth = 9;

    /// <param name="schedule">The prayer times, or null before any have loaded.</param>
    /// <param name="now">The time.</param>
    /// <param name="liveBefore">How long before a prayer the notch shows it.</param>
    /// <param name="ramadan">When to count down to imsak and berbuka.</param>
    /// <param name="hijriOffset">Days added to the computed Hijri date when e-Solat gives none.</param>
    public static SolatNow At(PrayerSchedule? schedule, DateTimeOffset now, TimeSpan liveBefore, RamadanMode ramadan, int hijriOffset)
    {
        var today = schedule?.DayAt(now);
        if (schedule is null || today is null)
            return new SolatNow(null, null, null, false, false, null);

        var next = schedule.Next(now);
        var current = schedule.Current(now);
        var live = (next is { } n && n.Time - now <= liveBefore)
            || (current is { } c && now - c.Time < JustStarted);

        // The Hijri day begins at Maghrib, so from then on the next fast belongs to tomorrow's date. The evening
        // before 1 Ramadan counts down to the first imsak, and the last evening of Ramadan does not.
        var fastDate = now >= today.Maghrib ? today.Date.AddDays(1) : today.Date;
        var hijri = schedule.DayOf(fastDate)?.Hijri ?? HijriDate.FromGregorian(fastDate, hijriOffset);
        var isRamadan = ramadan switch
        {
            RamadanMode.On => true,
            RamadanMode.Off => false,
            _ => hijri.Month == RamadanMonth,
        };
        PrayerTime? fast = null;
        if (isRamadan)
        {
            fast = now < today.Imsak ? new PrayerTime(Prayer.Imsak, today.Imsak, today.Date)
                : now < today.Maghrib ? new PrayerTime(Prayer.Maghrib, today.Maghrib, today.Date)
                : schedule.Next(now, [Prayer.Imsak]);
        }

        return new SolatNow(next, current, today, live, isRamadan, fast);
    }

    /// <summary>
    /// The next moment the widget's state changes: a prayer begins, its live window opens or closes, or the day
    /// turns (Malaysia time). The widget wakes once for it. Text that counts minutes ticks on its own while shown.
    /// </summary>
    public static DateTimeOffset NextChange(PrayerSchedule? schedule, DateTimeOffset now, TimeSpan liveBefore)
    {
        var tomorrow = MalaysiaTime.At(MalaysiaTime.DateOf(now).AddDays(1), TimeOnly.MinValue);
        var candidates = new List<DateTimeOffset> { tomorrow };
        if (schedule?.Next(now) is { } next)
        {
            candidates.Add(next.Time);
            if (next.Time - liveBefore > now)
                candidates.Add(next.Time - liveBefore);
        }
        if (schedule?.Current(now) is { } current && current.Time + JustStarted > now)
            candidates.Add(current.Time + JustStarted);
        if (schedule?.DayAt(now) is { } today)
        {
            if (today.Imsak > now)
                candidates.Add(today.Imsak);
            if (today.Maghrib > now)
                candidates.Add(today.Maghrib);
        }
        return candidates.Where(t => t > now).Min();
    }
}
