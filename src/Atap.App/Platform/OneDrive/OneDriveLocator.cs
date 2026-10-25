using System.IO;
using System.Security;
using Atap.Core.Sync;
using Microsoft.Win32;

namespace Atap.Platform.OneDrive;

/// <summary>Where OneDrive might be on this PC, so tests never read the real environment or registry.</summary>
internal interface IOneDriveEnvironment
{
    /// <summary>A user environment variable, or null when it is not set.</summary>
    string? GetVariable(string name);

    /// <summary>HKCU\Software\Microsoft\OneDrive\Accounts\Personal\UserFolder, or null when it is not there.</summary>
    string? GetPersonalUserFolder();

    bool FolderExists(string path);
}

/// <summary>The real environment, registry and file system.</summary>
internal sealed class WindowsOneDriveEnvironment : IOneDriveEnvironment
{
    public const string PersonalAccountKey = @"Software\Microsoft\OneDrive\Accounts\Personal";

    public string? GetVariable(string name) => Environment.GetEnvironmentVariable(name);

    public string? GetPersonalUserFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalAccountKey);
            return key?.GetValue("UserFolder") as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public bool FolderExists(string path) => Directory.Exists(path);
}

/// <summary>
/// Finds the OneDrive folder without asking OneDrive or the network: %OneDrive%, then %OneDriveConsumer%,
/// then %OneDriveCommercial%, then the personal account's UserFolder in the registry. The first one that
/// is a full path to a folder that exists wins.
/// </summary>
internal static class OneDriveLocator
{
    public static IReadOnlyList<string> Variables { get; } = ["OneDrive", "OneDriveConsumer", "OneDriveCommercial"];

    /// <summary>The OneDrive folder, or null when none is found.</summary>
    public static string? FindRoot(IOneDriveEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        foreach (var name in Variables)
        {
            if (Usable(environment, environment.GetVariable(name)) is { } found)
                return found;
        }
        return Usable(environment, environment.GetPersonalUserFolder());
    }

    /// <summary>
    /// The folder that holds atap-sync.json: <c>&lt;root&gt;\Apps\Atap</c>. The root is the folder the user chose
    /// (sync.folder) when it still exists, otherwise the OneDrive folder found on this PC. Null when there is neither.
    /// </summary>
    public static string? SyncFolder(string? chosenRoot, IOneDriveEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var root = Usable(environment, chosenRoot) ?? FindRoot(environment);
        return root is null ? null : Path.Combine(root, SyncFileNames.FolderUnderOneDrive);
    }

    private static string? Usable(IOneDriveEnvironment environment, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = path.Trim().Trim('"');
        return Path.IsPathFullyQualified(path) && environment.FolderExists(path) ? path : null;
    }
}
