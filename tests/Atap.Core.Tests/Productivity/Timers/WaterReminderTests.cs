using System.Text.Json;
using Atap.Core.Productivity;
using Atap.Core.Productivity.Timers;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Timers;

public class WaterReminderTests
{
    private static readonly WaterSettings Daytime = new(); // 08:00 to 22:00, every hour
    private static readonly WaterSettings NightShift = new() { ActiveFrom = new TimeOnly(20, 0), ActiveUntil = new TimeOnly(4, 0) };

    private const string SavedState = """
        {
          "schemaVersion": 1,
          "anchor": "2026-10-25T09:00:00+08:00",
          "day": "2026-10-25",
          "count": 3
        }
        """;

    /// <summary>The next reminder time. Fails unless the state it comes with is due at that time.</summary>
    private static DateTimeOffset Next(WaterState state, WaterSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        var (kept, due) = state.NextReminder(settings, now, zone);
        Assert.True(due > now, $"Reminder at {due} is not after {now}.");
        Assert.True(kept.IsDueAt(settings, due, zone), $"The state returned with {due} is not due then.");
        return due;
    }

    [Fact]
    public void InsideActiveHours_RemindsOneIntervalAfterTheAnchor()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 9, 0) };

        Assert.Equal(My(2026, 10, 25, 10, 0), Next(state, Daytime, My(2026, 10, 25, 9, 30), MalaysiaZone));
    }

    [Fact]
    public void DueAfterActiveHours_MovesToOneIntervalAfterTheNextStart()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 21, 30) };

        Assert.Equal(My(2026, 10, 26, 9, 0), Next(state, Daytime, My(2026, 10, 25, 21, 30), MalaysiaZone));
    }

    [Fact]
    public void DueExactlyAtTheEnd_CountsAsOutside()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 21, 0) };

        Assert.Equal(My(2026, 10, 26, 9, 0), Next(state, Daytime, My(2026, 10, 25, 21, 0), MalaysiaZone));
    }

    [Fact]
    public void EarlyMorning_WaitsForActiveHours()
    {
        var state = new WaterState();

        Assert.Equal(My(2026, 10, 25, 9, 0), Next(state, Daytime, My(2026, 10, 25, 6, 0), MalaysiaZone));
    }

    [Fact]
    public void HoursPastMidnight_RemindUntilTheEndThenWaitForTheEvening()
    {
        var beforeEnd = new WaterState { Anchor = My(2026, 10, 26, 2, 30) };
        var pastEnd = new WaterState { Anchor = My(2026, 10, 26, 3, 30) };

        Assert.Equal(My(2026, 10, 26, 3, 30), Next(beforeEnd, NightShift, My(2026, 10, 26, 2, 30), MalaysiaZone));
        Assert.Equal(My(2026, 10, 26, 21, 0), Next(pastEnd, NightShift, My(2026, 10, 26, 3, 30), MalaysiaZone));
    }

    [Fact]
    public void ReminderMissedWhileClosed_IsSkippedAndTheIntervalStartsFromNow()
    {
        var loaded = JsonSerializer.Deserialize(SavedState, TimersJsonContext.Default.WaterState)!;
        var now = My(2026, 10, 25, 14, 0);

        var (kept, due) = loaded.NextReminder(Daytime, now, MalaysiaZone);

        Assert.Equal(My(2026, 10, 25, 15, 0), due);
        Assert.Equal(now, kept.Anchor);
        Assert.Equal(3, kept.CountAt(now, Daytime, MalaysiaZone));
        Assert.True(kept.IsDueAt(Daytime, due, MalaysiaZone));
        Assert.False(loaded.IsDueAt(Daytime, due, MalaysiaZone));
    }

    [Fact]
    public void AtTheReminderTime_IsDue_ThenTheNextIsOneIntervalLater()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 9, 0) };
        var at = My(2026, 10, 25, 10, 0);

        Assert.False(state.IsDueAt(Daytime, My(2026, 10, 25, 9, 59), MalaysiaZone));
        Assert.True(state.IsDueAt(Daytime, at, MalaysiaZone));
        Assert.True(state.IsDueAt(Daytime, at + WaterState.OnTimeGrace, MalaysiaZone));
        Assert.Equal(My(2026, 10, 25, 11, 0), Next(state.Reminded(at), Daytime, at, MalaysiaZone));
        Assert.False(state.Reminded(at).IsDueAt(Daytime, at, MalaysiaZone));
    }

    [Fact]
    public void LateCallbackAfterResume_IsNotDue_AndTheSkipStartsTheIntervalFromNow()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 9, 0) };
        var resumed = My(2026, 10, 25, 10, 30);

        var skipped = state.Restart(resumed);
        var next = Next(skipped, Daytime, resumed, MalaysiaZone);

        Assert.False(state.IsDueAt(Daytime, resumed, MalaysiaZone));
        Assert.Equal(My(2026, 10, 25, 10, 0), state.ReminderAt(Daytime, resumed, MalaysiaZone));
        Assert.Equal(My(2026, 10, 25, 11, 30), next);
        Assert.True(skipped.IsDueAt(Daytime, next, MalaysiaZone));
        Assert.Equal(new WaterDue(skipped, next), state.NextReminder(Daytime, resumed, MalaysiaZone));
    }

    [Fact]
    public void ReminderMovedToTheMorning_IsNotSkippedBeforeItComes()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 21, 30) };
        var morning = My(2026, 10, 26, 9, 0);

        Assert.Equal(morning, Next(state, Daytime, My(2026, 10, 26, 8, 30), MalaysiaZone));
        Assert.True(state.IsDueAt(Daytime, morning, MalaysiaZone));
    }

    [Fact]
    public void NoAnchorOrAnchorInTheFuture_IsNeverDue()
    {
        var now = My(2026, 10, 25, 10, 0);

        Assert.False(new WaterState().IsDueAt(Daytime, now, MalaysiaZone));
        Assert.False(new WaterState { Anchor = My(2026, 12, 1, 9, 0) }.IsDueAt(Daytime, now, MalaysiaZone));
    }

    [Fact]
    public void Restart_AfterUnlock_StartsTheIntervalAgain()
    {
        var loaded = JsonSerializer.Deserialize(SavedState, TimersJsonContext.Default.WaterState)!;

        var unlocked = loaded.Restart(My(2026, 10, 25, 9, 50));

        Assert.Equal(My(2026, 10, 25, 10, 50), Next(unlocked, Daytime, My(2026, 10, 25, 9, 50), MalaysiaZone));
    }

    [Fact]
    public void AllDay_NeverMovesTheReminder()
    {
        var allDay = new WaterSettings { ActiveFrom = new TimeOnly(0, 0), ActiveUntil = new TimeOnly(0, 0) };
        var state = new WaterState { Anchor = My(2026, 10, 25, 23, 30) };

        Assert.Equal(My(2026, 10, 26, 0, 30), Next(state, allDay, My(2026, 10, 25, 23, 30), MalaysiaZone));
    }

    [Fact]
    public void IntervalLongerThanActiveHours_RemindsAtTheStart()
    {
        var shortWindow = new WaterSettings
        {
            Interval = TimeSpan.FromHours(2),
            ActiveFrom = new TimeOnly(12, 0),
            ActiveUntil = new TimeOnly(13, 0),
        };
        var state = new WaterState { Anchor = My(2026, 10, 25, 12, 30) };

        Assert.Equal(My(2026, 10, 26, 12, 0), Next(state, shortWindow, My(2026, 10, 25, 12, 30), MalaysiaZone));
    }

    [Fact]
    public void AnchorInTheFuture_IsTreatedAsNow()
    {
        var state = new WaterState { Anchor = My(2026, 12, 1, 9, 0) };
        var now = My(2026, 10, 25, 9, 0);

        var (kept, due) = state.NextReminder(Daytime, now, MalaysiaZone);

        Assert.Equal(My(2026, 10, 25, 10, 0), due);
        Assert.Equal(now, kept.Anchor);
        Assert.True(kept.IsDueAt(Daytime, due, MalaysiaZone));
        Assert.False(state.IsDueAt(Daytime, due, MalaysiaZone));
    }

    [Fact]
    public void NoAnchor_StartsTheIntervalFromNow()
    {
        var now = My(2026, 10, 25, 9, 0);

        var (kept, due) = new WaterState().NextReminder(Daytime, now, MalaysiaZone);

        Assert.Equal(My(2026, 10, 25, 10, 0), due);
        Assert.Equal(now, kept.Anchor);
        Assert.True(kept.IsDueAt(Daytime, due, MalaysiaZone));
    }

    [Fact]
    public void ReminderStillAhead_KeepsTheSameState()
    {
        var state = new WaterState { Anchor = My(2026, 10, 25, 9, 0), Day = new DateOnly(2026, 10, 25), Count = 2 };

        var next = state.NextReminder(Daytime, My(2026, 10, 25, 9, 30), MalaysiaZone);

        Assert.Same(state, next.State);
    }

    [Theory]
    [InlineData(8, 0, 22, 0, 45)]
    [InlineData(20, 0, 4, 0, 60)]
    [InlineData(7, 30, 19, 15, 90)]
    [InlineData(9, 0, 17, 0, 240)]
    public void EveryReminderOverAWeek_FallsInsideActiveHours(int fromHour, int fromMinute, int untilHour, int untilMinute, int intervalMinutes)
    {
        var settings = new WaterSettings
        {
            ActiveFrom = new TimeOnly(fromHour, fromMinute),
            ActiveUntil = new TimeOnly(untilHour, untilMinute),
            Interval = TimeSpan.FromMinutes(intervalMinutes),
        };
        var now = My(2026, 10, 25, 0, 0);
        var state = new WaterState();
        var reminders = 0;

        while (now < My(2026, 11, 1, 0, 0))
        {
            var next = state.NextReminder(settings, now, MalaysiaZone);
            Assert.True(next.State.IsDueAt(settings, next.Due, MalaysiaZone), $"Reminder at {next.Due} is not due then.");
            now = next.Due;
            Assert.True(settings.IsActiveAt(TimeOnly.FromDateTime(WallClock.ToLocal(now, MalaysiaZone))), $"Reminder at {now} is outside active hours.");
            state = next.State.Reminded(now);
            reminders++;
        }

        Assert.True(reminders >= 7);
    }

    [Fact]
    public void ActiveHours_StartIncludedEndExcluded()
    {
        Assert.True(Daytime.IsActiveAt(new TimeOnly(8, 0)));
        Assert.False(Daytime.IsActiveAt(new TimeOnly(22, 0)));
        Assert.True(NightShift.IsActiveAt(new TimeOnly(23, 0)));
        Assert.True(NightShift.IsActiveAt(new TimeOnly(3, 59)));
        Assert.False(NightShift.IsActiveAt(new TimeOnly(12, 0)));
    }

    [Fact]
    public void Count_StartsAgainOnANewDay()
    {
        var loaded = JsonSerializer.Deserialize(SavedState, TimersJsonContext.Default.WaterState)!;

        var later = loaded.Drink(My(2026, 10, 25, 11, 0), Daytime, MalaysiaZone);
        var nextDay = later.Drink(My(2026, 10, 26, 8, 30), Daytime, MalaysiaZone);

        Assert.Equal(4, later.CountAt(My(2026, 10, 25, 11, 0), Daytime, MalaysiaZone));
        Assert.Equal(My(2026, 10, 25, 11, 0), later.Anchor);
        Assert.Equal(0, later.CountAt(My(2026, 10, 26, 8, 0), Daytime, MalaysiaZone));
        Assert.Equal(1, nextDay.CountAt(My(2026, 10, 26, 8, 30), Daytime, MalaysiaZone));
    }

    [Fact]
    public void Count_PastMidnightBelongsToTheShiftThatStartedTheEveningBefore()
    {
        var evening = new WaterState().Drink(My(2026, 10, 25, 21, 0), NightShift, MalaysiaZone);

        var lateNight = evening.Drink(My(2026, 10, 26, 2, 0), NightShift, MalaysiaZone);

        Assert.Equal(new DateOnly(2026, 10, 25), lateNight.Day);
        Assert.Equal(2, lateNight.CountAt(My(2026, 10, 26, 3, 0), NightShift, MalaysiaZone));
    }

    [Fact]
    public void UndoDrink_TakesBackOneGlass()
    {
        var loaded = JsonSerializer.Deserialize(SavedState, TimersJsonContext.Default.WaterState)!;
        var now = My(2026, 10, 25, 12, 0);

        var undone = loaded.UndoDrink(now, Daytime, MalaysiaZone);
        var fresh = new WaterState();

        Assert.Equal(2, undone.CountAt(now, Daytime, MalaysiaZone));
        Assert.Same(fresh, fresh.UndoDrink(now, Daytime, MalaysiaZone));
    }
}
