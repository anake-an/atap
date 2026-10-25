namespace Atap.Tests.Infrastructure;

/// <summary>Finds the repository from the test output folder, for guards that scan source files.</summary>
internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string App => Path.Combine(Root, "src", "Atap.App");

    public static string Src => Path.Combine(Root, "src");

    /// <summary>Source files under <paramref name="folder"/>, without bin and obj.</summary>
    public static IEnumerable<string> Files(string folder, string pattern) =>
        Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Atap.sln")))
                return directory.FullName;
        }
        throw new InvalidOperationException("Atap.sln was not found above the test output folder.");
    }
}
