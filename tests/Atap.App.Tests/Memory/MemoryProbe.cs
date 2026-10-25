using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Memory;

/// <summary>Helpers for the memory tests: off-screen windows, a settled heap and the numbers the About page shows.</summary>
internal static class MemoryProbe
{
    /// <summary>The memory tests run alone, after the rest, so other tests do not move the numbers.</summary>
    public const string Collection = "Memory";

    /// <summary>A window far off every screen that never takes focus, so a test run does not flash on the desktop.</summary>
    public static Window OffscreenWindow() => new()
    {
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -20000,
        Top = -20000,
        Width = 900,
        Height = 700,
    };

    /// <summary>Lets the UI thread finish its queued work and lay something out, then collects, a few times over.</summary>
    public static async Task SettleAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            await Sta.Dispatcher.InvokeAsync(Elsewhere, DispatcherPriority.ContextIdle);
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    // The app keeps laying out its other windows, and WPF tidies its caches of item lists after a layout pass.
    private static void Elsewhere()
    {
        var list = new System.Windows.Controls.ItemsControl { ItemsSource = new[] { "a", "b" } };
        list.Measure(new Size(100, 100));
        list.Arrange(new Rect(0, 0, 100, 100));
        list.UpdateLayout();
    }

    /// <summary>Live managed heap, GC committed memory, private bytes and handles. Call it off the UI thread.</summary>
    public static string Numbers()
    {
        using var process = Process.GetCurrentProcess();
        var heap = GC.GetTotalMemory(forceFullCollection: true);
        var committed = GC.GetGCMemoryInfo().TotalCommittedBytes;
        return $"heap {heap / 1024} KB, GC committed {committed / 1024} KB, private {process.PrivateMemorySize64 / 1024} KB, "
            + $"handles {process.HandleCount}, user objects {GetGuiResources(process.Handle, 1)}";
    }

    /// <summary>
    /// Fails with the type names of everything still alive. Other tests on the shared UI thread can hold WPF caches
    /// a little longer, so it keeps settling for up to 3 seconds before it calls something a leak.
    /// </summary>
    public static async Task AssertCollectedAsync(IEnumerable<WeakReference> watched)
    {
        var list = watched.ToList();
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (list.Any(w => w.IsAlive) && DateTime.UtcNow < until)
        {
            await SettleAsync();
            await Task.Delay(50);
        }
        var alive = list.Where(w => w.IsAlive).Select(w => w.Target?.GetType().Name).Distinct().ToList();
        Assert.True(alive.Count == 0, "Still in memory after closing: " + string.Join(", ", alive));
    }

    [DllImport("user32.dll")]
    private static extern int GetGuiResources(IntPtr process, int flags);
}

[CollectionDefinition(MemoryProbe.Collection, DisableParallelization = true)]
public sealed class MemoryCollection;
