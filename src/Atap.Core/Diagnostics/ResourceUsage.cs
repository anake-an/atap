namespace Atap.Core.Diagnostics;

/// <summary>What Atap uses at one moment, for the resource meter. Nothing here leaves the PC.</summary>
/// <param name="PrivateBytes">Memory Windows has set aside for Atap alone, used or not, graphics driver buffers included.</param>
/// <param name="WorkingSet">Memory Atap has in RAM right now, shared parts included.</param>
/// <param name="ManagedHeap">Memory the .NET garbage collector holds.</param>
/// <param name="CpuPercent">Share of the whole processor since the last sample, 0 to 100.</param>
/// <param name="Threads">Threads in the process.</param>
/// <param name="Handles">Windows handles the process holds.</param>
/// <param name="ArmedTimers">Scheduler items waiting to run.</param>
/// <param name="WakesLastHour">Times the scheduler woke in the last hour.</param>
/// <param name="RunningWidgets">Widget copies running.</param>
/// <param name="SharedSources">Shared Windows sources in use, such as media sessions.</param>
/// <param name="Assemblies">.NET assemblies loaded.</param>
/// <param name="PrivateWorkingSet">Memory only Atap uses that is in RAM right now. The number Task Manager shows as memory. 0 when Windows does not report it.</param>
public sealed record ResourceUsage(
    long PrivateBytes,
    long WorkingSet,
    long ManagedHeap,
    double CpuPercent,
    int Threads,
    int Handles,
    int ArmedTimers,
    int WakesLastHour,
    int RunningWidgets,
    int SharedSources,
    int Assemblies,
    long PrivateWorkingSet = 0)
{
    /// <summary>The memory to show first: the same number as Task Manager, or the set-aside memory where that is not known.</summary>
    public long Memory => PrivateWorkingSet > 0 ? PrivateWorkingSet : PrivateBytes;

    /// <summary>Whole megabytes, rounded, as the meter and Task Manager show them.</summary>
    public static int Megabytes(long bytes) => (int)Math.Round(bytes / (1024.0 * 1024.0), MidpointRounding.AwayFromZero);

    /// <summary>
    /// The processor share between two readings of the process's total processor time: the time used, over the
    /// time passed on every core. Always 0 to 100. A clock that went backwards or no time passed gives 0.
    /// </summary>
    public static double CpuShare(TimeSpan usedBefore, TimeSpan usedAfter, TimeSpan elapsed, int cores)
    {
        if (elapsed <= TimeSpan.Zero || cores <= 0 || usedAfter < usedBefore)
            return 0;
        var share = (usedAfter - usedBefore).TotalMilliseconds / (elapsed.TotalMilliseconds * cores) * 100;
        return Math.Clamp(share, 0, 100);
    }

    /// <summary>
    /// The same numbers as plain text, one per line, for "Copy diagnostics". Labels stay in English, so the text
    /// reads the same in a bug report whatever language Atap shows.
    /// </summary>
    public string ToDiagnostics(string version) => string.Join(
        Environment.NewLine,
        $"Atap {version}",
        $"Memory (as in Task Manager): {Megabytes(Memory)} MB",
        $"Memory set aside (private bytes): {Megabytes(PrivateBytes)} MB",
        $"Working set: {Megabytes(WorkingSet)} MB",
        $"Managed heap: {Megabytes(ManagedHeap)} MB",
        $"CPU: {CpuPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} %",
        $"Threads: {Threads}",
        $"Handles: {Handles}",
        $"Timers waiting: {ArmedTimers}",
        $"Wake-ups in the last hour: {WakesLastHour}",
        $"Widgets running: {RunningWidgets}",
        $"Shared sources: {SharedSources}",
        $"Assemblies: {Assemblies}");
}
