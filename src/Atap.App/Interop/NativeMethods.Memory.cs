using System.Runtime.InteropServices;

namespace Atap.Interop;

/// <summary>Giving memory back to Windows once a big window has closed.</summary>
internal static partial class NativeMethods
{
    /// <summary>With -1 for both sizes, Windows takes back as much of the working set as it can.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumSize, IntPtr maximumSize);
}
