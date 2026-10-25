using System.Text.Json;
using Atap.Core.Productivity.Student;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Student;

public class AssignmentTests
{
    /// <summary>Assignments as saved on Sunday 25 October 2026.</summary>
    private const string SavedAssignments = """
        {
          "schemaVersion": 1,
          "assignments": [
            {
              "id": "fyp-bab3",
              "title": "Laporan FYP Bab 3",
              "course": "ISP573",
              "dueDate": "2026-10-28",
              "createdAt": "2026-10-01T09:00:00+08:00",
              "portal": "ufuture"
            },
            { "id": "lab5", "title": "Lab 5: REST API", "course": "CSC584", "dueDate": "2026-10-26", "dueTime": "08:00", "createdAt": "2026-10-19T14:00:00+08:00" },
            { "id": "kuiz2", "title": "Kuiz 2", "course": "TMC501", "dueDate": "2026-10-24", "dueTime": "17:00", "createdAt": "2026-10-20T08:00:00+08:00" },
            { "id": "refleksi", "title": "Refleksi minggu 3", "dueDate": "2026-10-20", "done": true, "doneAt": "2026-10-19T23:10:00+08:00" },
            { "id": "cadangan", "title": "Cadangan tajuk", "course": "ISP573" },
            { "id": "tanpa-tajuk", "title": "", "dueDate": "2026-11-01" },
            { "id": "tarikh-rosak", "title": "Esei", "dueDate": "0001-01-01", "dueTime": "10:00" }
          ]
        }
        """;

    private static AssignmentDocument Load() =>
        JsonSerializer.Deserialize(SavedAssignments, StudentJsonContext.Default.AssignmentDocument)!.Normalize();

    [Fact]
    public void ThreeCalendarDaysAway_IsLaterWithThreeDays()
    {
        var countdown = Load().Find("fyp-bab3")!.CountdownAt(My(2026, 10, 25, 10, 0), MalaysiaZone);

        Assert.Equal(new DueCountdown(DueState.Later, 3, new TimeSpan(3, 13, 59, 0)), countdown);
    }

    [Fact]
    public void Days_CountCalendarDaysNotWholeDaysLeft()
    {
        var due = My(2026, 10, 28, 8, 0);

        var countdown = DueCountdown.Between(due, My(2026, 10, 25, 22, 0), MalaysiaZone);

        Assert.Equal(3, countdown.Days);
        Assert.Equal(new TimeSpan(2, 10, 0, 0), countdown.Remaining);
    }

    [Fact]
    public void DueTomorrowMorning_IsTomorrow()
    {
        var countdown = Load().Find("lab5")!.CountdownAt(My(2026, 10, 25, 22, 0), MalaysiaZone);

        Assert.Equal(new DueCountdown(DueState.Tomorrow, 1, TimeSpan.FromHours(10)), countdown);
    }

    [Fact]
    public void DueLaterToday_IsToday()
    {
        var assignment = Assignment.Create("a", "Esei", "TMC501", new DateOnly(2026, 10, 25), null, My(2026, 10, 1, 9, 0));

        var countdown = assignment.CountdownAt(My(2026, 10, 25, 10, 0), MalaysiaZone);

        Assert.Equal(new DueCountdown(DueState.Today, 0, new TimeSpan(13, 59, 0)), countdown);
    }

    [Fact]
    public void PastTheDueTime_IsOverdue()
    {
        var yesterday = Load().Find("kuiz2")!.CountdownAt(My(2026, 10, 25, 10, 0), MalaysiaZone)!.Value;
        var earlierToday = DueCountdown.Between(My(2026, 10, 25, 9, 0), My(2026, 10, 25, 10, 0), MalaysiaZone);

        Assert.Equal(DueState.Overdue, yesterday.State);
        Assert.Equal(-1, yesterday.Days);
        Assert.Equal(new DueCountdown(DueState.Overdue, 0, TimeSpan.FromHours(-1)), earlierToday);
    }

