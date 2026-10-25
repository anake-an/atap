using System.Text.Json;
using Atap.Core.Productivity.Timers;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Timers;

public class FocusTimerTests
{
    private static readonly FocusSettings Settings = new();
    private static readonly FocusSettings AutoSettings = new() { AutoStartBreaks = true, AutoStartFocus = true };

    /// <summary>State file written while a focus session ran from 10:00, as saved in machine state.</summary>
    private const string SavedWhileRunning = """
        {
          "schemaVersion": 1,
          "phase": "focus",
          "run": "running",
          "endsAt": "2026-10-25T10:25:00+08:00",
          "length": "00:25:00",
          "completedInCycle": 2,
          "soundPack": "lembut"
        }
        """;

    [Fact]
    public void Start_RunsTheFocusPhaseForItsLength()
    {
        var state = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        Assert.Equal(FocusRun.Running, state.Run);
        Assert.Equal(My(2026, 10, 25, 10, 25), state.EndsAt);
        Assert.Equal(TimeSpan.FromMinutes(25), state.Length);
        Assert.True(state.IsLive);
    }

    [Fact]
    public void Restart_ResumesFromTheSavedEndTime()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.FocusTimerState)!;
        var afterRestart = My(2026, 10, 25, 10, 10);

        var advance = loaded.Advance(Settings, afterRestart);

        Assert.Null(advance.Ended);
        Assert.Equal(FocusRun.Running, advance.State.Run);
        Assert.Equal(TimeSpan.FromMinutes(15), advance.State.RemainingAt(afterRestart));
        Assert.Equal(My(2026, 10, 25, 10, 25), advance.State.NextDue);
        Assert.Equal(2, advance.State.CompletedInCycle);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsStateAndKeepsUnknownFields()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.FocusTimerState)!;

        var json = JsonSerializer.Serialize(loaded, TimersJsonContext.Default.FocusTimerState);
        var again = JsonSerializer.Deserialize(json, TimersJsonContext.Default.FocusTimerState)!;

        Assert.Contains("\"soundPack\": \"lembut\"", json);
        Assert.Contains("\"run\": \"running\"", json);
        Assert.Equal(loaded.EndsAt, again.EndsAt);
        Assert.Equal(loaded.Phase, again.Phase);
        Assert.Equal(loaded.Length, again.Length);
    }

    [Fact]
    public void EndPassedWhileClosed_EndsOnceAsLateAndDoesNotAutoStart()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.FocusTimerState)!;
        var afterRestart = My(2026, 10, 25, 11, 0);

        var advance = loaded.Advance(AutoSettings, afterRestart);

        Assert.Equal(new FocusPhaseEnd(FocusPhase.Focus, My(2026, 10, 25, 10, 25), Late: true), advance.Ended);
        Assert.Equal(FocusPhase.ShortBreak, advance.State.Phase);
        Assert.Equal(FocusRun.Idle, advance.State.Run);
        Assert.Equal(3, advance.State.CompletedInCycle);

        var second = advance.State.Advance(AutoSettings, afterRestart);
        Assert.Null(second.Ended);
    }

    [Fact]
    public void EndHandledOnTime_AutoStartsTheBreakFromTheOldEndTime()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        var advance = running.Advance(AutoSettings, My(2026, 10, 25, 10, 25, 5));

        Assert.False(advance.Ended!.Value.Late);
        Assert.Equal(FocusPhase.ShortBreak, advance.State.Phase);
        Assert.Equal(FocusRun.Running, advance.State.Run);
        Assert.Equal(My(2026, 10, 25, 10, 30), advance.State.EndsAt);
    }

    [Fact]
    public void EndWithoutAutoStart_WaitsAtTheNextPhase()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        var advance = running.Advance(Settings, My(2026, 10, 25, 10, 25));

        Assert.Equal(FocusPhase.ShortBreak, advance.State.Phase);
        Assert.Equal(FocusRun.Idle, advance.State.Run);
        Assert.Null(advance.State.NextDue);
        Assert.False(advance.State.IsLive);
    }

    [Fact]
    public void FourthFocus_GivesALongBreak_ThenTheCycleStartsAgain()
    {
        var now = My(2026, 10, 25, 9, 0);
        var state = new FocusTimerState();
        var phases = new List<FocusPhase>();
        for (var i = 0; i < 8; i++)
        {
            state = state.Start(Settings, now);
            now = state.EndsAt!.Value;
            state = state.Advance(Settings, now).State;
            phases.Add(state.Phase);
        }

        Assert.Equal(
            [
                FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.ShortBreak, FocusPhase.Focus,
                FocusPhase.ShortBreak, FocusPhase.Focus, FocusPhase.LongBreak, FocusPhase.Focus,
            ],
            phases);
        Assert.Equal(0, state.CompletedInCycle);
    }

    [Fact]
    public void PauseAndResume_KeepTheTimeLeft()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        var paused = running.Pause(My(2026, 10, 25, 10, 10));
        var resumed = paused.Start(Settings, My(2026, 10, 25, 11, 0));

        Assert.Equal(FocusRun.Paused, paused.Run);
        Assert.Null(paused.NextDue);
        Assert.Equal(TimeSpan.FromMinutes(15), paused.RemainingAt(My(2026, 10, 25, 10, 50)));
        Assert.True(paused.IsLive);
        Assert.Equal(My(2026, 10, 25, 11, 15), resumed.EndsAt);
    }

    [Fact]
    public void Pause_AfterTheEnd_LeavesTheEndForAdvance()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        Assert.Same(running, running.Pause(My(2026, 10, 25, 10, 30)));
    }

    [Fact]
    public void AddTime_ExtendsTheRunAndStopsAt180MinutesLeft()
    {
        var start = My(2026, 10, 25, 10, 0);
        var running = new FocusTimerState().Start(Settings, start);

        var extended = running.AddTime(TimeSpan.FromMinutes(5), start);
        var capped = running.AddTime(TimeSpan.FromHours(10), start);

        Assert.Equal(My(2026, 10, 25, 10, 30), extended.EndsAt);
        Assert.Equal(TimeSpan.FromMinutes(30), extended.Length);
        Assert.Equal(TimeSpan.FromMinutes(180), capped.RemainingAt(start));
    }

    [Fact]
    public void Skip_MovesOnWithoutCounting()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        var skipped = running.Skip();

        Assert.Equal(FocusPhase.ShortBreak, skipped.Phase);
        Assert.Equal(FocusRun.Idle, skipped.Run);
        Assert.Equal(0, skipped.CompletedInCycle);
    }

    [Fact]
    public void Progress_FollowsTheRun()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        Assert.Equal(0.0, running.ProgressAt(My(2026, 10, 25, 10, 0)), 6);
        Assert.Equal(0.4, running.ProgressAt(My(2026, 10, 25, 10, 10)), 6);
        Assert.Equal(1.0, running.ProgressAt(My(2026, 10, 25, 10, 40)), 6);
    }

    [Fact]
    public void Clamped_KeepsSettingsInRange()
    {
        var clamped = new FocusSettings
        {
            Focus = TimeSpan.FromHours(9),
            ShortBreak = TimeSpan.Zero,
            SessionsPerLongBreak = 40,
        }.Clamped();

        Assert.Equal(TimeSpan.FromMinutes(180), clamped.Focus);
        Assert.Equal(TimeSpan.FromMinutes(1), clamped.ShortBreak);
        Assert.Equal(12, clamped.SessionsPerLongBreak);
    }

    [Theory]
    [InlineData("""{ "phase": "focus", "run": "running", "length": "00:25:00" }""")]
    [InlineData("""{ "phase": "focus", "run": 7, "endsAt": "2026-10-25T10:25:00+08:00", "length": "00:25:00" }""")]
    [InlineData("""{ "phase": "shortBreak", "run": "paused", "length": "00:05:00" }""")]
    [InlineData("""{ "phase": "focus", "run": "paused", "pausedRemaining": "-00:05:00", "length": "00:25:00" }""")]
    public void Normalize_BrokenRunGoesBackToIdleAndCanStartAgain(string json)
    {
        var now = My(2026, 10, 25, 10, 0);
        var loaded = JsonSerializer.Deserialize(json, TimersJsonContext.Default.FocusTimerState)!;

        var normalized = loaded.Normalize(now);

        Assert.True(loaded.IsLive);
        Assert.Equal(FocusRun.Idle, normalized.Run);
        Assert.False(normalized.IsLive);
        Assert.Null(normalized.NextDue);
        Assert.Equal(FocusRun.Running, normalized.Start(Settings, now).Run);
    }

    [Fact]
    public void Normalize_EndYearsAheadIsBroughtBackTo180Minutes()
    {
        var now = My(2026, 10, 25, 10, 0);
        var loaded = JsonSerializer.Deserialize(
            """{ "phase": 9, "run": "running", "endsAt": "2031-01-01T00:00:00+08:00", "length": "00:25:00", "completedInCycle": -3 }""",
            TimersJsonContext.Default.FocusTimerState)!;

        var normalized = loaded.Normalize(now);

        Assert.Equal(now + FocusSettings.MaxLength, normalized.NextDue);
        Assert.Equal(FocusPhase.Focus, normalized.Phase);
        Assert.Equal(0, normalized.CompletedInCycle);
    }

    [Fact]
    public void Normalize_KeepsAGoodStateAsItIs()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.FocusTimerState)!;
        var paused = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0)).Pause(My(2026, 10, 25, 10, 10));

        var normalized = loaded.Normalize(My(2026, 10, 25, 10, 10));

        Assert.Equal(FocusRun.Running, normalized.Run);
        Assert.Equal(My(2026, 10, 25, 10, 25), normalized.EndsAt);
        Assert.Equal(2, normalized.CompletedInCycle);
        Assert.Equal(paused, paused.Normalize(My(2026, 10, 25, 10, 20)));
    }

    [Fact]
    public void ClockSetBackwards_NeverShowsMoreThanTheLongestRun()
    {
        var running = new FocusTimerState().Start(Settings, My(2026, 10, 25, 10, 0));

        Assert.Equal(TimeSpan.FromMinutes(180), running.RemainingAt(My(2026, 10, 20, 10, 0)));
    }
}
