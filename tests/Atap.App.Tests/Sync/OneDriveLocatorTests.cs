using Atap.Platform.OneDrive;

namespace Atap.Tests.Sync;

public sealed class OneDriveLocatorTests
{
    private const string Personal = @"C:\Users\amin\OneDrive";
    private const string Consumer = @"C:\Users\amin\OneDrive - Personal";
    private const string Commercial = @"C:\Users\amin\OneDrive - Universiti Malaya";
    private const string Registry = @"D:\OneDrive";

    [Fact]
    public void Order_IsOneDrive_ThenConsumer_ThenCommercial_ThenTheRegistry()
    {
        var environment = new FakeEnvironment
        {
            Variables = { ["OneDrive"] = Personal, ["OneDriveConsumer"] = Consumer, ["OneDriveCommercial"] = Commercial },
            UserFolder = Registry,
            Folders = { Personal, Consumer, Commercial, Registry },
        };

        Assert.Equal(Personal, OneDriveLocator.FindRoot(environment));

        environment.Variables.Remove("OneDrive");
        Assert.Equal(Consumer, OneDriveLocator.FindRoot(environment));

        environment.Variables.Remove("OneDriveConsumer");
        Assert.Equal(Commercial, OneDriveLocator.FindRoot(environment));

        environment.Variables.Remove("OneDriveCommercial");
        Assert.Equal(Registry, OneDriveLocator.FindRoot(environment));

        environment.UserFolder = null;
        Assert.Null(OneDriveLocator.FindRoot(environment));
    }

    [Fact]
    public void AFolderThatIsGoneOrNotAFullPath_IsSkipped()
    {
        var environment = new FakeEnvironment
        {
            Variables = { ["OneDrive"] = Personal, ["OneDriveConsumer"] = @"OneDrive", ["OneDriveCommercial"] = "  " },
            UserFolder = Registry,
            Folders = { Registry, "OneDrive" },
        };

        Assert.Equal(Registry, OneDriveLocator.FindRoot(environment));
    }

    [Fact]
    public void TheSyncFolder_IsAppsAtap_UnderTheChosenFolderFirst()
    {
        var environment = new FakeEnvironment
        {
            Variables = { ["OneDrive"] = Personal },
            Folders = { Personal, Registry },
        };

        Assert.Equal(Path.Combine(Personal, "Apps", "Atap"), OneDriveLocator.SyncFolder(null, environment));
        Assert.Equal(Path.Combine(Registry, "Apps", "Atap"), OneDriveLocator.SyncFolder(Registry, environment));
        // A chosen folder that is gone falls back to the one found.
        Assert.Equal(Path.Combine(Personal, "Apps", "Atap"), OneDriveLocator.SyncFolder(@"E:\Gone", environment));

        environment.Variables.Clear();
        Assert.Null(OneDriveLocator.SyncFolder(string.Empty, environment));
    }

    private sealed class FakeEnvironment : IOneDriveEnvironment
    {
        public Dictionary<string, string?> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? UserFolder { get; set; }

        public HashSet<string> Folders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetVariable(string name) => Variables.GetValueOrDefault(name);

        public string? GetPersonalUserFolder() => UserFolder;

        public bool FolderExists(string path) => Folders.Contains(path);
    }
}
