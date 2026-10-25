using System.Diagnostics;
using Atap.Core.Diagnostics;
using Atap.Core.Scheduling;

namespace Atap.Platform.Diagnostics;

/// <summary>Takes one reading of what Atap uses. Tests pass their own.</summary>
internal interface IUsageProbe
{
    ResourceUsage Sample();
}

/// <summary>
/// Reads Atap's own process: memory, processor share since the last reading, threads and handles, plus the
/// scheduler, widgets and shared sources. Holds no timer: the resource meter asks for a reading while it shows.
/// </summary>
internal sealed class ProcessSampler(TimeProvider time, Func<SchedulerStats> scheduler, Func<int> runningWidgets, Func<int> sharedSources) : IUsageProbe
{
    private TimeSpan? _cpuBefore;
    private long _stampBefore;

    public ResourceUsage Sample()
    {
        using var process = Process.GetCurrentProcess();
        var stamp = time.GetTimestamp();
        var cpu = process.TotalProcessorTime;
        // The first reading has nothing to compare with, so it says 0 % until the next one.
        var share = _cpuBefore is { } before
            ? ResourceUsage.CpuShare(before, cpu, time.GetElapsedTime(_stampBefore, stamp), Environment.ProcessorCount)
            : 0;
        _cpuBefore = cpu;
        _stampBefore = stamp;

        var stats = scheduler();
        return new ResourceUsage(
            process.PrivateMemorySize64,
            process.WorkingSet64,
            GC.GetTotalMemory(forceFullCollection: false),
            share,
            process.Threads.Count,
            process.HandleCount,
            stats.ArmedItems,
            stats.WakesLastHour,
            runningWidgets(),
            sharedSources(),
            AppDomain.CurrentDomain.GetAssemblies().Length);
    }
}
