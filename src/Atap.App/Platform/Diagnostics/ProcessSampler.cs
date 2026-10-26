using System.Diagnostics;
using System.Runtime.InteropServices;
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
            AppDomain.CurrentDomain.GetAssemblies().Length,
            PrivateWorkingSet(process.Handle));
    }

    /// <summary>What Task Manager shows as memory: private pages in RAM. 0 when Windows does not report it.</summary>
    private static long PrivateWorkingSet(IntPtr process)
    {
        var counters = new ProcessMemoryCountersEx2 { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>() };
        return GetProcessMemoryInfo(process, ref counters, counters.Size) ? (long)counters.PrivateWorkingSetSize : 0;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx2 counters, uint size);

    // PROCESS_MEMORY_COUNTERS_EX2, Windows 10 1809 and later. Older Windows fills the shorter part and leaves the rest 0.
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCountersEx2
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }
}
