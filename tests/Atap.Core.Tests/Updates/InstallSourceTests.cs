using Atap.Core.Updates;

namespace Atap.Core.Tests.Updates;

public class InstallSourceTests
{
    [Theory]
    [InlineData(@"C:\Users\aina\scoop\apps\atap\current\Atap.exe")]
    [InlineData(@"C:\ProgramData\scoop\apps\atap\1.0.0\Atap.exe")]
    [InlineData(@"C:\Users\aina\SCOOP\Apps\atap\current\Atap.exe")]
    [InlineData("C:/Users/aina/scoop/apps/atap/current/Atap.exe")]
    public void TheUsualScoopFolders_AreScoop(string path)
    {
        Assert.Equal(InstallSource.Scoop, InstallSources.Detect(path));
    }

    [Fact]
    public void AScoopMovedSomewhereCustom_IsFoundThroughItsRoot()
    {
        Assert.Equal(InstallSource.Scoop, InstallSources.Detect(@"D:\tools\apps\atap\current\Atap.exe", null, @"D:\tools\"));
    }

    [Theory]
    [InlineData(@"C:\Users\aina\AppData\Local\Atap\current\Atap.exe")]
    [InlineData(@"D:\Portable\Atap\current\Atap.exe")]
    [InlineData(@"C:\Users\scoop\Desktop\Atap\Atap.exe")]
    [InlineData(@"C:\apps\scoop\Atap.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void AnythingElse_UpdatesItself(string? path)
    {
        Assert.Equal(InstallSource.Atap, InstallSources.Detect(path, @"C:\Users\aina\scoop", null));
    }
}
