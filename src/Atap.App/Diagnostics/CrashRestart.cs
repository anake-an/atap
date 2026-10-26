using System.Runtime.InteropServices;

namespace Atap.Diagnostics;

/// <summary>
/// Asks Windows to open Atap again if it crashes or stops responding, so a crash does not leave the notch gone until
/// the user starts Atap by hand. Windows only does this for a run that lasted at least a minute, so a crash at start
/// cannot loop. Updates and restarts of the PC are left out, because Atap starts by itself then when the user wants.
/// </summary>
internal static class CrashRestart
{
    private const uint RestartNoPatch = 4;
    private const uint RestartNoReboot = 8;

    public static void Register()
    {
        try
        {
            var result = RegisterApplicationRestart(null, RestartNoPatch | RestartNoReboot);
            if (result != 0)
                Log.Warn($"Windows did not take the restart-after-crash request (0x{result:X8}).");
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Only a safety net is lost.
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLine, uint flags);
}
