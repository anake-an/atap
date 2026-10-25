using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Student;

public enum DueState
{
    /// <summary>The due time has passed.</summary>
    Overdue,

    /// <summary>Due later today.</summary>
    Today,

    /// <summary>Due tomorrow.</summary>
    Tomorrow,

    /// <summary>Due in two or more days, for example "3 hari lagi".</summary>
    Later,
}

/// <param name="Days">
/// Calendar days from today to the due date, as in "3 hari lagi" or "in 3 days". Zero when due
/// today. Negative when the due date has passed.
/// </param>
/// <param name="Remaining">Time left until the due time. Negative when overdue.</param>
public readonly record struct DueCountdown(DueState State, int Days, TimeSpan Remaining)
{
    /// <summary>
    /// Counts calendar days, not 24 hour blocks, so a deadline on Wednesday reads "3 days" all of
    /// Sunday, whatever its time.
    /// </summary>
    public static DueCountdown Between(DateTimeOffset due, DateTimeOffset now, TimeZoneInfo zone)
    {
        var days = WallClock.DateOf(due, zone).DayNumber - WallClock.DateOf(now, zone).DayNumber;
        var remaining = due - now;
        var state = remaining <= TimeSpan.Zero ? DueState.Overdue
            : days <= 0 ? DueState.Today
            : days == 1 ? DueState.Tomorrow
            : DueState.Later;
        return new DueCountdown(state, days, remaining);
    }
}

/// <summary>One assignment with a deadline, for example "Laporan FYP Bab 3".</summary>
public sealed record Assignment
{
    public const int MaxTitleLength = 120;
    public const int MaxCourseLength = 80;

    /// <summary>Due time when only a date is given. Most portals close at 23:59.</summary>
    public static readonly TimeOnly DefaultDueTime = new(23, 59);

    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    /// <summary>Course code or name, for example "CSC584".</summary>
    public string Course { get; set; } = "";

    /// <summary>Due date. Null means none was set.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Due time on <see cref="DueDate"/>. Null means 23:59.</summary>
    public TimeOnly? DueTime { get; set; }

    public bool Done { get; set; }

    public DateTimeOffset? DoneAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// A new assignment. A due date outside <see cref="WallClock.IsSupported(DateOnly)"/> counts as
    /// no date, as in the to-do list.
    /// </summary>
    public static Assignment Create(string id, string? title, string? course, DateOnly dueDate, TimeOnly? dueTime, DateTimeOffset now)
    {
        var usable = WallClock.IsSupported(dueDate) ? dueDate : (DateOnly?)null;
        return new()
        {
            Id = id,
            Title = ProductivityText.CleanLine(title, MaxTitleLength),
            Course = ProductivityText.CleanLine(course, MaxCourseLength),
            DueDate = usable,
            DueTime = usable is null ? null : dueTime,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// When the assignment is due. A date outside <see cref="WallClock.IsSupported(DateOnly)"/>
    /// counts as no date, so one bad entry never shows a countdown of centuries.
    /// </summary>
    public DateTimeOffset? DueAt(TimeZoneInfo zone) =>
        DueDate is { } date && WallClock.IsSupported(date) ? WallClock.ToInstant(date, DueTime ?? DefaultDueTime, zone) : null;

    /// <summary>The countdown at <paramref name="now"/>, or null when there is no due date.</summary>
    public DueCountdown? CountdownAt(DateTimeOffset now, TimeZoneInfo zone) =>
        DueAt(zone) is { } due ? DueCountdown.Between(due, now, zone) : null;

    public Assignment Complete(DateTimeOffset now) => Done ? this : this with { Done = true, DoneAt = now };

    public Assignment Reopen() => Done ? this with { Done = false, DoneAt = null } : this;
}

public readonly record struct AssignmentCountdown(Assignment Assignment, DueCountdown? Countdown);

/// <summary>The assignments of one assignments widget, stored as user data.</summary>
public sealed record AssignmentDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxAssignments = 100;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public IReadOnlyList<Assignment> Assignments { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsFull => ItemLists.Present(Assignments).Count() >= MaxAssignments;

    /// <summary>
    /// Repairs the values of a document that was read from a file: drops nulls and untitled
    /// entries, fixes ids, trims text and clears unusable dates. A value the reader cannot parse at
    /// all, such as "7am" for a time, fails the whole read before this runs.
    /// </summary>
    public AssignmentDocument Normalize() => this with
    {
        Assignments = ItemLists.Normalize(Assignments, MaxAssignments, a => a.Id, (assignment, id) =>
        {
            var title = ProductivityText.CleanLine(assignment.Title, Assignment.MaxTitleLength);
            if (title.Length == 0)
                return null;
            var date = assignment.DueDate is { } due && WallClock.IsSupported(due) ? due : (DateOnly?)null;
            return assignment with
            {
                Id = id,
                Title = title,
                Course = ProductivityText.CleanLine(assignment.Course, Assignment.MaxCourseLength),
                DueDate = date,
                DueTime = date is null ? null : assignment.DueTime,
                DoneAt = assignment.Done ? assignment.DoneAt : null,
            };
        }),
    };

    public Assignment? Find(string id) =>
        ItemLists.Present(Assignments).FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));

    public AssignmentDocument Upsert(Assignment assignment) =>
        this with { Assignments = ItemLists.Upsert(Assignments, assignment, a => a.Id, MaxAssignments) };

    public AssignmentDocument Remove(string id) => this with { Assignments = ItemLists.Remove(Assignments, id, a => a.Id) };

    public AssignmentDocument RemoveDone() =>
        this with { Assignments = ItemLists.Present(Assignments).Where(a => !a.Done).ToList() };

    /// <summary>Open assignments with their countdowns, nearest deadline first. Overdue ones lead, undated ones come last.</summary>
    public IReadOnlyList<AssignmentCountdown> Upcoming(DateTimeOffset now, TimeZoneInfo zone) =>
        ItemLists.Present(Assignments)
            .Where(a => !a.Done)
            .Select(a => (Assignment: a, Due: a.DueAt(zone)))
            .OrderBy(x => x.Due is null)
            .ThenBy(x => x.Due ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Assignment.Title, StringComparer.Ordinal)
            .ThenBy(x => x.Assignment.Id, StringComparer.Ordinal)
            .Select(x => new AssignmentCountdown(
                x.Assignment,
                x.Due is { } due ? DueCountdown.Between(due, now, zone) : null))
            .ToList();

    /// <summary>
    /// The next time any countdown changes state: the nearest deadline still ahead, or local
    /// midnight when the day count changes. One scheduler item at this time is enough.
    /// </summary>
    public DateTimeOffset NextChange(DateTimeOffset now, TimeZoneInfo zone)
    {
        var change = WallClock.StartOfNextDay(now, zone);
        foreach (var assignment in ItemLists.Present(Assignments))
        {
            if (!assignment.Done && assignment.DueAt(zone) is { } due && due > now && due < change)
                change = due;
        }
        return change;
    }
}
