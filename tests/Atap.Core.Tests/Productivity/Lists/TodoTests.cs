using System.Text.Json;
using Atap.Core.Productivity.Lists;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Lists;

public class TodoTests
{
    private static readonly TimeOnly NineAm = TodoDocument.DefaultAllDayReminder;

    /// <summary>
    /// To-do file as saved on Sunday 25 October 2026: a bill due Tuesday with a reminder a day
    /// before, slides due Monday 14:00 with a reminder 30 minutes before, a done task, an undated
    /// task, a blank task, a task with an unusable date and a field from a newer version.
    /// </summary>
    private const string SavedTodos = """
        {
          "schemaVersion": 1,
          "items": [
            {
              "id": "bil-tnb",
              "title": "Bayar bil elektrik",
              "createdAt": "2026-10-20T08:00:00+08:00",
              "dueDate": "2026-10-27",
              "remindBefore": "1.00:00:00",
              "remindedThrough": "2026-10-20T08:00:00+08:00"
            },
            {
              "id": "slaid",
              "title": "Hantar slaid ke penyelia",
              "createdAt": "2026-10-22T21:15:00+08:00",
              "dueDate": "2026-10-26",
              "dueTime": "14:00",
              "remindBefore": "00:30:00",
              "remindedThrough": "2026-10-22T21:15:00+08:00",
              "colour": "amber"
            },
            {
              "id": "dapur",
              "title": "Beli barang dapur",
              "createdAt": "2026-10-23T18:00:00+08:00",
              "done": true,
              "doneAt": "2026-10-24T18:30:00+08:00"
            },
            { "id": "cukai-jalan", "title": "Renew road tax", "createdAt": "2026-10-24T10:00:00+08:00" },
            { "id": "kosong", "title": "   ", "createdAt": "2026-10-24T10:00:00+08:00" },
            {
              "id": "rosak",
              "title": "Tarikh rosak",
              "createdAt": "2026-10-25T07:00:00+08:00",
              "dueDate": "0001-01-01",
              "dueTime": "09:00",
              "remindBefore": "00:10:00"
            }
          ],
          "sortMode": "due"
        }
        """;

    private static TodoDocument Load() =>
        JsonSerializer.Deserialize(SavedTodos, ListsJsonContext.Default.TodoDocument)!.Normalize();

    private static TodoItem Task(DateOnly? date, TimeOnly? time, TimeSpan? before, DateTimeOffset setAt) =>
        TodoItem.Create("t", "Tugasan", setAt).WithDue(date, time, before, setAt);

    [Fact]
    public void Reminder_WithDueTime_IsTheLeadBeforeIt()
    {
        var item = Task(new DateOnly(2026, 10, 26), new TimeOnly(14, 0), TimeSpan.FromMinutes(30), My(2026, 10, 25, 9, 0));

        Assert.Equal(My(2026, 10, 26, 13, 30), item.ReminderAt(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 26, 14, 0), item.DueAt(MalaysiaZone));
    }

    [Fact]
    public void Reminder_AtTheDueTime_WhenTheLeadIsZero()
    {
        var item = Task(new DateOnly(2026, 10, 26), new TimeOnly(14, 0), TimeSpan.Zero, My(2026, 10, 25, 9, 0));

        Assert.Equal(My(2026, 10, 26, 14, 0), item.ReminderAt(MalaysiaZone, NineAm));
    }

    [Fact]
    public void Reminder_WithoutDueTime_UsesTheAllDayTime()
    {
        var onTheDay = Task(new DateOnly(2026, 10, 27), null, TimeSpan.Zero, My(2026, 10, 25, 9, 0));
        var dayBefore = Task(new DateOnly(2026, 10, 27), null, TimeSpan.FromDays(1), My(2026, 10, 25, 9, 0));

        Assert.Equal(My(2026, 10, 27, 9, 0), onTheDay.ReminderAt(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 26, 9, 0), dayBefore.ReminderAt(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 28, 0, 0), dayBefore.DueAt(MalaysiaZone));
    }

