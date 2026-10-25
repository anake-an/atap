using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Lists;

/// <summary>
/// One task. A reminder is set as a lead time before the due time. Reminders at or before
/// <see cref="RemindedThrough"/> never show again, so each one shows once, even across a restart.
/// </summary>
public sealed record TodoItem
{
    public const int MaxTitleLength = 200;

    /// <summary>The longest reminder lead, so a typo cannot push a reminder years back.</summary>
    public static readonly TimeSpan MaxRemindBefore = TimeSpan.FromDays(30);

    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public bool Done { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DoneAt { get; set; }

    /// <summary>Day the task is due. Null means no due date.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Time on <see cref="DueDate"/>. Null means any time that day.</summary>
    public TimeOnly? DueTime { get; set; }

    /// <summary>How long before the due time to remind. Null means no reminder, zero means at the due time.</summary>
    public TimeSpan? RemindBefore { get; set; }

    public DateTimeOffset? SnoozedUntil { get; set; }

    /// <summary>Reminders at or before this time have shown, or were already past when set.</summary>
    public DateTimeOffset? RemindedThrough { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    public static TodoItem Create(string id, string? title, DateTimeOffset now) => new()
    {
        Id = id,
        Title = ProductivityText.CleanLine(title, MaxTitleLength),
        CreatedAt = now,
    };

    /// <summary>
    /// When the task is due. A task with no due time is due at the end of its day. A date outside
    /// <see cref="WallClock.IsSupported(DateOnly)"/> counts as no date, so one bad task never throws.
    /// </summary>
    public DateTimeOffset? DueAt(TimeZoneInfo zone)
    {
        if (DueDate is not { } date || !WallClock.IsSupported(date))
            return null;
        return DueTime is { } time
            ? WallClock.ToInstant(date, time, zone)
            : WallClock.ToInstant(date.AddDays(1), TimeOnly.MinValue, zone);
    }

    public bool IsOverdueAt(DateTimeOffset now, TimeZoneInfo zone) => !Done && DueAt(zone) is { } due && due <= now;

    /// <summary>
    /// When the reminder shows, snooze included. A task with no due time is reminded at
    /// <paramref name="allDayAt"/> on its day. Whole days of the lead are taken off the wall clock,
    /// so "1 day before" keeps its time of day across a daylight saving change. Hours and minutes
    /// are taken off the real timeline. An unsupported date counts as no date and the lead is kept
    /// to 30 days, as <see cref="TodoDocument.Normalize"/> does.
    /// </summary>
    public DateTimeOffset? ReminderAt(TimeZoneInfo zone, TimeOnly allDayAt)
    {
        if (SnoozedUntil is { } snoozed)
            return snoozed;
        if (DueDate is not { } date || !WallClock.IsSupported(date) || ClampLead(RemindBefore) is not { } before)
            return null;

        var wholeDays = before.Days;
        var wall = date.ToDateTime(DueTime ?? allDayAt).AddDays(-wholeDays);
        return WallClock.ToInstant(wall, zone) - (before - TimeSpan.FromDays(wholeDays));
    }

    /// <summary>The reminder still to show, or null when there is none, it already showed or the task is done.</summary>
    public DateTimeOffset? PendingReminder(TimeZoneInfo zone, TimeOnly allDayAt) =>
        !Done && ReminderAt(zone, allDayAt) is { } at && (RemindedThrough is not { } through || at > through)
            ? at
            : null;

    /// <summary>Records that the reminder at <paramref name="at"/> showed. Call it when the alert is raised.</summary>
    public TodoItem MarkReminded(DateTimeOffset at) => this with
    {
        RemindedThrough = RemindedThrough is { } through && through > at ? through : at,
        SnoozedUntil = null,
    };

    public TodoItem Snooze(DateTimeOffset now, TimeSpan length) =>
        this with { SnoozedUntil = now + (length > TimeSpan.Zero ? length : TimeSpan.FromMinutes(1)) };

    /// <summary>
    /// Sets the due date, time and reminder lead. A reminder time already past at
    /// <paramref name="now"/> never shows. Without a date there is no time and no reminder. A date
    /// outside the supported years, such as 31/12/9999 from a date picker, counts as no date.
    /// </summary>
    public TodoItem WithDue(DateOnly? date, TimeOnly? time, TimeSpan? remindBefore, DateTimeOffset now)
    {
        var usable = date is { } value && WallClock.IsSupported(value) ? value : (DateOnly?)null;
        return this with
        {
            DueDate = usable,
            DueTime = usable is null ? null : time,
            RemindBefore = usable is null ? null : ClampLead(remindBefore),
            SnoozedUntil = null,
            RemindedThrough = now,
        };
    }

    public TodoItem Rename(string? title) => this with { Title = ProductivityText.CleanLine(title, MaxTitleLength) };

    public TodoItem Complete(DateTimeOffset now) => Done ? this : this with { Done = true, DoneAt = now, SnoozedUntil = null };

    /// <summary>Opens a done task again. Reminders that passed while it was done stay quiet.</summary>
    public TodoItem Reopen(DateTimeOffset now) => !Done ? this : this with
    {
        Done = false,
        DoneAt = null,
        RemindedThrough = RemindedThrough is { } through && through > now ? through : now,
    };

    internal static TimeSpan? ClampLead(TimeSpan? lead) =>
        lead is not { } value || value < TimeSpan.Zero ? null : value > MaxRemindBefore ? MaxRemindBefore : value;
}
