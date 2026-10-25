using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Timers;

/// <summary>Days an alarm repeats on. None means it rings once.</summary>
[Flags]
[JsonConverter(typeof(CamelCaseEnumConverter<AlarmDays>))]
public enum AlarmDays
{
    None = 0,
    Sunday = 1 << (int)DayOfWeek.Sunday,
    Monday = 1 << (int)DayOfWeek.Monday,
    Tuesday = 1 << (int)DayOfWeek.Tuesday,
    Wednesday = 1 << (int)DayOfWeek.Wednesday,
    Thursday = 1 << (int)DayOfWeek.Thursday,
    Friday = 1 << (int)DayOfWeek.Friday,
    Saturday = 1 << (int)DayOfWeek.Saturday,
    Everyday = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}

public static class AlarmDaysExtensions
{
    public static AlarmDays ToAlarmDay(this DayOfWeek day) => (AlarmDays)(1 << (int)day);

    public static bool Includes(this AlarmDays days, DayOfWeek day) => (days & day.ToAlarmDay()) != AlarmDays.None;

    /// <summary>The set of the given days. The weekend differs by state, so there is no fixed "weekdays" value.</summary>
    public static AlarmDays Of(params ReadOnlySpan<DayOfWeek> days)
    {
        var result = AlarmDays.None;
        foreach (var day in days)
            result |= day.ToAlarmDay();
        return result;
    }
}

public enum AlarmState
{
    /// <summary>Turned off, or a one-time alarm that already rang.</summary>
    Off,

    /// <summary>Waiting for <see cref="AlarmStatus.Occurrence"/>.</summary>
    Waiting,

    /// <summary>Its time has come within <see cref="Alarm.RingWindow"/>. Ring now.</summary>
    Ringing,

    /// <summary>Its time passed while Atap was closed or asleep. Show "Missed alarm" once.</summary>
    Missed,
}

/// <param name="Occurrence">The occurrence waited for, ringing or missed. Null when off.</param>
public readonly record struct AlarmStatus(AlarmState State, DateTimeOffset? Occurrence);

/// <summary>
/// One alarm. It waits for its first occurrence after <see cref="HandledThrough"/>, or after
/// <see cref="ArmedAt"/> when nothing has been handled yet. An occurrence that passed while Atap
/// was closed is still found, so it can be reported as missed exactly once.
/// </summary>
public sealed record Alarm
{
    public const int MaxLabelLength = 48;

    /// <summary>How long after its time an alarm still rings. Any later and it counts as missed.</summary>
    public static readonly TimeSpan RingWindow = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan MinSnooze = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxSnooze = TimeSpan.FromMinutes(60);

    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public TimeOnly Time { get; set; }

