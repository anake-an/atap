using Atap.Core.Privacy;

namespace Atap.Core.Tests.Privacy;

public class ConsentUsageTests
{
    [Fact]
    public void InUse_NeedsStartAndNoStop()
    {
        Assert.True(ConsentUsage.IsInUse(new(Sensor.Camera, "a", true, 5, 0)));
        Assert.False(ConsentUsage.IsInUse(new(Sensor.Camera, "a", true, 5, 9)));
        Assert.False(ConsentUsage.IsInUse(new(Sensor.Camera, "a", true, 0, 0)));
    }

    [Fact]
    public void Names_ComeFromThePathOrThePackage()
    {
        Assert.Equal("Zoom", ConsentUsage.AppName(new(Sensor.Camera, @"C:#Program Files#Zoom#bin#Zoom.exe", false, 1, 0)));
        Assert.Equal("WindowsCamera", ConsentUsage.AppName(new(Sensor.Camera, "Microsoft.WindowsCamera_8wekyb3d8bbwe", true, 1, 0)));
        Assert.Equal(@"C:\Program Files\Zoom\bin\Zoom.exe", ConsentUsage.ExePath(new(Sensor.Camera, @"C:#Program Files#Zoom#bin#Zoom.exe", false, 1, 0)));
    }

    [Fact]
    public void AppsThatAreNotRunning_AreIgnored()
    {
        ConsentEntry[] entries =
        [
            new(Sensor.Microphone, @"C:#Apps#Discord.exe", false, 1, 0),
            new(Sensor.Microphone, @"C:#Apps#Crashed.exe", false, 1, 0),
            new(Sensor.Camera, @"C:#Apps#Discord.exe", false, 1, 0),
            new(Sensor.Camera, @"C:#Other#discord.exe", false, 1, 0),
            new(Sensor.Camera, "Microsoft.WindowsCamera_8wekyb3d8bbwe", true, 1, 0),
            new(Sensor.Camera, @"C:#Apps#Done.exe", false, 1, 2),
        ];

        var used = ConsentUsage.InUse(entries, path => !path.Contains("Crashed", StringComparison.Ordinal));

        Assert.Equal(
            ["Camera Discord", "Camera WindowsCamera", "Microphone Discord"],
            used.Select(u => $"{u.Sensor} {u.AppName}"));
    }
}