    [Fact]
    public void Reminder_NoLeadOrNoDate_IsNone()
    {
        var noLead = Task(new DateOnly(2026, 10, 27), null, null, My(2026, 10, 25, 9, 0));
        var noDate = Task(null, new TimeOnly(9, 0), TimeSpan.Zero, My(2026, 10, 25, 9, 0));

        Assert.Null(noLead.ReminderAt(MalaysiaZone, NineAm));
        Assert.Null(noDate.ReminderAt(MalaysiaZone, NineAm));
        Assert.Null(noDate.DueTime);
    }

    [Fact]
    public void Reminder_WholeDaysKeepTheTimeOfDayAcrossDaylightSaving()
    {
        var item = Task(new DateOnly(2026, 3, 29), new TimeOnly(9, 0), TimeSpan.FromDays(1), Utc(2026, 3, 1, 0));

        var at = item.ReminderAt(Seasonal, NineAm)!.Value;

        Assert.Equal(new DateTime(2026, 3, 28, 9, 0, 0), Atap.Core.Productivity.WallClock.ToLocal(at, Seasonal));
    }

    [Fact]
    public void Reminder_HoursComeOffTheRealTimeline()
    {
        // 02:30 on 2026-03-29 comes one real hour after 00:30 because 01:00 jumps to 02:00.
        var item = Task(new DateOnly(2026, 3, 29), new TimeOnly(2, 30), TimeSpan.FromHours(1), Utc(2026, 3, 1, 0));

        Assert.Equal(Utc(2026, 3, 29, 0, 30), item.ReminderAt(Seasonal, NineAm));
    }

    [Fact]
    public void SavedFile_RemindersAreCorrect()
    {
        var todos = Load();

        Assert.Equal(My(2026, 10, 26, 9, 0), todos.Find("bil-tnb")!.PendingReminder(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 26, 13, 30), todos.Find("slaid")!.PendingReminder(MalaysiaZone, NineAm));
        Assert.Null(todos.Find("dapur")!.PendingReminder(MalaysiaZone, NineAm));
        Assert.Null(todos.Find("cukai-jalan")!.PendingReminder(MalaysiaZone, NineAm));
    }

    [Fact]
    public void NextReminder_IsOnlyTheEarliestAhead()
    {
        var todos = Load();

        var next = todos.NextReminder(My(2026, 10, 25, 22, 0), MalaysiaZone, NineAm);

        Assert.Equal("bil-tnb", next!.Value.Item.Id);
        Assert.Equal(My(2026, 10, 26, 9, 0), next.Value.At);
    }

    [Fact]
    public void RemindersThatCameDueWhileClosed_ShowOnceOnStart()
    {
        var todos = Load();
        var startedAt = My(2026, 10, 26, 13, 45);

        var due = todos.DueReminders(startedAt, MalaysiaZone, NineAm);
        foreach (var reminder in due.Recent)
            todos = todos.MarkReminded(reminder.Item.Id, reminder.At);
        todos = todos.MarkReminded(due.Older);
        var again = todos.DueReminders(startedAt, MalaysiaZone, NineAm);

        Assert.Equal(["slaid"], due.Recent.Select(r => r.Item.Id));
        Assert.Equal(["bil-tnb"], due.Older.Select(r => r.Item.Id));
        Assert.Empty(again.Recent);
        Assert.Empty(again.Older);
        Assert.Null(todos.NextReminder(startedAt, MalaysiaZone, NineAm));
    }

    [Fact]
    public void WeeksAway_OldRemindersFoldIntoOneSummary()
    {
        var setAt = My(2026, 10, 1, 9, 0);
        var todos = new TodoDocument();
        for (var day = 2; day <= 24; day++)
        {
            todos = todos.Upsert(TodoItem.Create($"t{day}", $"Tugasan {day}", setAt)
                .WithDue(new DateOnly(2026, 10, day), new TimeOnly(10, 0), TimeSpan.Zero, setAt));
        }
        var back = My(2026, 10, 24, 10, 30);

        var due = todos.DueReminders(back, MalaysiaZone, NineAm);
        var again = todos.MarkReminded(due.Older).DueReminders(back, MalaysiaZone, NineAm);

        Assert.Equal(["t24"], due.Recent.Select(r => r.Item.Id));
        Assert.Equal(22, due.Older.Count);
        Assert.Equal("t2", due.Older[0].Item.Id);
        Assert.Empty(again.Older);
        Assert.Equal(["t24"], again.Recent.Select(r => r.Item.Id));
    }

