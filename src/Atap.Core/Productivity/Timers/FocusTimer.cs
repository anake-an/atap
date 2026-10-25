using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Timers;

[JsonConverter(typeof(CamelCaseEnumConverter<FocusPhase>))]
public enum FocusPhase
{
    Focus,
    ShortBreak,
    LongBreak,
}

[JsonConverter(typeof(CamelCaseEnumConverter<FocusRun>))]
public enum FocusRun
{
    Idle,
    Running,
    Paused,
}

/// <summary>Lengths and rules for the focus timer, read from the widget settings.</summary>
public sealed record FocusSettings
{
    /// <summary>The most time a single run can have, added time included.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(180);

    public TimeSpan Focus { get; init; } = TimeSpan.FromMinutes(25);

    public TimeSpan ShortBreak { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan LongBreak { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Focus sessions to finish before a long break.</summary>
    public int SessionsPerLongBreak { get; init; } = 4;

    public bool AutoStartBreaks { get; init; }

    public bool AutoStartFocus { get; init; }

    public TimeSpan LengthOf(FocusPhase phase) => phase switch
    {
        FocusPhase.ShortBreak => ShortBreak,
        FocusPhase.LongBreak => LongBreak,
        _ => Focus,
    };

    /// <summary>Focus between 1 and 180 minutes, breaks between 1 and 60, and 1 to 12 sessions per long break.</summary>
    public FocusSettings Clamped() => this with
    {
        Focus = ClampLength(Focus, MaxLength),
        ShortBreak = ClampLength(ShortBreak, TimeSpan.FromMinutes(60)),
        LongBreak = ClampLength(LongBreak, TimeSpan.FromMinutes(60)),
        SessionsPerLongBreak = Math.Clamp(SessionsPerLongBreak, 1, 12),
    };

    private static TimeSpan ClampLength(TimeSpan value, TimeSpan max)
    {
        var min = TimeSpan.FromMinutes(1);
        return value < min ? min : value > max ? max : value;
    }
}

/// <summary>
/// Saved state of a focus timer, kept in machine state. A running timer stores its end time, not a
/// countdown, so a restart resumes from the saved end time. Methods return a new state and never
/// read the clock.
/// </summary>
public sealed record FocusTimerState
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// How late the end of a phase can be handled and still count as on time. A late end (after a
    /// restart or sleep) never starts the next phase on its own.
    /// </summary>
    public static readonly TimeSpan OnTimeGrace = TimeSpan.FromMinutes(1);

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public FocusPhase Phase { get; set; }

    public FocusRun Run { get; set; }

    /// <summary>When the running phase ends. Set only while running.</summary>
    public DateTimeOffset? EndsAt { get; set; }

    /// <summary>Time left in the paused phase. Set only while paused.</summary>
    public TimeSpan? PausedRemaining { get; set; }

    /// <summary>Full length of the current run, added time included. Used for progress.</summary>
    public TimeSpan Length { get; set; }

    /// <summary>Focus sessions finished since the last long break.</summary>
    public int CompletedInCycle { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Running or paused. The widget is live while this is true.</summary>
    [JsonIgnore]
    public bool IsLive => Run != FocusRun.Idle;

    /// <summary>When the scheduler should call <see cref="Advance"/>. Null unless running.</summary>
    [JsonIgnore]
    public DateTimeOffset? NextDue => Run == FocusRun.Running ? EndsAt : null;

    /// <summary>
    /// Repairs the values of a state that was read from a file, so an odd value can never hold the
    /// notch. An unknown phase number becomes focus. An unknown run number, a running phase with no
    /// end time or a paused phase with no time left goes back to idle. An end time more than 180
    /// minutes after <paramref name="now"/> is brought back to 180 minutes. A value the reader
    /// cannot parse at all, such as "stopped" for the run, fails the whole read before this runs.
    /// </summary>
    public FocusTimerState Normalize(DateTimeOffset now)
    {
        var state = this with
        {
            Phase = Enum.IsDefined(Phase) ? Phase : FocusPhase.Focus,
            CompletedInCycle = Math.Max(CompletedInCycle, 0),
        };

        switch (Run)
        {
            case FocusRun.Running when EndsAt is { } end:
                var latest = now + FocusSettings.MaxLength;
                return state with { EndsAt = end > latest ? latest : end, PausedRemaining = null };
            case FocusRun.Paused when PausedRemaining is { } left && left > TimeSpan.Zero:
                return state with
                {
                    EndsAt = null,
                    PausedRemaining = left > FocusSettings.MaxLength ? FocusSettings.MaxLength : left,
                };
            default:
                return state with { Run = FocusRun.Idle, EndsAt = null, PausedRemaining = null, Length = TimeSpan.Zero };
        }
    }

    public TimeSpan RemainingAt(DateTimeOffset now)
    {
        var left = Run switch
        {
            FocusRun.Running when EndsAt is { } end => end - now,
            FocusRun.Paused => PausedRemaining ?? TimeSpan.Zero,
            _ => TimeSpan.Zero,
        };
        return left < TimeSpan.Zero ? TimeSpan.Zero : left > FocusSettings.MaxLength ? FocusSettings.MaxLength : left;
    }

    /// <summary>Share of the current run that has passed, from 0 to 1. Zero while idle.</summary>
    public double ProgressAt(DateTimeOffset now) =>
        Run == FocusRun.Idle || Length <= TimeSpan.Zero
            ? 0
            : Math.Clamp(1 - (RemainingAt(now) / Length), 0, 1);

    /// <summary>Starts the current phase from its full length, or resumes it when paused.</summary>
    public FocusTimerState Start(FocusSettings settings, DateTimeOffset now)
    {
        switch (Run)
        {
            case FocusRun.Running:
                return this;
            case FocusRun.Paused:
                return this with { Run = FocusRun.Running, EndsAt = now + RemainingAt(now), PausedRemaining = null };
            default:
                var length = settings.Clamped().LengthOf(Phase);
                return this with { Run = FocusRun.Running, EndsAt = now + length, PausedRemaining = null, Length = length };
        }
    }

    /// <summary>Pauses a running phase. A phase that already ended is left for <see cref="Advance"/>.</summary>
    public FocusTimerState Pause(DateTimeOffset now)
    {
        if (Run != FocusRun.Running || EndsAt is not { } end || end <= now)
            return this;
        return this with { Run = FocusRun.Paused, EndsAt = null, PausedRemaining = RemainingAt(now) };
    }

    /// <summary>Adds time to a running or paused phase, up to 180 minutes left.</summary>
    public FocusTimerState AddTime(TimeSpan extra, DateTimeOffset now)
    {
        if (Run == FocusRun.Idle || extra <= TimeSpan.Zero)
            return this;

        var left = RemainingAt(now);
        var room = FocusSettings.MaxLength - left;
        if (room <= TimeSpan.Zero)
            return this;
        if (extra > room)
            extra = room;

        return Run == FocusRun.Running
            ? this with { EndsAt = now + left + extra, Length = Length + extra }
            : this with { PausedRemaining = left + extra, Length = Length + extra };
    }

    /// <summary>Ends the current phase without counting it and waits at the next one.</summary>
    public FocusTimerState Skip() => this with
    {
        Phase = Phase == FocusPhase.Focus ? FocusPhase.ShortBreak : FocusPhase.Focus,
        Run = FocusRun.Idle,
        EndsAt = null,
        PausedRemaining = null,
        Length = TimeSpan.Zero,
        CompletedInCycle = Phase == FocusPhase.LongBreak ? 0 : CompletedInCycle,
    };

    /// <summary>Stops and goes back to a new cycle.</summary>
    public FocusTimerState Reset() => this with
    {
        Phase = FocusPhase.Focus,
        Run = FocusRun.Idle,
        EndsAt = null,
        PausedRemaining = null,
        Length = TimeSpan.Zero,
        CompletedInCycle = 0,
    };

    /// <summary>
    /// Ends the running phase if its end time has come. A focus phase counts toward the long break.
    /// The next phase starts on its own only when the settings say so and the end was handled on
    /// time. It then starts at the old end time, so a slightly late tick does not shift the cycle.
    /// At most one phase ends per call.
    /// </summary>
    public FocusAdvance Advance(FocusSettings settings, DateTimeOffset now)
    {
        if (Run != FocusRun.Running || EndsAt is not { } end || end > now)
            return new FocusAdvance(this, null);

        settings = settings.Clamped();
        var late = now - end > OnTimeGrace;
        var completed = CompletedInCycle;
        FocusPhase next;
        if (Phase == FocusPhase.Focus)
        {
            completed++;
            next = completed >= settings.SessionsPerLongBreak ? FocusPhase.LongBreak : FocusPhase.ShortBreak;
        }
        else
        {
            if (Phase == FocusPhase.LongBreak)
                completed = 0;
            next = FocusPhase.Focus;
        }

        var autoStart = !late && (next == FocusPhase.Focus ? settings.AutoStartFocus : settings.AutoStartBreaks);
        var length = settings.LengthOf(next);
        var state = this with
        {
            Phase = next,
            CompletedInCycle = completed,
            Run = autoStart ? FocusRun.Running : FocusRun.Idle,
            EndsAt = autoStart ? end + length : null,
            PausedRemaining = null,
            Length = autoStart ? length : TimeSpan.Zero,
        };
        return new FocusAdvance(state, new FocusPhaseEnd(Phase, end, late));
    }
}

/// <param name="Late">True when the end was handled after <see cref="FocusTimerState.OnTimeGrace"/>, for example after a restart.</param>
public readonly record struct FocusPhaseEnd(FocusPhase Phase, DateTimeOffset EndedAt, bool Late);

/// <param name="Ended">The phase that ended in this call, or null when nothing ended.</param>
public readonly record struct FocusAdvance(FocusTimerState State, FocusPhaseEnd? Ended);
