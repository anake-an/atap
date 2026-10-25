using System.IO;
using Atap.Core.Alerts;
using Atap.Features.Alerts;

namespace Atap.Tests.Alerts;

/// <summary>Which sound files Atap opens. Nothing here plays sound.</summary>
public sealed class SoundFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    public SoundFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ALocalFile_Opens()
    {
        var path = Write("azan.mp3", 16);

        using var stream = MediaFoundationPlayback.OpenLocalFile(path, _ => DriveType.Fixed);

        Assert.Equal(16, stream.Length);
    }

    [Theory]
    [InlineData(DriveType.Network)]
    [InlineData(DriveType.NoRootDirectory)]
    [InlineData(DriveType.Unknown)]
    public void AFileOnANetworkDrive_IsRefused(DriveType type)
    {
        // A mapped Z: passes the path check, but it is still another computer.
        var path = Write("azan.mp3", 16);

        Assert.Throws<IOException>(() => MediaFoundationPlayback.OpenLocalFile(path, _ => type));
    }

    [Theory]
    [InlineData("COM1.wav")]
    [InlineData("lpt9.mp3")]
    [InlineData("NUL.ogg")]
    [InlineData("con.wav")]
    public void AWindowsDeviceName_IsRefused(string name)
    {
        Assert.Throws<IOException>(() => MediaFoundationPlayback.OpenLocalFile(Path.Combine(_folder, name), _ => DriveType.Fixed));
    }

    [Fact]
    public void AFileOverTheLimit_IsRefused()
    {
        var path = Write("long.wav", 101);

        Assert.Throws<IOException>(() => MediaFoundationPlayback.OpenLocalFile(path, _ => DriveType.Fixed, maxBytes: 100));
    }

    [Theory]
    [InlineData("push", "file:C:\\Music\\a.mp3", AlertSounds.Windows)]
    [InlineData("push#2", "file:C:\\Music\\a.mp3", AlertSounds.Windows)]
    [InlineData("push", AlertSounds.Default, AlertSounds.Default)]
    [InlineData("prayer", "file:C:\\Music\\a.mp3", "file:C:\\Music\\a.mp3")]
    public void PushedAlerts_NeverOpenAFile(string source, string key, string expected)
    {
        var alert = new ShownAlert(source, new Alert("k", AlertPriority.Normal, "Title") { SoundKey = key }, DateTimeOffset.UnixEpoch);

        Assert.Equal(expected, AlertsFeature.SoundKeyFor(alert));
    }

    private string Write(string name, int bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }
}
