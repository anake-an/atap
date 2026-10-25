using Atap.Tests.Infrastructure;

namespace Atap.Tests.Scheduling;

/// <summary>
/// Widgets run later or repeatedly only through their scoped scheduler, so their wakes coalesce and end with
/// the instance. Clock changes and resume reach the scheduler from the systemClock feature, not from widgets.
/// </summary>
public sealed class WidgetTimerGuardTests
{
    private static readonly string[] Forbidden =
    [
        "DispatcherTimer",
        "PeriodicTimer",
        "new Timer(",
        "new System.Threading.Timer(",
        "System.Timers.Timer",
        ".CreateTimer(",
        "SystemEvents.TimeChanged",
        "SystemEvents.PowerModeChanged",
    ];

    [Fact]
    public void Widgets_OwnNoTimersOrClockEvents()
    {
        var problems = new List<string>();
        foreach (var file in RepoPaths.Files(Path.Combine(RepoPaths.App, "Widgets"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var pattern in Forbidden)
            {
                if (text.Contains(pattern, StringComparison.Ordinal))
                    problems.Add($"{RepoPaths.Relative(file)} uses {pattern}. Use Context.Scheduler instead.");
            }
        }

        Assert.Empty(problems);
    }
}