    [Fact]
    public void UnsupportedDate_CountsAsNoDateAndNeverThrows()
    {
        var now = My(2026, 10, 25, 9, 0);
        var picked = Task(DateOnly.MaxValue, null, TimeSpan.FromDays(1), now);
        var allDayAtTheEnd = TodoItem.Create("akhir", "Hujung", now) with { DueDate = DateOnly.MaxValue };
        var nearTheStart = TodoItem.Create("awal", "Permulaan", now) with
        {
            DueDate = DateOnly.MinValue,
            DueTime = new TimeOnly(9, 0),
            RemindBefore = TimeSpan.FromDays(1),
        };
        var todos = new TodoDocument { Items = [picked, allDayAtTheEnd, nearTheStart] };

        Assert.Null(picked.DueDate);
        Assert.Null(picked.RemindBefore);
        Assert.Null(allDayAtTheEnd.DueAt(MalaysiaZone));
        Assert.Null(nearTheStart.DueAt(MalaysiaZone));
        Assert.Null(nearTheStart.ReminderAt(MalaysiaZone, NineAm));
        Assert.Equal(3, todos.Ordered(MalaysiaZone).Count);
        Assert.Equal(0, todos.OverdueCount(now, MalaysiaZone));
        Assert.Null(todos.NextReminder(now, MalaysiaZone, NineAm));
        Assert.Empty(todos.DueReminders(now, MalaysiaZone, NineAm).Recent);
    }

    [Fact]
    public void Reminder_LeadOverThirtyDaysInAnUnnormalizedTask_IsCapped()
    {
        var item = Task(new DateOnly(2026, 12, 1), new TimeOnly(9, 0), null, My(2026, 10, 25, 9, 0)) with { RemindBefore = TimeSpan.MaxValue };

        Assert.Equal(My(2026, 11, 1, 9, 0), item.ReminderAt(MalaysiaZone, NineAm));
    }

    [Fact]
    public void ReminderSetInThePast_NeverShows()
    {
        var item = Task(new DateOnly(2026, 10, 25), new TimeOnly(9, 0), TimeSpan.Zero, My(2026, 10, 25, 10, 0));

        Assert.Equal(My(2026, 10, 25, 9, 0), item.ReminderAt(MalaysiaZone, NineAm));
        Assert.Null(item.PendingReminder(MalaysiaZone, NineAm));
    }

    [Fact]
    public void ReminderMovedLater_ShowsAgain()
    {
        var item = Task(new DateOnly(2026, 10, 25), new TimeOnly(11, 0), TimeSpan.Zero, My(2026, 10, 25, 9, 0))
            .MarkReminded(My(2026, 10, 25, 11, 0));

        var moved = item.WithDue(new DateOnly(2026, 10, 26), new TimeOnly(11, 0), TimeSpan.Zero, My(2026, 10, 25, 11, 1));

        Assert.Null(item.PendingReminder(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 26, 11, 0), moved.PendingReminder(MalaysiaZone, NineAm));
    }

    [Fact]
    public void Snooze_ShowsAgainOnceThenStaysQuiet()
    {
        var shown = Task(new DateOnly(2026, 10, 25), new TimeOnly(11, 0), TimeSpan.Zero, My(2026, 10, 25, 9, 0))
            .MarkReminded(My(2026, 10, 25, 11, 0));

        var snoozed = shown.Snooze(My(2026, 10, 25, 11, 0, 30), TimeSpan.FromMinutes(10));
        var shownAgain = snoozed.MarkReminded(snoozed.PendingReminder(MalaysiaZone, NineAm)!.Value);

        Assert.Equal(My(2026, 10, 25, 11, 10, 30), snoozed.PendingReminder(MalaysiaZone, NineAm));
        Assert.Null(shownAgain.PendingReminder(MalaysiaZone, NineAm));
    }

