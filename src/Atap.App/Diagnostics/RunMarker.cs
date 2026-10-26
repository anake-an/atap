using System.IO;
using Atap.Core.Storage;

namespace Atap.Diagnostics;

/// <summary>
/// Notices a run that ended without closing, such as a crash inside the .NET runtime, where no exception handler
/// runs and nothing reaches the log. A small file is written at start and deleted on a normal exit. If it is still
/// there at the next start, the log says so, so the lines just before it show what was happening.
/// </summary>
internal static class RunMarker
{
    public static void Start(AtapPaths paths)
    {
        try
        {
            if (File.Exists(paths.RunMarker))
                Log.Warn($"The last run of Atap, started {File.GetLastWriteTime(paths.RunMarker):yyyy-MM-dd HH:mm:ss}, did not close normally.");
            Directory.CreateDirectory(paths.Local);
            File.WriteAllText(paths.RunMarker, string.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a log line is lost.
        }
    }

    public static void Stop(AtapPaths paths)
    {
        try
        {
            File.Delete(paths.RunMarker);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next start then reports a run that did close. Rare, and only a log line.
        }
    }
}
