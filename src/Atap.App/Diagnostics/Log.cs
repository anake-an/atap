using System.Globalization;
using System.IO;
using System.Text;

namespace Atap.Diagnostics;

/// <summary>
/// Tiny append-only file log: %APPDATA%\Atap\logs\atap-yyyyMMdd.log, newest 7 files kept,
/// each capped at about 1 MB. For errors and a few lifecycle lines only. Never log song titles,
/// window titles or other personal content; the user profile path is replaced with %USERPROFILE%.
/// </summary>
internal static class Log
{
    private const int FilesToKeep = 7;
    private const string FilePrefix = "atap-";

    // An error that repeats on every input or render pass must not fill the disk.
    private const long MaxFileBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string? _currentFile;
    private static long _currentFileBytes;
    private static bool _limitReached;

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Atap",
        "logs");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var now = DateTime.Now;
            var text = string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}");
            if (exception is not null)
                text += Environment.NewLine + exception;
            text = Scrub(text) + Environment.NewLine;

            var file = Path.Combine(Folder, string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{now:yyyyMMdd}.log"));
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);

                var isNewFile = !string.Equals(file, _currentFile, StringComparison.OrdinalIgnoreCase);
                if (isNewFile)
                {
                    // An earlier run today may have written to this file already.
                    _currentFile = file;
                    _currentFileBytes = File.Exists(file) ? new FileInfo(file).Length : 0;
                    _limitReached = false;
                }

                if (_currentFileBytes < MaxFileBytes)
                {
                    File.AppendAllText(file, text);
                    _currentFileBytes += Encoding.UTF8.GetByteCount(text);
                }
                else if (!_limitReached)
                {
                    // One last line, then nothing more until the next day's file.
                    _limitReached = true;
                    File.AppendAllText(file, string.Create(CultureInfo.InvariantCulture, $"{now:yyyy-MM-dd HH:mm:ss.fff} WARN Log limit reached; nothing more is logged today.{Environment.NewLine}"));
                }

                // Prune once per new day file, not on every write.
                if (isNewFile)
                    Prune();
            }
        }
        catch (Exception)
        {
            // Logging must never take the app down.
        }
    }

    private static void Prune()
    {
        // Names sort by date, so the oldest come first.
        var files = Directory.GetFiles(Folder, FilePrefix + "*.log");
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Length - FilesToKeep; i++)
            File.Delete(files[i]);
    }

    private static string Scrub(string text) =>
        string.IsNullOrEmpty(UserProfile)
            ? text
            : text.Replace(UserProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
}
