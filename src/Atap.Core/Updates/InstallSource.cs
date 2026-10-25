namespace Atap.Core.Updates;

/// <summary>Who put Atap on this PC, which decides who updates it.</summary>
public enum InstallSource
{
    /// <summary>Atap's own installer or portable zip: Atap updates itself.</summary>
    Atap,

    /// <summary>Scoop: Scoop updates it, so Atap's own updater stays off.</summary>
    Scoop,
}

/// <summary>Works out the <see cref="InstallSource"/> from where Atap runs. Pure, so tests need no install.</summary>
public static class InstallSources
{
    /// <param name="exePath">Atap.exe's full path.</param>
    /// <param name="scoopRoots">The SCOOP and SCOOP_GLOBAL folders when set, for Scoop moved somewhere custom.</param>
    public static InstallSource Detect(string? exePath, params string?[] scoopRoots)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return InstallSource.Atap;
        var path = exePath.Replace('/', '\\');

        foreach (var root in scoopRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            var apps = root.Replace('/', '\\').TrimEnd('\\') + "\\apps\\";
            if (path.StartsWith(apps, StringComparison.OrdinalIgnoreCase))
                return InstallSource.Scoop;
        }

        // The usual places: %USERPROFILE%\scoop\apps\... and C:\ProgramData\scoop\apps\...
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (segments[i].Equals("scoop", StringComparison.OrdinalIgnoreCase) && segments[i + 1].Equals("apps", StringComparison.OrdinalIgnoreCase))
                return InstallSource.Scoop;
        }
        return InstallSource.Atap;
    }
}