    [Fact]
    public void Done_SilencesTheReminder_AndReopenKeepsPastOnesQuiet()
    {
        var item = Task(new DateOnly(2026, 10, 26), new TimeOnly(14, 0), TimeSpan.FromMinutes(30), My(2026, 10, 25, 9, 0));

        var done = item.Complete(My(2026, 10, 25, 10, 0));
        var reopenedBefore = done.Reopen(My(2026, 10, 26, 12, 0));
        var reopenedAfter = done.Reopen(My(2026, 10, 26, 13, 45));

        Assert.Null(done.PendingReminder(MalaysiaZone, NineAm));
        Assert.Equal(My(2026, 10, 26, 13, 30), reopenedBefore.PendingReminder(MalaysiaZone, NineAm));
        Assert.Null(reopenedAfter.PendingReminder(MalaysiaZone, NineAm));
    }

    [Fact]
    public void Overdue_TimedAtItsTimeAllDayAtTheEndOfTheDay()
    {
        var timed = Task(new DateOnly(2026, 10, 25), new TimeOnly(14, 0), null, My(2026, 10, 25, 9, 0));
        var allDay = Task(new DateOnly(2026, 10, 25), null, null, My(2026, 10, 25, 9, 0));

        Assert.False(timed.IsOverdueAt(My(2026, 10, 25, 13, 59), MalaysiaZone));
        Assert.True(timed.IsOverdueAt(My(2026, 10, 25, 14, 0), MalaysiaZone));
        Assert.False(allDay.IsOverdueAt(My(2026, 10, 25, 23, 59), MalaysiaZone));
        Assert.True(allDay.IsOverdueAt(My(2026, 10, 26, 0, 0), MalaysiaZone));
    }

    [Fact]
    public void Ordered_OpenByDueThenUndatedThenDone()
    {
        var todos = Load();

        Assert.Equal(["slaid", "bil-tnb", "cukai-jalan", "rosak", "dapur"], todos.Ordered(MalaysiaZone).Select(i => i.Id));
        Assert.Equal(4, todos.OpenCount());
        Assert.Equal(1, todos.OverdueCount(My(2026, 10, 26, 15, 0), MalaysiaZone));
    }

    [Fact]
    public void SavedFile_NormalizeDropsBlankTasksAndClearsUnusableDates()
    {
        var todos = Load();

        Assert.Null(todos.Find("kosong"));
        var broken = todos.Find("rosak")!;
        Assert.Null(broken.DueDate);
        Assert.Null(broken.DueTime);
        Assert.Null(broken.RemindBefore);
    }

    [Fact]
    public void SavedFile_RoundTripsAndKeepsUnknownFields()
    {
        var todos = Load();

        var json = JsonSerializer.Serialize(todos, ListsJsonContext.Default.TodoDocument);
        var again = JsonSerializer.Deserialize(json, ListsJsonContext.Default.TodoDocument)!;

        Assert.Contains("\"sortMode\": \"due\"", json);
        Assert.Contains("\"colour\": \"amber\"", json);
        Assert.Contains("\"dueTime\": \"14:00\"", json);
        Assert.Equal(
            todos.Items.Select(i => (i.Id, i.Title, i.DueDate, i.DueTime, i.RemindBefore, i.Done)),
            again.Items.Select(i => (i.Id, i.Title, i.DueDate, i.DueTime, i.RemindBefore, i.Done)));
    }

    [Fact]
    public void Normalize_CapsTheLeadAtThirtyDays()
    {
        var document = new TodoDocument
        {
            Items = [Task(new DateOnly(2026, 12, 1), null, TimeSpan.FromDays(400), My(2026, 10, 25, 9, 0)) with { RemindBefore = TimeSpan.FromDays(400) }],
        }.Normalize();

        Assert.Equal(TimeSpan.FromDays(30), document.Items[0].RemindBefore);
    }

    [Fact]
    public void NullItems_LoadAsAnEmptyList()
    {
        var loaded = JsonSerializer.Deserialize("""{ "items": null }""", ListsJsonContext.Default.TodoDocument)!;

        Assert.Equal(0, loaded.OpenCount());
        Assert.Empty(loaded.Ordered(MalaysiaZone));
        Assert.Null(loaded.NextReminder(My(2026, 10, 25, 9, 0), MalaysiaZone, NineAm));
        Assert.Empty(loaded.Normalize().Items);
    }

    [Fact]
    public void Create_CleansTheTitle()
    {
        var item = TodoItem.Create("t", "  Beli\nroti  ", My(2026, 10, 25, 9, 0));

        Assert.Equal("Beli roti", item.Title);
    }
}
