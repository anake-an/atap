using Atap.Core.Alerts;

namespace Atap.Core.Tests.Alerts;

public class AlertSoundsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("beep")]
    [InlineData("http://example.com/azan.mp3")]
    public void NoKeyOrAnUnknownKey_PlaysNothing(string? key)
    {
        Assert.Null(AlertSounds.Resolve(key, AlertSounds.Windows));
    }

    [Fact]
    public void Windows_IsTheNotificationSound()
    {
        Assert.Equal(new AlertSound(AlertSoundKind.Windows, string.Empty), AlertSounds.Resolve(AlertSounds.Windows, null));
    }

    [Fact]
    public void Default_FollowsTheUsersPick()
    {
        Assert.Equal(AlertSoundKind.Windows, AlertSounds.Resolve(AlertSounds.Default, AlertSounds.Windows)!.Kind);
        Assert.Equal(@"C:\Sounds\chime.wav", AlertSounds.Resolve(AlertSounds.Default, AlertSounds.ForFile(@"C:\Sounds\chime.wav"))!.Value);
        Assert.Null(AlertSounds.Resolve(AlertSounds.Default, null));
        Assert.Null(AlertSounds.Resolve(AlertSounds.Default, AlertSounds.Default));
    }

    [Theory]
    [InlineData(@"C:\Sounds\azan.mp3")]
    [InlineData(@"d:\Music\Adhan Zohor.ogg")]
    [InlineData("E:/sounds/bell.WAV")]
    [InlineData(@"C:\Users\Aniq\Music\chime.m4a")]
    public void LocalAudioFiles_AreUsed(string path)
    {
        var sound = AlertSounds.Resolve(AlertSounds.ForFile(path), null);

        Assert.Equal(new AlertSound(AlertSoundKind.File, path), sound);
    }

    [Theory]
    [InlineData(@"\\nas\music\azan.mp3")]
    [InlineData("//nas/music/azan.mp3")]
    [InlineData(@"\\?\C:\Sounds\azan.mp3")]
    [InlineData(@"\\.\C:\Sounds\azan.mp3")]
    [InlineData(@"Sounds\azan.mp3")]
    [InlineData(@"C:azan.mp3")]
    [InlineData(@"C:\Sounds\azan.exe")]
    [InlineData(@"C:\Sounds\azan")]
    [InlineData(@"C:\Sounds\azan.mp3:stream")]
    [InlineData("")]
    public void NetworkRelativeAndOddPaths_AreRefused(string path)
    {
        Assert.False(AlertSounds.IsUsableFile(path));
        Assert.Null(AlertSounds.Resolve(AlertSounds.ForFile(path), null));
    }

    [Theory]
    [InlineData("azan.ogg", true)]
    [InlineData("chime-subuh.mp3", true)]
    [InlineData("Azan.ogg", false)]
    [InlineData("../azan.ogg", false)]
    [InlineData(@"sounds\azan.ogg", false)]
    [InlineData(".ogg", false)]
    [InlineData("azan.txt", false)]
    public void BundledNames_AreLowerCaseFileNames(string name, bool usable)
    {
        Assert.Equal(usable, AlertSounds.IsUsableBundledName(name));
        Assert.Equal(usable ? new AlertSound(AlertSoundKind.Bundled, name) : null, AlertSounds.Resolve(AlertSounds.ForBundled(name), null));
    }

    [Theory]
    [InlineData(AlertPriority.Normal, false, false, true)]
    [InlineData(AlertPriority.High, true, false, false)]
    [InlineData(AlertPriority.High, false, true, false)]
    [InlineData(AlertPriority.Overlay, true, true, false)]
    [InlineData(AlertPriority.Critical, true, false, true)]
    [InlineData(AlertPriority.Critical, false, true, true)]
    [InlineData(AlertPriority.Critical, true, true, true)]
    public void OnlyCriticalSounds_PlayWhileQuietOrHidden(AlertPriority priority, bool quiet, bool hidden, bool plays)
    {
        Assert.Equal(plays, AlertSounds.MayPlay(priority, quiet, hidden));
    }
}
