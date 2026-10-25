using System.Text.Json;
using Atap.Core.Productivity.Timers;
using static Atap.Core.Tests.Productivity.TestZones;

namespace Atap.Core.Tests.Productivity.Timers;

public class StopwatchTests
{
    /// <summary>A stopwatch that ran 5 minutes, was stopped, then started again at 09:00 with two laps.</summary>
    private const string SavedWhileRunning = """
        {
          "schemaVersion": 1,
          "runningSince": "2026-10-25T09:00:00+08:00",
          "banked": "00:05:00",
          "laps": ["00:02:00", "00:06:30"]
        }
        """;

    [Fact]
    public void Restart_KeepsCountingFromTheSavedStart()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.StopwatchState)!;

        Assert.True(loaded.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(15), loaded.ElapsedAt(My(2026, 10, 25, 9, 10)));
    }

    [Fact]
    public void StopAndStart_BankTheTimeBetweenRuns()
    {
        var state = new StopwatchState().Start(My(2026, 10, 25, 9, 0));

        state = state.Stop(My(2026, 10, 25, 9, 3));
        var stoppedLater = state.ElapsedAt(My(2026, 10, 25, 12, 0));
        state = state.Start(My(2026, 10, 25, 12, 0));

        Assert.Equal(TimeSpan.FromMinutes(3), stoppedLater);
        Assert.Equal(TimeSpan.FromMinutes(4), state.ElapsedAt(My(2026, 10, 25, 12, 1)));
    }

    [Fact]
    public void Laps_GiveSplitsAndTotals()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.StopwatchState)!;

        var state = loaded.Lap(My(2026, 10, 25, 9, 5));

        Assert.Equal(
            [
                new StopwatchLap(1, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2)),
                new StopwatchLap(2, TimeSpan.FromMinutes(4.5), TimeSpan.FromMinutes(6.5)),
                new StopwatchLap(3, TimeSpan.FromMinutes(3.5), TimeSpan.FromMinutes(10)),
            ],
            state.LapTimes());
    }

    [Fact]
    public void Lap_WhileStoppedOrAtTheCap_DoesNothing()
    {
        var stopped = new StopwatchState();
        var full = new StopwatchState
        {
            RunningSince = My(2026, 10, 25, 9, 0),
            Laps = Enumerable.Range(1, StopwatchState.MaxLaps).Select(i => TimeSpan.FromSeconds(i)).ToList(),
        };

        Assert.Same(stopped, stopped.Lap(My(2026, 10, 25, 9, 0)));
        Assert.Same(full, full.Lap(My(2026, 10, 25, 9, 5)));
    }

    [Fact]
    public void ClockSetBackwards_NeverCountsDown()
    {
        var state = new StopwatchState { RunningSince = My(2026, 10, 25, 9, 0), Banked = TimeSpan.FromMinutes(2) };

        Assert.Equal(TimeSpan.FromMinutes(2), state.ElapsedAt(My(2026, 10, 25, 8, 0)));
    }

    [Fact]
    public void EmptyFile_KeepsDefaults()
    {
        var loaded = JsonSerializer.Deserialize("{}", TimersJsonContext.Default.StopwatchState)!;

        Assert.Equal(StopwatchState.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Empty(loaded.Laps);
        Assert.Empty(loaded.LapTimes());
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var loaded = JsonSerializer.Deserialize(SavedWhileRunning, TimersJsonContext.Default.StopwatchState)!;

        var reset = loaded.Reset();

        Assert.False(reset.IsRunning);
        Assert.Equal(TimeSpan.Zero, reset.ElapsedAt(My(2026, 10, 25, 10, 0)));
        Assert.Empty(reset.Laps);
    }
}
