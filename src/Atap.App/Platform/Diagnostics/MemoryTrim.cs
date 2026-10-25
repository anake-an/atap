using System.Runtime;
using System.Windows.Threading;
using Atap.Interop;

namespace Atap.Platform.Diagnostics;

/// <summary>
/// Gives memory back to Windows after a big window such as Settings has closed. A full collection hands the freed
/// heap back, then Windows trims the working set. Without it the heap stays as large as it was at its busiest.
/// It runs once the UI thread is idle, so closing stays quick, and several closes in a row trim once.
/// </summary>
internal static class MemoryTrim
{
    private static int _queued;

    /// <summary>Trims once the UI thread has nothing else to do.</summary>
    public static void Soon(Dispatcher dispatcher)
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1)
            return;
        dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            Interlocked.Exchange(ref _queued, 0);
            Now();
        });
    }

    /// <summary>Trims right away. Takes a few tens of milliseconds on the calling thread.</summary>
    public static void Now()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        // Aggressive also returns the free parts of the heap to Windows, which a normal collection keeps for later.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
    }
}
