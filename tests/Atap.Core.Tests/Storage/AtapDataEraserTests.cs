using System.Diagnostics;
using Atap.Core.Storage;

namespace Atap.Core.Tests.Storage;

/// <summary>"Delete all my Atap data" on temp folders laid out like a real PC: AppData\Roaming and AppData\Local.</summary>
public sealed class AtapDataEraserTests : IDisposable
{
    private readonly TempFolder _folder = new();

    private string RoamingRoot => Path.Combine(_folder.Path, "Roaming");

    private string LocalRoot => Path.Combine(_folder.Path, "Local");

    private AtapPaths Paths => new(Path.Combine(RoamingRoot, "Atap"), Path.Combine(LocalRoot, "AtapData"));

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Erase_DeletesBothDataFolders_AndEverythingInThem()
    {
        var paths = Paths;
        Write(paths.SettingsFile);
        Write(paths.LayoutFile);
        Write(Path.Combine(paths.UserData, "todo", "lists.json"));
        Write(Path.Combine(paths.Roaming, "logs", "atap-20261026.log"));
        Write(paths.SecretsFile);
        Write(Path.Combine(paths.Cache, "http", "a.cache"));
        Write(Path.Combine(paths.MachineState, "clipboard", "history.json"));

        var result = AtapDataEraser.Erase(paths);

        Assert.False(Directory.Exists(paths.Roaming));
        Assert.False(Directory.Exists(paths.Local));
        Assert.Equal(0, result.Left);
        Assert.True(result.Deleted >= 7);
    }

    [Fact]
    public void Erase_KeepsTheInstalledProgram_AndEverythingBesideTheDataFolders()
    {
        var paths = Paths;
        Write(paths.SettingsFile);
        Write(paths.SecretsFile);
        // Velopack installs to %LOCALAPPDATA%\Atap, next to AtapData.
        var program = Path.Combine(LocalRoot, "Atap", "current", "Atap.exe");
        var updater = Path.Combine(LocalRoot, "Atap", "Update.exe");
        var packages = Path.Combine(LocalRoot, "Atap", "packages", "Atap-1.1.1-full.nupkg");
        var other = Path.Combine(RoamingRoot, "Atap-other", "keep.txt");
        var beside = Path.Combine(RoamingRoot, "keep.txt");
        Write(program);
        Write(updater);
        Write(packages);
        Write(other);
        Write(beside);

        AtapDataEraser.Erase(paths);

        Assert.All([program, updater, packages, other, beside], file => Assert.True(File.Exists(file), file));
    }

    [Fact]
    public void Erase_WithoutTheFolders_DoesNothing()
    {
        var result = AtapDataEraser.Erase(Paths);

        Assert.Equal(new EraseResult(0, 0), result);
    }

    [Fact]
    public void Erase_DeletesHiddenAndReadOnlyFiles()
    {
        var paths = Paths;
        var hidden = Path.Combine(paths.Roaming, "hidden.json");
        var readOnly = Path.Combine(paths.Local, "readonly.json");
        Write(hidden);
        Write(readOnly);
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);

        var result = AtapDataEraser.Erase(paths);

        Assert.Equal(0, result.Left);
        Assert.False(Directory.Exists(paths.Roaming));
        Assert.False(Directory.Exists(paths.Local));
    }

    [Fact]
    public void Erase_LeavesAFileInUse_AndSaysSo()
    {
        var paths = Paths;
        var locked = Path.Combine(paths.Local, "cache", "locked.cache");
        Write(locked);
        Write(paths.SettingsFile);

        EraseResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = AtapDataEraser.Erase(paths);

        Assert.True(result.Left > 0);
        Assert.True(File.Exists(locked));
        Assert.False(Directory.Exists(paths.Roaming));
    }

    [Fact]
    public void Erase_RemovesAJunctionInside_ButNeverWhatItPointsTo()
    {
        var paths = Paths;
        var outside = Path.Combine(_folder.Path, "Documents");
        var precious = Path.Combine(outside, "thesis.docx");
        Write(precious);
        Directory.CreateDirectory(paths.Roaming);
        var link = Path.Combine(paths.Roaming, "linked");
        Junction(link, outside);
        Assert.True(File.Exists(Path.Combine(link, "thesis.docx")));

        AtapDataEraser.Erase(paths);

        Assert.True(File.Exists(precious));
        Assert.False(Directory.Exists(paths.Roaming));
    }

    [Fact]
    public void Erase_WhenTheDataFolderItselfIsAJunction_RemovesOnlyTheLink()
    {
        var paths = Paths;
        var outside = Path.Combine(_folder.Path, "Elsewhere");
        var precious = Path.Combine(outside, "keep.txt");
        Write(precious);
        Directory.CreateDirectory(RoamingRoot);
        Junction(paths.Roaming, outside);

        AtapDataEraser.Erase(paths);

        Assert.True(File.Exists(precious));
        Assert.False(Directory.Exists(paths.Roaming));
    }

    [Theory]
    [InlineData("Roaming", "AtapData")]
    [InlineData("Roaming\\Atap", "Local")]
    [InlineData("Roaming\\Atap", "Local\\Atap")]
    [InlineData("Roaming", "Local")]
    [InlineData("Roaming\\Atap\\..\\Microsoft", "Local\\AtapData")]
    public void Erase_RefusesFoldersThatAreNotAtapsOwn_AndDeletesNothing(string roaming, string local)
    {
        var paths = new AtapPaths(Path.Combine(_folder.Path, roaming), Path.Combine(_folder.Path, local));
        var roamingFile = Path.Combine(paths.Roaming, "keep.txt");
        var localFile = Path.Combine(paths.Local, "keep.txt");
        Write(roamingFile);
        Write(localFile);

        Assert.Throws<InvalidOperationException>(() => AtapDataEraser.Erase(paths));

        Assert.True(File.Exists(roamingFile));
        Assert.True(File.Exists(localFile));
    }

    [Theory]
    [InlineData(@"C:\Users\Aisyah\AppData\Roaming\Atap", "Atap", true)]
    [InlineData(@"C:\Users\Aisyah\AppData\Roaming\Atap\", "Atap", true)]
    [InlineData(@"C:\Users\Aisyah\AppData\Local\AtapData", "AtapData", true)]
    [InlineData(@"C:\Users\Aisyah\AppData\Local\Atap", "AtapData", false)]
    [InlineData(@"C:\Users\Aisyah\AppData\Roaming", "Atap", false)]
    [InlineData(@"C:\", "Atap", false)]
    [InlineData(@"Atap", "Atap", false)]
    [InlineData(@"..\Atap", "Atap", false)]
    [InlineData(@"\\server\share\Atap", "Atap", false)]
    [InlineData("", "Atap", false)]
    [InlineData(null, "Atap", false)]
    public void IsOwnFolder_AcceptsOnlyAFullLocalPathEndingInTheName(string? path, string name, bool expected)
    {
        Assert.Equal(expected, AtapDataEraser.IsOwnFolder(path, name));
    }

    private static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    // A junction needs no admin rights, unlike a symbolic link.
    private static void Junction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
