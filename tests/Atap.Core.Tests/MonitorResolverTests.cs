using Atap.Core.Placement;

namespace Atap.Core.Tests;

public class MonitorResolverTests
{
    private static readonly DisplayMonitor Laptop = new(@"\\?\DISPLAY#BOE0A1B#4&1&UID8388688#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "Built-in display", new PixelRect(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly DisplayMonitor External = new(@"\\?\DISPLAY#DELA0F4#5&2&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "DELL U2720Q", new PixelRect(1920, -360, 2560, 1440), IsPrimary: false);

    private static readonly IReadOnlyList<DisplayMonitor> Both = [External, Laptop];

    [Fact]
    public void Resolve_Primary_ReturnsThePrimaryMonitor()
    {
        var targets = MonitorResolver.Resolve(MonitorSelection.Primary(), Both);

        Assert.Equal(new[] { Laptop }, targets);
    }

    [Fact]
    public void Resolve_Specific_ReturnsTheChosenMonitor()
    {
        var targets = MonitorResolver.Resolve(MonitorSelection.Specific(External), Both);

        Assert.Equal(new[] { External }, targets);
    }

    [Fact]
    public void Resolve_Specific_MatchesDeviceIdIgnoringCase()
    {
        var selection = new MonitorSelection { Mode = MonitorMode.Specific, DeviceId = External.DeviceId.ToUpperInvariant() };

        Assert.Equal(new[] { External }, MonitorResolver.Resolve(selection, Both));
    }

    [Fact]
    public void Resolve_SpecificMonitorMissing_FallsBackToPrimary()
    {
        var targets = MonitorResolver.Resolve(MonitorSelection.Specific(External), [Laptop]);

        Assert.Equal(new[] { Laptop }, targets);
    }

    [Fact]
    public void Resolve_All_ReturnsEveryMonitor()
    {
        var targets = MonitorResolver.Resolve(MonitorSelection.All(), Both);

        Assert.Equal(Both, targets);
    }

    [Fact]
    public void Resolve_NoPrimaryFlag_UsesTheFirstMonitor()
    {
        var notPrimary = Laptop with { IsPrimary = false };

        Assert.Equal(new[] { External }, MonitorResolver.Resolve(null, [External, notPrimary]));
    }

    [Fact]
    public void Resolve_NoMonitors_ReturnsEmpty()
    {
        Assert.Empty(MonitorResolver.Resolve(MonitorSelection.All(), []));
        Assert.Empty(MonitorResolver.Resolve(MonitorSelection.Primary(), []));
    }
}
