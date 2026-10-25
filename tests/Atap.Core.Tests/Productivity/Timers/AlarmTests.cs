using System.Text.Json;
using Atap.Core.Productivity.Timers;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Timers;

public class AlarmTests
{
    private static readonly AlarmDays MondayToFriday = AlarmDaysExtensions.Of(
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

    /// <summary>
    /// Alarm file as saved: a work alarm last handled on Friday 23 October, a sahur alarm that is
    /// turned off, a one-time alarm, a hand-added alarm with no id or armed time, a repeated id and a null.
    /// </summary>
    private const string SavedAlarms = """
        {
          "schemaVersion": 1,
          "alarms": [
            {
              "id": "kerja",
              "label": "Bangun kerja",
              "time": "06:30",
              "days": "monday, tuesday, wednesday, thursday, friday",
              "enabled": true,
              "armedAt": "2026-10-01T22:00:00+08:00",
              "handledThrough": "2026-10-23T06:30:00+08:00"
            },
            {
              "id": "sahur",
              "label": "Sahur",
              "time": "04:45",
              "days": "everyday",
              "enabled": false,
              "armedAt": "2026-03-01T21:00:00+08:00"
            },
            {
              "id": "ubat",
              "label": "Makan ubat",
              "time": "21:00",
              "days": "none",
              "armedAt": "2026-10-25T08:00:00+08:00",
              "tone": "loceng"
            },
            { "label": "  Tambahan sendiri  ", "time": "7:15", "days": "saturday" },
            { "id": "kerja", "label": "Salinan", "time": "08:00", "days": "none", "armedAt": "2026-10-25T08:00:00+08:00" },
            null
          ]
        }
        """;

    private static Alarm Daily(TimeOnly time, DateTimeOffset armedAt) =>
        Alarm.Create("a", time, AlarmDays.Everyday, "Bangun", armedAt);

    [Fact]
    public void NextOccurrence_LaterToday()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 25, 6, 0));

        Assert.Equal(My(2026, 10, 25, 7, 0), alarm.NextOccurrenceAfter(My(2026, 10, 25, 6, 0), MalaysiaZone));
    }

    [Fact]
    public void NextOccurrence_TimePassedToday_IsTomorrow()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 25, 8, 0));

        Assert.Equal(My(2026, 10, 26, 7, 0), alarm.NextOccurrenceAfter(My(2026, 10, 25, 7, 0), MalaysiaZone));
    }

    [Fact]
    public void NextOccurrence_WeekdaysOnFridayEvening_IsMonday()
    {
        var alarm = Alarm.Create("a", new TimeOnly(6, 30), MondayToFriday, "Kerja", My(2026, 10, 1, 9, 0));

        Assert.Equal(My(2026, 10, 26, 6, 30), alarm.NextOccurrenceAfter(My(2026, 10, 23, 20, 0), MalaysiaZone));
    }

    [Fact]
    public void NextOccurrence_OneDayAWeekAfterItsTime_IsNextWeek()
    {
        var alarm = Alarm.Create("a", new TimeOnly(9, 0), AlarmDays.Sunday, "Pasar", My(2026, 10, 1, 9, 0));

        Assert.Equal(My(2026, 11, 1, 9, 0), alarm.NextOccurrenceAfter(My(2026, 10, 25, 9, 30), MalaysiaZone));
    }

    [Fact]
    public void Check_BeforeItsTime_Waits()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 25, 7, 0)), alarm.Check(My(2026, 10, 25, 6, 0), MalaysiaZone));
    }

    [Fact]
    public void Check_JustAfterItsTime_Rings()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Ringing, My(2026, 10, 25, 7, 0)), alarm.Check(My(2026, 10, 25, 7, 1), MalaysiaZone));
    }

    [Fact]
    public void Check_RingWindowEdge_TwoMinutesRingsOneSecondMoreIsMissed()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0));
        var at = My(2026, 10, 25, 7, 0);

        Assert.Equal(TimeSpan.FromMinutes(2), Alarm.RingWindow);
        Assert.Equal(new AlarmStatus(AlarmState.Ringing, at), alarm.Check(My(2026, 10, 25, 7, 2, 0), MalaysiaZone));
        Assert.Equal(new AlarmStatus(AlarmState.Missed, at), alarm.Check(My(2026, 10, 25, 7, 2, 1), MalaysiaZone));
    }

    [Fact]
    public void Check_AfterRestartPastItsTime_IsMissedOnlyOnce()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0));
        var startedAt = My(2026, 10, 25, 7, 30);

        var first = alarm.Check(startedAt, MalaysiaZone);
        var handled = alarm.MarkHandled(first.Occurrence!.Value);
        var second = handled.Check(startedAt, MalaysiaZone);

        Assert.Equal(new AlarmStatus(AlarmState.Missed, My(2026, 10, 25, 7, 0)), first);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), second);
    }

    [Fact]
    public void Check_AfterDaysAway_ReportsOnlyTheLatestMissedOccurrence()
    {
        var alarms = JsonSerializer.Deserialize(SavedAlarms, TimersJsonContext.Default.AlarmDocument)!
            .Normalize(My(2026, 10, 28, 9, 0));
        var work = alarms.Find("kerja")!;
        var backOnWednesday = My(2026, 10, 28, 9, 0);

        var status = work.Check(backOnWednesday, MalaysiaZone);
        var handled = work.MarkHandled(status.Occurrence!.Value);

        Assert.Equal(new AlarmStatus(AlarmState.Missed, My(2026, 10, 28, 6, 30)), status);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 29, 6, 30)), handled.Check(backOnWednesday, MalaysiaZone));
    }

    [Fact]
    public void Check_BackJustAfterItsTimeAfterDaysAway_RingsTheLatestOccurrence()
    {
        var daily = Daily(new TimeOnly(7, 0), My(2026, 10, 1, 22, 0)).MarkHandled(My(2026, 10, 23, 7, 0));
        var work = JsonSerializer.Deserialize(SavedAlarms, TimersJsonContext.Default.AlarmDocument)!
            .Normalize(My(2026, 10, 27, 6, 31))
            .Find("kerja")!;

        Assert.Equal(new AlarmStatus(AlarmState.Ringing, My(2026, 10, 26, 7, 0)), daily.Check(My(2026, 10, 26, 7, 1), MalaysiaZone));
        Assert.Equal(new AlarmStatus(AlarmState.Ringing, My(2026, 10, 27, 6, 30)), work.Check(My(2026, 10, 27, 6, 31), MalaysiaZone));
    }

    [Fact]
    public void MissedSnoozeOnARepeatingAlarm_IsReportedOnce()
    {
        var snoozed = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0))
            .MarkHandled(My(2026, 10, 25, 7, 0))
            .Snooze(My(2026, 10, 25, 7, 0), TimeSpan.FromMinutes(10));
        var wokeUp = My(2026, 10, 26, 9, 0);

        var missed = snoozed.Check(wokeUp, MalaysiaZone);
        var handled = snoozed.MarkHandled(missed.Occurrence!.Value);

        Assert.Equal(new AlarmStatus(AlarmState.Missed, My(2026, 10, 26, 7, 0)), missed);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 27, 7, 0)), handled.Check(wokeUp, MalaysiaZone));
    }

    [Fact]
    public void MissedSnoozeTheSameDay_IsReportedOnceAtTheSnoozeEnd()
    {
        var snoozed = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0))
            .MarkHandled(My(2026, 10, 25, 7, 0))
            .Snooze(My(2026, 10, 25, 7, 0), TimeSpan.FromMinutes(10));
        var wokeUp = My(2026, 10, 25, 7, 30);

        var missed = snoozed.Check(wokeUp, MalaysiaZone);
        var handled = snoozed.MarkHandled(missed.Occurrence!.Value);

        Assert.Equal(new AlarmStatus(AlarmState.Missed, My(2026, 10, 25, 7, 10)), missed);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), handled.Check(wokeUp, MalaysiaZone));
    }

    [Fact]
    public void ClockSteppedBackAfterItRang_DoesNotRingAgain()
    {
        var rang = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0)).MarkHandled(My(2026, 10, 25, 7, 0));
        var snoozeRang = rang.Snooze(My(2026, 10, 25, 7, 0), TimeSpan.FromMinutes(10)).MarkHandled(My(2026, 10, 25, 7, 10));
        var steppedBack = My(2026, 10, 25, 6, 55);

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), rang.Check(steppedBack, MalaysiaZone));
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), snoozeRang.Check(steppedBack, MalaysiaZone));
    }

    [Fact]
    public void OneTimeAlarm_TurnsOffAfterItRings()
    {
        var alarm = Alarm.Create("a", new TimeOnly(21, 0), AlarmDays.None, "Makan ubat", My(2026, 10, 25, 8, 0));

        var ringing = alarm.Check(My(2026, 10, 25, 21, 0), MalaysiaZone);
        var handled = alarm.MarkHandled(ringing.Occurrence!.Value);

        Assert.Equal(AlarmState.Ringing, ringing.State);
        Assert.False(handled.Enabled);
        Assert.Equal(new AlarmStatus(AlarmState.Off, null), handled.Check(My(2026, 10, 26, 21, 0), MalaysiaZone));
    }

    [Fact]
    public void OneTimeAlarmMissedWhileClosed_IsMissedOnceThenOff()
    {
        var alarm = Alarm.Create("a", new TimeOnly(21, 0), AlarmDays.None, "Makan ubat", My(2026, 10, 25, 8, 0));
        var startedAt = My(2026, 10, 26, 8, 0);

        var missed = alarm.Check(startedAt, MalaysiaZone);
        var handled = alarm.MarkHandled(missed.Occurrence!.Value);

        Assert.Equal(new AlarmStatus(AlarmState.Missed, My(2026, 10, 25, 21, 0)), missed);
        Assert.False(handled.Enabled);
        Assert.Equal(new AlarmStatus(AlarmState.Off, null), handled.Check(startedAt, MalaysiaZone));
        Assert.Equal(new AlarmStatus(AlarmState.Off, null), handled.Check(My(2026, 10, 26, 21, 0), MalaysiaZone));
    }

    [Fact]
    public void Snooze_RingsAgainThenGoesBackToTheSchedule()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 24, 22, 0));
        var rang = alarm.MarkHandled(My(2026, 10, 25, 7, 0));

        var snoozed = rang.Snooze(My(2026, 10, 25, 7, 0, 20), TimeSpan.FromMinutes(5));
        var snoozeRings = snoozed.Check(My(2026, 10, 25, 7, 5, 20), MalaysiaZone);
        var afterSnooze = snoozed.MarkHandled(snoozeRings.Occurrence!.Value);

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 25, 7, 5, 20)), snoozed.Check(My(2026, 10, 25, 7, 1), MalaysiaZone));
        Assert.Equal(AlarmState.Ringing, snoozeRings.State);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), afterSnooze.Check(My(2026, 10, 25, 7, 6), MalaysiaZone));
    }

    [Fact]
    public void SnoozeOnAOneTimeAlarm_RingsThenTurnsOff()
    {
        var alarm = Alarm.Create("a", new TimeOnly(21, 0), AlarmDays.None, null, My(2026, 10, 25, 8, 0));
        var rang = alarm.MarkHandled(My(2026, 10, 25, 21, 0));

        var snoozed = rang.Snooze(My(2026, 10, 25, 21, 0), TimeSpan.FromMinutes(10));
        var done = snoozed.MarkHandled(snoozed.Check(My(2026, 10, 25, 21, 10), MalaysiaZone).Occurrence!.Value);

        Assert.True(snoozed.Enabled);
        Assert.False(done.Enabled);
    }

    [Fact]
    public void TurnOn_NeverReportsAnOccurrenceFromBeforeIt()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 1, 22, 0)).TurnOff();

        var on = alarm.TurnOn(My(2026, 10, 25, 9, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), on.Check(My(2026, 10, 25, 9, 0), MalaysiaZone));
    }

    [Fact]
    public void Reschedule_ArmsTheNewTimeFromNow()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 1, 22, 0));

        var moved = alarm.Reschedule(new TimeOnly(9, 30), AlarmDays.Sunday, My(2026, 10, 25, 9, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 25, 9, 30)), moved.Check(My(2026, 10, 25, 9, 0), MalaysiaZone));
    }

    [Fact]
    public void TimeSkippedByDaylightSaving_RingsWhenTheClockJumpsPast()
    {
        var alarm = Alarm.Create("a", new TimeOnly(1, 30), AlarmDays.Everyday, null, Utc(2026, 3, 28, 12));

        var next = alarm.NextOccurrenceAfter(Utc(2026, 3, 28, 12), Seasonal);

        Assert.Equal(Utc(2026, 3, 29, 1, 30), next);
    }

    [Fact]
    public void TimeThatHappensTwice_RingsOnceOnTheFirst()
    {
        var alarm = Alarm.Create("a", new TimeOnly(1, 30), AlarmDays.Everyday, null, Utc(2026, 10, 24, 12));

        var first = alarm.Check(Utc(2026, 10, 25, 0, 30), Seasonal);
        var handled = alarm.MarkHandled(first.Occurrence!.Value);
        var atSecondOneThirty = handled.Check(Utc(2026, 10, 25, 1, 30), Seasonal);

        Assert.Equal(new AlarmStatus(AlarmState.Ringing, Utc(2026, 10, 25, 0, 30)), first);
        Assert.Equal(new AlarmStatus(AlarmState.Waiting, Utc(2026, 10, 26, 1, 30)), atSecondOneThirty);
    }

    [Fact]
    public void ClockSteppedBackBeforeItWasSet_DoesNotRingEarly()
    {
        // Set at 09:00 for 07:00, so it is meant for tomorrow. Then the clock steps back to 06:59.
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 25, 9, 0));
        var oneTime = Alarm.Create("b", new TimeOnly(7, 0), AlarmDays.None, null, My(2026, 10, 25, 9, 0));
        var tomorrow = new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0));

        Assert.Equal(tomorrow, alarm.Check(My(2026, 10, 25, 6, 59), MalaysiaZone));
        Assert.Equal(tomorrow, alarm.Check(My(2026, 10, 25, 7, 0), MalaysiaZone));
        Assert.Equal(tomorrow, oneTime.Check(My(2026, 10, 25, 6, 59), MalaysiaZone));
    }

    [Fact]
    public void SetWhileTheClockWasFarAhead_StillRingsOnceTheClockIsRight()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2027, 10, 25, 9, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), alarm.Check(My(2026, 10, 25, 9, 0), MalaysiaZone));
    }

    [Fact]
    public void ClockSetBackwards_StillWaitsForTheNextOccurrence()
    {
        var alarm = Daily(new TimeOnly(7, 0), My(2026, 10, 1, 22, 0)).MarkHandled(My(2026, 10, 30, 7, 0));

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), alarm.Check(My(2026, 10, 25, 9, 0), MalaysiaZone));
    }

    [Fact]
    public void SavedFile_LoadsAndNormalizes()
    {
        var now = My(2026, 10, 25, 9, 0);

        var alarms = JsonSerializer.Deserialize(SavedAlarms, TimersJsonContext.Default.AlarmDocument)!.Normalize(now);

        Assert.Equal(5, alarms.Alarms.Count);
        Assert.Equal(alarms.Alarms.Count, alarms.Alarms.Select(a => a.Id).Distinct().Count());
        var work = alarms.Find("kerja")!;
        Assert.Equal(new TimeOnly(6, 30), work.Time);
        Assert.Equal(MondayToFriday, work.Days);
        Assert.False(alarms.Find("sahur")!.Enabled);
        Assert.False(alarms.Find("ubat")!.Repeats);
        var handAdded = alarms.Alarms[3];
        Assert.Equal("Tambahan sendiri", handAdded.Label);
        Assert.Equal(new TimeOnly(7, 15), handAdded.Time);
        Assert.Equal(now, handAdded.ArmedAt);
        Assert.Equal("Salinan", alarms.Alarms[4].Label);
        Assert.NotEqual("kerja", alarms.Alarms[4].Id);
    }

    [Fact]
    public void SavedFile_RoundTripsDaysAndUnknownFields()
    {
        var alarms = JsonSerializer.Deserialize(SavedAlarms, TimersJsonContext.Default.AlarmDocument)!
            .Normalize(My(2026, 10, 25, 9, 0));

        var json = JsonSerializer.Serialize(alarms, TimersJsonContext.Default.AlarmDocument);
        var again = JsonSerializer.Deserialize(json, TimersJsonContext.Default.AlarmDocument)!;

        Assert.Contains("\"days\": \"monday, tuesday, wednesday, thursday, friday\"", json);
        Assert.Contains("\"time\": \"06:30\"", json);
        Assert.Contains("\"tone\": \"loceng\"", json);
        Assert.Equal(alarms.Alarms.Select(a => (a.Id, a.Time, a.Days, a.Enabled)), again.Alarms.Select(a => (a.Id, a.Time, a.Days, a.Enabled)));
    }

    [Fact]
    public void Document_NextDue_PicksTheEarliestEnabledAlarm()
    {
        var now = My(2026, 10, 25, 9, 0);
        var alarms = JsonSerializer.Deserialize(SavedAlarms, TimersJsonContext.Default.AlarmDocument)!.Normalize(now);

        var next = alarms.NextDue(now, MalaysiaZone);

        Assert.Equal("ubat", next!.Value.Alarm.Id);
        Assert.Equal(My(2026, 10, 25, 21, 0), next.Value.Due);
    }

    [Fact]
    public void Document_UpsertStopsAtTheCap()
    {
        var now = My(2026, 10, 25, 9, 0);
        var document = new AlarmDocument();
        for (var i = 0; i < AlarmDocument.MaxAlarms + 3; i++)
            document = document.Upsert(Alarm.Create($"a{i}", new TimeOnly(7, 0), AlarmDays.None, null, now));

        Assert.Equal(AlarmDocument.MaxAlarms, document.Alarms.Count);
        Assert.True(document.IsFull);
        Assert.Equal(AlarmDocument.MaxAlarms - 1, document.Remove("a0").Alarms.Count);
    }

    [Fact]
    public void MissingFields_KeepTheirDefaults()
    {
        var document = JsonSerializer.Deserialize("""{ "alarms": [ { "id": "a", "time": "07:00" } ] }""", TimersJsonContext.Default.AlarmDocument)!;

        Assert.Equal(AlarmDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.True(document.Alarms[0].Enabled);
        Assert.Equal(string.Empty, document.Alarms[0].Label);
    }

    [Fact]
    public void NotNormalized_WithNoArmedTime_WaitsInsteadOfReportingMissed()
    {
        var alarm = new Alarm { Id = "a", Time = new TimeOnly(7, 0), Days = AlarmDays.Everyday };

        Assert.Equal(new AlarmStatus(AlarmState.Waiting, My(2026, 10, 26, 7, 0)), alarm.Check(My(2026, 10, 25, 9, 0), MalaysiaZone));
    }

    [Fact]
    public void Days_Includes()
    {
        Assert.True(MondayToFriday.Includes(DayOfWeek.Friday));
        Assert.False(MondayToFriday.Includes(DayOfWeek.Sunday));
        Assert.True(AlarmDays.Everyday.Includes(DayOfWeek.Saturday));
    }
}
