using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Timers;

/// <summary>
/// Saved state of a stopwatch, kept in machine state. A running stopwatch stores when it started,
/// so it keeps counting across a restart. Methods return a new state and never read the clock.
/// </summary>
public sealed record StopwatchState
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxLaps = 99;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>When the current run started. Null while stopped.</summary>
    public DateTimeOffset? RunningSince { get; set; }

    /// <summary>Time counted by earlier runs.</summary>
    public TimeSpan Banked { get; set; }

    /// <summary>Total time at each lap, oldest first.</summary>
    public IReadOnlyList<TimeSpan> Laps { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool IsRunning => RunningSince is not null;

    /// <summary>Total time counted. A clock set backwards never makes it go down.</summary>
    public TimeSpan ElapsedAt(DateTimeOffset now)
    {
        var banked = Banked < TimeSpan.Zero ? TimeSpan.Zero : Banked;
        return RunningSince is { } since && now > since ? banked + (now - since) : banked;
    }

    public StopwatchState Start(DateTimeOffset now) => IsRunning ? this : this with { RunningSince = now };

    public StopwatchState Stop(DateTimeOffset now) =>
        IsRunning ? this with { Banked = ElapsedAt(now), RunningSince = null } : this;

    public StopwatchState Reset() => this with { RunningSince = null, Banked = TimeSpan.Zero, Laps = [] };

    /// <summary>Records a lap while running. Laps after the 99th are ignored.</summary>
    public StopwatchState Lap(DateTimeOffset now)
    {
        var laps = Laps ?? [];
        if (!IsRunning || laps.Count >= MaxLaps)
            return this;
        return this with { Laps = [.. laps, ElapsedAt(now)] };
    }

    /// <summary>Each lap with its own length and the total at that point, oldest first.</summary>
    public IReadOnlyList<StopwatchLap> LapTimes()
    {
        var laps = Laps ?? [];
        var result = new List<StopwatchLap>(laps.Count);
        var previous = TimeSpan.Zero;
        for (var i = 0; i < laps.Count; i++)
        {
            result.Add(new StopwatchLap(i + 1, laps[i] - previous, laps[i]));
            previous = laps[i];
        }
        return result;
    }
}

public readonly record struct StopwatchLap(int Number, TimeSpan Split, TimeSpan Total);
