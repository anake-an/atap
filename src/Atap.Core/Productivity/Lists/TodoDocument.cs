using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Lists;

public readonly record struct TodoReminder(TodoItem Item, DateTimeOffset At);

/// <param name="Recent">Reminders that came due within <see cref="TodoDocument.MaxReminderAge"/>, oldest first. Show each on its own.</param>
/// <param name="Older">Reminders that came due longer ago, for example while the PC was off for days, oldest first. Show them as one summary.</param>
public readonly record struct TodoDueReminders(IReadOnlyList<TodoReminder> Recent, IReadOnlyList<TodoReminder> Older);

/// <summary>The tasks of one to-do widget, stored as user data.</summary>
public sealed record TodoDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxItems = 500;

    /// <summary>Reminder time for tasks that have a due date but no due time.</summary>
    public static readonly TimeOnly DefaultAllDayReminder = new(9, 0);

    /// <summary>How old a reminder can be and still show on its own. Older ones are shown as one summary.</summary>
    public static readonly TimeSpan MaxReminderAge = TimeSpan.FromHours(1);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public IReadOnlyList<TodoItem> Items { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsFull => ItemLists.Present(Items).Count() >= MaxItems;

    /// <summary>
    /// Repairs the values of a document that was read from a file: drops nulls and blank tasks,
    /// fixes ids, trims titles, clears unusable dates and keeps the reminder lead between 0 and 30
    /// days. A value the reader cannot parse at all, such as "7am" for a time, fails the whole read
    /// before this runs.
    /// </summary>
    public TodoDocument Normalize() => this with
    {
        Items = ItemLists.Normalize(Items, MaxItems, i => i.Id, (item, id) =>
        {
            var title = ProductivityText.CleanLine(item.Title, TodoItem.MaxTitleLength);
            if (title.Length == 0)
                return null;
            var date = item.DueDate is { } due && WallClock.IsSupported(due) ? due : (DateOnly?)null;
            return item with
            {
                Id = id,
                Title = title,
                DueDate = date,
                DueTime = date is null ? null : item.DueTime,
                RemindBefore = date is null ? null : TodoItem.ClampLead(item.RemindBefore),
                DoneAt = item.Done ? item.DoneAt : null,
                SnoozedUntil = item.SnoozedUntil is { } snoozed && WallClock.IsSupported(snoozed) ? snoozed : null,
                RemindedThrough = item.RemindedThrough is { } through && WallClock.IsSupported(through) ? through : null,
            };
        }),
    };

    public TodoItem? Find(string id) =>
        ItemLists.Present(Items).FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.Ordinal));

    /// <summary>Replaces the task with the same id, or adds it while the list is not full.</summary>
    public TodoDocument Upsert(TodoItem item) => this with { Items = ItemLists.Upsert(Items, item, i => i.Id, MaxItems) };

    public TodoDocument Remove(string id) => this with { Items = ItemLists.Remove(Items, id, i => i.Id) };

    public TodoDocument RemoveDone() => this with { Items = ItemLists.Present(Items).Where(i => !i.Done).ToList() };

    public int OpenCount() => ItemLists.Present(Items).Count(i => !i.Done);

    public int OverdueCount(DateTimeOffset now, TimeZoneInfo zone) =>
        ItemLists.Present(Items).Count(i => i.IsOverdueAt(now, zone));

    /// <summary>
    /// Open tasks first, by due time with undated tasks after them, then by when they were added.
    /// Done tasks come last, the most recently done first.
    /// </summary>
    public IReadOnlyList<TodoItem> Ordered(TimeZoneInfo zone)
    {
        var present = ItemLists.Present(Items).ToList();
        var open = present
            .Where(i => !i.Done)
            .Select(i => (Item: i, Due: i.DueAt(zone)))
            .OrderBy(x => x.Due is null)
            .ThenBy(x => x.Due ?? DateTimeOffset.MaxValue)
            .ThenBy(x => x.Item.CreatedAt)
            .ThenBy(x => x.Item.Id, StringComparer.Ordinal)
            .Select(x => x.Item);
        var done = present
            .Where(i => i.Done)
            .OrderByDescending(i => i.DoneAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Id, StringComparer.Ordinal);
        return open.Concat(done).ToList();
    }

    /// <summary>
    /// Reminders due at or before <paramref name="now"/> that have not shown, oldest first. This
    /// includes reminders that came due while Atap was closed. Those older than
    /// <see cref="MaxReminderAge"/> are listed apart, so weeks away give one summary, not one
    /// alert per task. Show each recent one and the summary, then mark them with
    /// <see cref="MarkReminded(string, DateTimeOffset)"/> or <see cref="MarkReminded(IEnumerable{TodoReminder})"/>.
    /// </summary>
    public TodoDueReminders DueReminders(DateTimeOffset now, TimeZoneInfo zone, TimeOnly allDayAt)
    {
        var recent = new List<TodoReminder>();
        var older = new List<TodoReminder>();
        foreach (var reminder in Pending(zone, allDayAt))
        {
            if (reminder.At > now)
                break;
            (now - reminder.At > MaxReminderAge ? older : recent).Add(reminder);
        }
        return new TodoDueReminders(recent, older);
    }

    /// <summary>The earliest reminder after <paramref name="now"/>. Only this one needs a scheduler item.</summary>
    public TodoReminder? NextReminder(DateTimeOffset now, TimeZoneInfo zone, TimeOnly allDayAt)
    {
        foreach (var reminder in Pending(zone, allDayAt))
        {
            if (reminder.At > now)
                return reminder;
        }
        return null;
    }

    public TodoDocument MarkReminded(string id, DateTimeOffset at) =>
        Find(id) is { } item ? Upsert(item.MarkReminded(at)) : this;

    /// <summary>Records that all of <paramref name="reminders"/> showed, for example after the summary of older ones.</summary>
    public TodoDocument MarkReminded(IEnumerable<TodoReminder> reminders) =>
        reminders.Aggregate(this, (document, reminder) => document.MarkReminded(reminder.Item.Id, reminder.At));

    private IEnumerable<TodoReminder> Pending(TimeZoneInfo zone, TimeOnly allDayAt) =>
        ItemLists.Present(Items)
            .Select(i => (Item: i, At: i.PendingReminder(zone, allDayAt)))
            .Where(x => x.At is not null)
            .Select(x => new TodoReminder(x.Item, x.At!.Value))
            .OrderBy(r => r.At)
            .ThenBy(r => r.Item.Id, StringComparer.Ordinal);
}
