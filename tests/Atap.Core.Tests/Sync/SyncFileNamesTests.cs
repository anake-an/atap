using Atap.Core.Sync;

namespace Atap.Core.Tests.Sync;

public sealed class SyncFileNamesTests
{
    [Theory]
    [InlineData("atap-sync-DESKTOP-1234.json")]
    [InlineData("atap-sync-DESKTOP-1234-2.json")]
    [InlineData("ATAP-SYNC-laptop.JSON")]
    [InlineData(@"C:\Users\aniq\OneDrive\Apps\Atap\atap-sync-PC.json")]
    public void IsConflictCopy_OneDriveCopies(string name)
    {
        Assert.True(SyncFileNames.IsConflictCopy(name));
    }

    [Theory]
    [InlineData(SyncFileNames.Bundle)]
    [InlineData(SyncFileNames.Temp)]
    [InlineData("atap-sync-.json")]
    [InlineData("atap-sync-PC.json.tmp")]
    [InlineData("layout.json")]
    [InlineData("atap-syncPC.json")]
    public void IsConflictCopy_OtherFiles(string name)
    {
        Assert.False(SyncFileNames.IsConflictCopy(name));
    }
}
