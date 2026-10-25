namespace Atap.Core.Scheduling;

/// <summary>
/// The only way Atap code runs later or repeatedly. Every item shares one timer, armed only while
/// items exist. Callbacks run on the UI thread. Dispose the returned handle to cancel an item.
/// </summary>
public interface IScheduler
{
    /// <summary>Runs once at <paramref name="due"/>. A due time in the past runs on the next turn.</summary>
    IDisposable At(DateTimeOffset due, Action callback, ScheduleOptions options = default);

    /// <summary>Runs every <paramref name="period"/>, first after one period. Periods under 1 second are raised to 1 second.</summary>
    IDisposable Every(TimeSpan period, Action callback, ScheduleOptions options = default);

    /// <summary>Shared tick just after the start of each minute (hh:mm:00.020).</summary>
    IDisposable EveryMinute(Action callback);

    /// <summary>Shared tick just after the start of each second. Only for content visible in Expanded or Panel.</summary>
    IDisposable EverySecond(Action callback);
}

/// <param name="Slack">How late the item may run so it can share a wake with others. Zero for times the user expects.</param>
/// <param name="Deferrable">Periods are stretched four times while battery saver is on. Never for alarms or prayer reminders.</param>
/// <param name="Missed">What happens to a due time missed while the PC slept.</param>
public readonly record struct ScheduleOptions(TimeSpan Slack = default, bool Deferrable = false, MissedRun Missed = MissedRun.Skip);

public enum MissedRun
{
    /// <summary>
    /// Wait for the next slot. A missed one-time item is dropped, and a missed repeating item runs once in the
    /// shared refresh shortly after resume. The right choice for anything the user sees at a set time.
    /// </summary>
    Skip,

    /// <summary>Run once on resume, then carry on.</summary>
    RunOnce,
}

/// <summary>Control and statistics for the app, not for widgets.</summary>
public interface ISchedulerControl
{
    /// <summary>The wall clock or time zone changed. Aligned ticks are recomputed, and repeating items wait at most one period from now.</summary>
    void NotifyClockChanged();

    /// <summary>The PC resumed from sleep. Missed items follow their <see cref="MissedRun"/> policy.</summary>
    void NotifyResumed();

    SchedulerStats Stats { get; }

    /// <summary>True while battery saver stretches <see cref="ScheduleOptions.Deferrable"/> periods. The app sets it from <c>IPowerState.SaverActive</c>.</summary>
    bool SaverActive { get; }

    /// <summary>Raised when a callback throws. The other items still run.</summary>
    event EventHandler<SchedulerFault>? JobFailed;
}

/// <param name="ArmedItems">Items waiting to run.</param>
/// <param name="WakesLastHour">Timer wakes in the last hour.</param>
public readonly record struct SchedulerStats(int ArmedItems, int WakesLastHour);

public sealed record SchedulerFault(Exception Exception);