    /// <summary>Days it repeats on. None rings once.</summary>
    public AlarmDays Days { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>When the alarm was set or last turned on. Occurrences before it never count.</summary>
    public DateTimeOffset ArmedAt { get; set; }

    /// <summary>Occurrences at or before this have rung or been reported missed.</summary>
    public DateTimeOffset? HandledThrough { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool Repeats => (Days & AlarmDays.Everyday) != AlarmDays.None;

    public static Alarm Create(string id, TimeOnly time, AlarmDays days, string? label, DateTimeOffset now) => new()
    {
        Id = id,
        Time = time,
        Days = days & AlarmDays.Everyday,
        Label = ProductivityText.CleanLine(label, MaxLabelLength),
        ArmedAt = now,
    };

    /// <summary>The first time it rings strictly after <paramref name="after"/>. Ignores snooze and the on switch.</summary>
    public DateTimeOffset NextOccurrenceAfter(DateTimeOffset after, TimeZoneInfo zone)
    {
        var date = WallClock.DateOf(after, zone);
        // Eight days reach the same weekday next week, even when today's time has passed.
        for (var day = 0; day <= 8; day++)
        {
            var candidate = date.AddDays(day);
            if (Repeats && !Days.Includes(candidate.DayOfWeek))
                continue;
            var at = WallClock.ToInstant(candidate, Time, zone);
            if (at > after)
                return at;
        }
        return WallClock.ToInstant(date.AddDays(9), Time, zone);
    }

    /// <summary>The occurrence this alarm waits for: the snooze end, or the first occurrence after the last handled one. Null when off.</summary>
    public DateTimeOffset? Pending(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!Enabled)
            return null;
        if (SnoozedUntil is { } snoozed)
            return snoozed;

        var anchor = HandledThrough is { } handled && handled > ArmedAt ? handled : ArmedAt;
        if (anchor <= now && WallClock.IsSupported(anchor))
            return NextOccurrenceAfter(anchor, zone);

        // The clock was set backwards, or the file was edited by hand. When it steps back only a
        // little, for example after a time sync on resume, the occurrence that already rang (or
        // whose snooze rang), or the one from before the alarm was set, comes up again. Skip it so
        // it never rings twice and never rings before it was meant to.
        var next = NextOccurrenceAfter(now, zone);
        if (next <= anchor)
        {
            var after = NextOccurrenceAfter(next, zone);
            if (after > anchor)
                return after;
        }
        return next;
    }

    /// <summary>
    /// What to do at <paramref name="now"/>. A missed repeating alarm reports its latest occurrence,
    /// so several days away or a missed snooze give one "Missed alarm", not one per day. When that
    /// latest occurrence is still inside <see cref="RingWindow"/> it rings instead.
    /// </summary>
    public AlarmStatus Check(DateTimeOffset now, TimeZoneInfo zone)
    {
        if (Pending(now, zone) is not { } due)
            return new AlarmStatus(AlarmState.Off, null);
        if (now < due)
            return new AlarmStatus(AlarmState.Waiting, due);

        if (Repeats
            && LatestOccurrenceAtOrBefore(now, zone) is { } latest
            && latest > due
            && (HandledThrough is not { } handled || latest > handled))
            due = latest;
        return now - due <= RingWindow
            ? new AlarmStatus(AlarmState.Ringing, due)
            : new AlarmStatus(AlarmState.Missed, due);
    }

    /// <summary>
    /// Records that <paramref name="occurrence"/> rang or was reported missed, so it never alerts
    /// again. Call it when ringing starts. A one-time alarm turns off.
    /// </summary>
    public Alarm MarkHandled(DateTimeOffset occurrence) => this with
    {
        HandledThrough = HandledThrough is { } handled && handled > occurrence ? handled : occurrence,
        SnoozedUntil = null,
        Enabled = Enabled && Repeats,
    };

    /// <summary>Rings again after <paramref name="length"/>, kept between 1 and 60 minutes.</summary>
    public Alarm Snooze(DateTimeOffset now, TimeSpan length)
    {
        var clamped = length < MinSnooze ? MinSnooze : length > MaxSnooze ? MaxSnooze : length;
        return this with { SnoozedUntil = now + clamped, Enabled = true };
    }

    /// <summary>Turns the alarm on from now. Occurrences before now never count.</summary>
    public Alarm TurnOn(DateTimeOffset now) => this with
    {
        Enabled = true,
        ArmedAt = now,
        HandledThrough = null,
        SnoozedUntil = null,
    };

    public Alarm TurnOff() => this with { Enabled = false, SnoozedUntil = null };

    /// <summary>Changes the time or days. The alarm is armed again from now.</summary>
    public Alarm Reschedule(TimeOnly time, AlarmDays days, DateTimeOffset now) =>
        TurnOn(now) with { Time = time, Days = days & AlarmDays.Everyday };

    public Alarm Rename(string? label) => this with { Label = ProductivityText.CleanLine(label, MaxLabelLength) };

    private DateTimeOffset? LatestOccurrenceAtOrBefore(DateTimeOffset atOrBefore, TimeZoneInfo zone)
    {
        var date = WallClock.DateOf(atOrBefore, zone);
        for (var day = 0; day <= 7; day++)
        {
            var candidate = date.AddDays(-day);
            if (!Days.Includes(candidate.DayOfWeek))
                continue;
            var at = WallClock.ToInstant(candidate, Time, zone);
            if (at <= atOrBefore)
                return at;
        }
        return null;
    }
}

public readonly record struct AlarmDue(Alarm Alarm, DateTimeOffset Due);

/// <summary>The alarms of one alarm widget, stored as user data.</summary>
public sealed record AlarmDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxAlarms = 20;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public IReadOnlyList<Alarm> Alarms { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsFull => ItemLists.Present(Alarms).Count() >= MaxAlarms;

    /// <summary>
    /// Repairs the values of a document that was read from a file: drops nulls, fixes ids, trims
    /// labels and caps the list. An alarm with no usable armed time is armed from
    /// <paramref name="now"/>. A value the reader cannot parse at all, such as "7am" for a time or
    /// "weekdays" for the days, fails the whole read before this runs.
    /// </summary>
    public AlarmDocument Normalize(DateTimeOffset now) => this with
    {
        Alarms = ItemLists.Normalize(Alarms, MaxAlarms, a => a.Id, (a, id) => a with
        {
            Id = id,
            Label = ProductivityText.CleanLine(a.Label, Alarm.MaxLabelLength),
            Days = a.Days & AlarmDays.Everyday,
            ArmedAt = WallClock.IsSupported(a.ArmedAt) ? a.ArmedAt : now,
            HandledThrough = a.HandledThrough is { } handled && WallClock.IsSupported(handled) ? handled : null,
            SnoozedUntil = a.SnoozedUntil is { } snoozed && WallClock.IsSupported(snoozed) ? snoozed : null,
        }),
    };

    public Alarm? Find(string id) =>
        ItemLists.Present(Alarms).FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    /// <summary>Replaces the alarm with the same id, or adds it while there are fewer than 20.</summary>
    public AlarmDocument Upsert(Alarm alarm) => this with { Alarms = ItemLists.Upsert(Alarms, alarm, a => a.Id, MaxAlarms) };

    public AlarmDocument Remove(string id) => this with { Alarms = ItemLists.Remove(Alarms, id, a => a.Id) };

    /// <summary>The enabled alarm due first, so only one scheduler item is needed.</summary>
    public AlarmDue? NextDue(DateTimeOffset now, TimeZoneInfo zone)
    {
        AlarmDue? best = null;
        foreach (var alarm in ItemLists.Present(Alarms))
        {
            if (alarm.Pending(now, zone) is not { } due)
                continue;
            if (best is not { } current
                || due < current.Due
                || (due == current.Due && string.CompareOrdinal(alarm.Id, current.Alarm.Id) < 0))
                best = new AlarmDue(alarm, due);
        }
        return best;
    }
}