    [Fact]
    public void NoDueTime_MeansOneMinuteBeforeMidnight()
    {
        Assert.Equal(My(2026, 10, 28, 23, 59), Load().Find("fyp-bab3")!.DueAt(MalaysiaZone));
    }

    [Fact]
    public void DateOutsideTheSupportedRange_CountsAsNoDate()
    {
        var now = My(2026, 10, 25, 10, 0);

        var picked = Assignment.Create("a", "Esei", "TMC501", new DateOnly(9999, 12, 31), new TimeOnly(17, 0), now);
        var edited = new Assignment { Id = "b", Title = "Kuiz", DueDate = new DateOnly(9999, 12, 31) };
        var assignments = new AssignmentDocument { Assignments = [picked, edited] };

        Assert.Null(picked.DueDate);
        Assert.Null(picked.DueTime);
        Assert.Null(picked.CountdownAt(now, MalaysiaZone));
        Assert.Null(edited.DueAt(MalaysiaZone));
        Assert.Null(edited.CountdownAt(now, MalaysiaZone));
        Assert.All(assignments.Upcoming(now, MalaysiaZone), u => Assert.Null(u.Countdown));
        Assert.Equal(My(2026, 10, 26, 0, 0), assignments.NextChange(now, MalaysiaZone));
    }

    [Fact]
    public void Upcoming_OverdueFirstThenNearestThenUndated()
    {
        var upcoming = Load().Upcoming(My(2026, 10, 25, 10, 0), MalaysiaZone);

        Assert.Equal(["kuiz2", "lab5", "fyp-bab3", "cadangan", "tarikh-rosak"], upcoming.Select(u => u.Assignment.Id));
        Assert.Null(upcoming[^1].Countdown);
    }

    [Fact]
    public void NextChange_IsMidnightOrAnEarlierDeadline()
    {
        var assignments = Load();

        Assert.Equal(My(2026, 10, 26, 0, 0), assignments.NextChange(My(2026, 10, 25, 22, 0), MalaysiaZone));
        Assert.Equal(My(2026, 10, 26, 8, 0), assignments.NextChange(My(2026, 10, 26, 7, 0), MalaysiaZone));
    }

    [Fact]
    public void CompleteAndReopen()
    {
        var now = My(2026, 10, 25, 10, 0);
        var assignments = Load();

        var done = assignments.Upsert(assignments.Find("lab5")!.Complete(now));
        var reopened = done.Upsert(done.Find("lab5")!.Reopen());

        Assert.DoesNotContain(done.Upcoming(now, MalaysiaZone), u => u.Assignment.Id == "lab5");
        Assert.Equal(now, done.Find("lab5")!.DoneAt);
        Assert.Contains(reopened.Upcoming(now, MalaysiaZone), u => u.Assignment.Id == "lab5");
        Assert.Equal(4, done.RemoveDone().Assignments.Count);
    }

    [Fact]
    public void SavedFile_NormalizeDropsUntitledAndClearsBadDates()
    {
        var assignments = Load();

        Assert.Equal(6, assignments.Assignments.Count);
        Assert.Null(assignments.Find("tanpa-tajuk"));
        Assert.Null(assignments.Find("tarikh-rosak")!.DueDate);
        Assert.Null(assignments.Find("tarikh-rosak")!.DueTime);
    }

    [Fact]
    public void SavedFile_RoundTripsAndKeepsUnknownFields()
    {
        var assignments = Load();

        var json = JsonSerializer.Serialize(assignments, StudentJsonContext.Default.AssignmentDocument);
        var again = JsonSerializer.Deserialize(json, StudentJsonContext.Default.AssignmentDocument)!;

        Assert.Contains("\"portal\": \"ufuture\"", json);
        Assert.Contains("\"dueDate\": \"2026-10-28\"", json);
        Assert.Equal(
            assignments.Assignments.Select(a => (a.Id, a.Title, a.Course, a.DueDate, a.DueTime, a.Done)),
            again.Assignments.Select(a => (a.Id, a.Title, a.Course, a.DueDate, a.DueTime, a.Done)));
    }
}
