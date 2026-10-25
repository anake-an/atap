using System.Runtime.CompilerServices;
using Atap.Platform.Diagnostics;
using Atap.Tests.Infrastructure;
using Xunit.Abstractions;

namespace Atap.Tests.Memory;

[Collection(MemoryProbe.Collection)]
public class MemoryTrimTests(ITestOutputHelper output)
{
    [Fact]
    public void Trim_GivesTheFreedHeapBackToWindows()
    {
        Churn();
        GC.Collect();
        var before = GC.GetGCMemoryInfo().TotalCommittedBytes;

        MemoryTrim.Now();

        var after = GC.GetGCMemoryInfo().TotalCommittedBytes;
        output.WriteLine($"GC committed before {before / 1024} KB, after {after / 1024} KB");
        // A normal collection keeps most of the 64 MB it freed for later. The trim gives it back.
        Assert.True(after < before - 16 * 1024 * 1024, $"Committed only went from {before / 1024} KB to {after / 1024} KB.");
    }

    [Fact]
    public async Task Soon_TrimsOnceTheUiThreadIsIdle()
    {
        Churn();
        GC.Collect();
        var before = GC.GetGCMemoryInfo().TotalCommittedBytes;

        Sta.Run(() =>
        {
            MemoryTrim.Soon(Sta.Dispatcher);
            MemoryTrim.Soon(Sta.Dispatcher);
        });
        await Sta.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.SystemIdle);

        Assert.True(GC.GetGCMemoryInfo().TotalCommittedBytes < before - 16 * 1024 * 1024);
    }

    // About 64 MB of short-lived objects, like a busy window leaves behind, kept alive long enough to reach the older generations.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Churn()
    {
        var kept = new List<byte[]>();
        for (var i = 0; i < 64 * 1024; i++)
            kept.Add(new byte[1000]);
        GC.Collect();
        GC.KeepAlive(kept);
    }
}
