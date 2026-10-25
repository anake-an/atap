using Atap.Core.Diagnostics;

namespace Atap.Core.Tests.Diagnostics;

public class ResourceUsageTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void CpuShare_IsTimeUsedOverTimeOnEveryCore()
    {
        // 100 ms of processor time in 2 s on 4 cores is 1.25 % of the whole processor.
        Assert.Equal(1.25, ResourceUsage.CpuShare(Ms(1000), Ms(1100), Ms(2000), 4), 6);
    }

    [Fact]
    public void CpuShare_NeverPassesAHundred()
    {
        Assert.Equal(100, ResourceUsage.CpuShare(Ms(0), Ms(5000), Ms(1000), 1));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-10, 4)]
    [InlineData(1000, 0)]
    public void CpuShare_IsZero_WithoutTimeOrCores(double elapsedMs, int cores)
    {
        Assert.Equal(0, ResourceUsage.CpuShare(Ms(0), Ms(10), Ms(elapsedMs), cores));
    }

    [Fact]
    public void CpuShare_IsZero_WhenTheCounterWentBack()
    {
        Assert.Equal(0, ResourceUsage.CpuShare(Ms(500), Ms(400), Ms(1000), 2));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1024 * 1024, 1)]
    [InlineData(1024 * 1024 * 3 / 2, 2)]
    [InlineData(45 * 1024 * 1024 + 1024, 45)]
    public void Megabytes_RoundsToWholeMegabytes(long bytes, int megabytes)
    {
        Assert.Equal(megabytes, ResourceUsage.Megabytes(bytes));
    }

    [Fact]
    public void Diagnostics_ListEveryNumber_InPlainEnglish()
    {
        var usage = new ResourceUsage(45L << 20, 80L << 20, 12L << 20, 0.25, 21, 512, 3, 60, 2, 1, 140);

        var text = usage.ToDiagnostics("1.0.0");

        Assert.Equal(
            [
                "Atap 1.0.0",
                "Memory (private): 45 MB",
                "Working set: 80 MB",
                "Managed heap: 12 MB",
                "CPU: 0.3 %",
                "Threads: 21",
                "Handles: 512",
                "Timers waiting: 3",
                "Wake-ups in the last hour: 60",
                "Widgets running: 2",
                "Shared sources: 1",
                "Assemblies: 140",
            ],
            text.Split(Environment.NewLine));
    }
}
