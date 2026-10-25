using System.IO;
using System.Runtime.InteropServices;
using Atap.Core.Metrics;

namespace Atap.Platform.Metrics;

/// <summary>Reads the machine's CPU times, memory and system drive. Faked in tests.</summary>
internal interface ISystemMetrics
{
    SystemReading Read();
}

/// <summary>
/// GetSystemTimes, GlobalMemoryStatusEx and the system drive. All cheap calls with no handles kept open, so a
/// reading costs nothing between samples. Anything Windows refuses comes back as null and the row hides.
/// </summary>
internal sealed class WindowsSystemMetrics : ISystemMetrics
{
    public SystemReading Read() => new(ReadCpu(), ReadMemory(), ReadDisk());

    private static CpuTimes? ReadCpu() =>
        GetSystemTimes(out var idle, out var kernel, out var user) ? new CpuTimes(idle, kernel, user) : null;

    private static MemoryReading? ReadMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? new MemoryReading(status.TotalPhys, status.AvailPhys) : null;
    }

    private static DiskReading? ReadDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            return drive.IsReady ? new DiskReading(root.TrimEnd('\\'), (ulong)drive.TotalSize, (ulong)drive.AvailableFreeSpace) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
