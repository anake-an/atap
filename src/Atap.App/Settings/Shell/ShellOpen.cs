using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Atap.Settings.Shell;

/// <summary>Opens files and folders the way Explorer does, for the cards that show licences and logs.</summary>
internal static class ShellOpen
{
    /// <summary>Opens a text file with its usual app, or Notepad when it has none. False when nothing opened.</summary>
    public static bool File(string path)
    {
        if (!System.IO.File.Exists(path))
            return false;
        return Start(new ProcessStartInfo(path) { UseShellExecute = true })
            || Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    /// <summary>Opens a folder in Explorer. False when it does not exist.</summary>
    public static bool Folder(string path) =>
        Directory.Exists(path) && Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    private static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}
