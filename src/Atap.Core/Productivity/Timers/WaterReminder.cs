using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Timers;

/// <summary>Interval, active hours and goal for the water reminder, read from the widget settings.</summary>
public sealed record WaterSettings
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxInterval = TimeSpan.FromHours(4);

    public TimeSpan Interval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Start of active hours. Equal to <see cref="ActiveUntil"/> means all day.</summary>
    public TimeOnly ActiveFrom { get; init; } = new(8, 0);

    /// <summary>End of active hours, not included. Earlier than the start means the hours run past midnight.</summary>
    public TimeOnly ActiveUntil { get; init; } = new(22, 0);

    /// <summary>Glasses a day.</summary>
    public int DailyGoal { get; init; } = 8;

    public bool AllDay => ActiveFrom == ActiveUntil;

    /// <summary>Interval between 15 minutes and 4 hours, goal between 1 and 30.</summary>
    public WaterSettings Clamped() => this with
    {
        Interval = Interval < MinInterval ? MinInterval : Interval > MaxInterval ? MaxInterval : Interval,
        DailyGoal = Math.Clamp(DailyGoal, 1, 30),
    };

    public bool IsActiveAt(TimeOnly time) =>
        AllDay
        || (ActiveFrom < ActiveUntil
            ? time >= ActiveFrom && time < ActiveUntil
            : time >= ActiveFrom || time < ActiveUntil);

    /// <summary>The day a local time counts toward. Hours past midnight count toward the day they started.</summary>
    public DateOnly ActiveDayOf(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        return !AllDay && ActiveFrom > ActiveUntil && TimeOnly.FromDateTime(local) < ActiveUntil
            ? date.AddDays(-1)
            : date;
    }
}

/// <param name="State">The state to save. Its anchor is now when the old reminder was skipped.</param>
/// <param name="Due">When the reminder shows. <see cref="WaterState.IsDueAt"/> on <paramref name="State"/> is true then.</param>
public readonly record struct WaterDue(WaterState State, DateTimeOffset Due);

/// <summary>
/// Saved state of a water reminder, kept in machine state. Reminders come one interval after the
/// last drink, reminder or restart, and only inside active hours. Methods never read the clock.
/// </summary>
public sealed record WaterState
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxCount = 99;

    /// <summary>
    /// How late a reminder can be handled and still show. A later one (after a restart, sleep or a
    /// late callback on resume) is skipped.
    /// </summary>
    public static readonly TimeSpan OnTimeGrace = TimeSpan.FromMinutes(1);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Last drink, reminder or restart. The next reminder is one interval after it.</summary>
    public DateTimeOffset? Anchor { get; set; }

    /// <summary>The active day <see cref="Count"/> belongs to.</summary>
    public DateOnly? Day { get; set; }

    public int Count { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Glasses so far in the active day at <paramref name="now"/>. A new day starts at zero.</summary>
    public int CountAt(DateTimeOffset now, WaterSettings settings, TimeZoneInfo zone) =>
        Day == settings.ActiveDayOf(WallClock.ToLocal(now, zone)) ? Math.Clamp(Count, 0, MaxCount) : 0;

    /// <summary>Counts a glass and starts the interval again.</summary>
    public WaterState Drink(DateTimeOffset now, WaterSettings settings, TimeZoneInfo zone) => this with
    {
        Day = settings.ActiveDayOf(WallClock.ToLocal(now, zone)),
        Count = Math.Min(CountAt(now, settings, zone) + 1, MaxCount),
        Anchor = now,
    };

    /// <summary>Takes back the last glass of the day. The interval is left as it is.</summary>
    public WaterState UndoDrink(DateTimeOffset now, WaterSettings settings, TimeZoneInfo zone)
    {
        var count = CountAt(now, settings, zone);
        return count == 0 ? this : this with { Count = count - 1 };
    }

    /// <summary>Records that a reminder showed at <paramref name="at"/>.</summary>
    public WaterState Reminded(DateTimeOffset at) => this with { Anchor = at };

    /// <summary>Starts the interval again, for example when the session is unlocked.</summary>
    public WaterState Restart(DateTimeOffset now) => this with { Anchor = now };

    /// <summary>
    /// The reminder time set by <see cref="Anchor"/>, one interval after it and moved into active
    /// hours. It can be in the past. Null when there is no anchor or the anchor is after
    /// <paramref name="now"/>, for example after the clock was set backwards.
    /// </summary>
    public DateTimeOffset? ReminderAt(WaterSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Anchor is not { } saved || saved > now)
            return null;
        settings = settings.Clamped();
        return IntoActiveHours(settings, saved + settings.Interval, zone);
    }

    /// <summary>
    /// True when a reminder should show at <paramref name="now"/>: its time came no more than
    /// <see cref="OnTimeGrace"/> ago. Show it, then call <see cref="Reminded"/>. When this is false
    /// and <see cref="ReminderAt"/> has passed, the reminder came due while Atap was closed, asleep
    /// or paused. <see cref="NextReminder"/> skips it.
    /// </summary>
    public bool IsDueAt(WaterSettings settings, DateTimeOffset now, TimeZoneInfo zone) =>
        ReminderAt(settings, now, zone) is { } at && at <= now && now - at <= OnTimeGrace;

    /// <summary>
    /// When the next reminder shows, always after <paramref name="now"/>, and the state to keep.
    /// Save that state and arm the time. <see cref="IsDueAt"/> on that state is true at that time.
    /// Check <see cref="IsDueAt"/> first, since at the reminder time itself this gives the one after
    /// it. A reminder that came due while Atap was closed or paused is skipped and the interval
    /// starts again from now. So does an anchor after now, for example after the clock was set
    /// backwards. Outside active hours the reminder moves to one interval after active hours start,
    /// or to the start itself when active hours are shorter than the interval.
    /// </summary>
    public WaterDue NextReminder(WaterSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (ReminderAt(settings, now, zone) is { } at && at > now)
            return new WaterDue(this, at);
        var restarted = Restart(now);
        settings = settings.Clamped();
        return new WaterDue(restarted, IntoActiveHours(settings, now + settings.Interval, zone));
    }

    private static DateTimeOffset IntoActiveHours(WaterSettings settings, DateTimeOffset due, TimeZoneInfo zone)
    {
        if (settings.AllDay)
            return due;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (settings.IsActiveAt(TimeOnly.FromDateTime(WallClock.ToLocal(due, zone))))
                return due;

            var start = NextStart(settings, due, zone);
            var end = EndOf(settings, start, zone);
            due = start + settings.Interval < end ? start + settings.Interval : start;
        }
        return due;
    }

    private static DateTimeOffset NextStart(WaterSettings settings, DateTimeOffset after, TimeZoneInfo zone)
    {
        var date = WallClock.DateOf(after, zone);
        for (var day = 0; day <= 2; day++)
        {
            var start = WallClock.ToInstant(date.AddDays(day), settings.ActiveFrom, zone);
            if (start > after)
                return start;
        }
        return WallClock.ToInstant(date.AddDays(3), settings.ActiveFrom, zone);
    }

    private static DateTimeOffset EndOf(WaterSettings settings, DateTimeOffset start, TimeZoneInfo zone)
    {
        var date = WallClock.DateOf(start, zone);
        var endDate = settings.ActiveFrom < settings.ActiveUntil ? date : date.AddDays(1);
        return WallClock.ToInstant(endDate, settings.ActiveUntil, zone);
    }
}
