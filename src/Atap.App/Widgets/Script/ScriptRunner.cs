using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Atap.Core.Custom;

namespace Atap.Widgets.Script;

/// <summary>How one run ended.</summary>
public enum ScriptRunStatus
{
    /// <summary>The program ended within the time limit. The output is what it printed.</summary>
    Done,

    /// <summary>It printed more than <see cref="ScriptSpec.MaxOutputBytes"/>, so it was stopped with everything it started.</summary>
    TooMuchOutput,

    /// <summary>It was still running after <see cref="ScriptSpec.RunTimeout"/>, so it was stopped with everything it started.</summary>
    TimedOut,

    /// <summary>
    /// The program or the working folder is on a network drive or a removable drive, or a link leads there. Nothing ran.
    /// </summary>
    NetworkDrive,

    /// <summary>The program could not start, for example because the file is gone. Nothing ran.</summary>
    CouldNotStart,
}

/// <param name="Status">How the run ended.</param>
/// <param name="Output">What it printed, at most <see cref="ScriptSpec.MaxOutputBytes"/>. Empty unless Done.</param>
/// <param name="ExitCode">The program's exit code, when it ended by itself.</param>
public sealed record ScriptRunResult(ScriptRunStatus Status, ReadOnlyMemory<byte> Output = default, int? ExitCode = null);

/// <summary>Runs a script widget's program once. Tests use a fake.</summary>
public interface IScriptRunner
{
    /// <summary>
    /// Runs <paramref name="spec"/> and reads what it prints. The caller has checked the approval.
    /// Throws only <see cref="OperationCanceledException"/>, after stopping the program, when <paramref name="cancellationToken"/> fires.
    /// </summary>
    Task<ScriptRunResult> RunAsync(ScriptSpec spec, CancellationToken cancellationToken);
}

/// <summary>
/// Starts the program with no shell: <see cref="ProcessStartInfo.ArgumentList"/>, no window, input closed at once and
/// output read up to <see cref="ScriptSpec.MaxOutputBytes"/>. The program goes into a job object that ends every process
/// in it when the job closes, so whatever it started ends with the run. A run over <see cref="ScriptSpec.RunTimeout"/>
/// or over the output cap is stopped at once.
/// </summary>
/// <remarks>
/// Process.Start cannot start a program suspended, so it joins the job a moment after it starts. A process it starts in
/// that moment is outside the job, so a stop also ends the whole process tree by parent, as a second net.
/// A program that cannot join the job is stopped and counts as not started.
/// </remarks>
internal sealed class ScriptProcessRunner : IScriptRunner
{
    public Task<ScriptRunResult> RunAsync(ScriptSpec spec, CancellationToken cancellationToken) =>
        Task.Run(() => RunCoreAsync(spec, cancellationToken), cancellationToken);

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> and one byte more, so a stream over the cap is found without reading it all.
    /// Stops at the end of the stream or once over the cap.
    /// </summary>
    internal static (byte[] Bytes, bool OverCap) ReadCapped(Stream stream, int maxBytes)
    {
        var buffer = new byte[maxBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = stream.Read(buffer, length, buffer.Length - length);
            if (read == 0)
                break;
            length += read;
        }
        return length > maxBytes ? ([], true) : (buffer[..length], false);
    }

    /// <summary>
    /// False for a path on a network drive, a removable drive, a drive that is not there, or a drive letter that points at
    /// a network share through subst or a mapping. True for fixed and RAM drives. A USB stick is refused because another
    /// stick can take the same letter and bring its own program with the same name.
    /// </summary>
    internal static bool IsOnLocalDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        if (root is null || root.Length < 2 || root[1] != ':' || !char.IsAsciiLetter(root[0]))
            return false;
        return IsLocalDriveLetter(root[0], depth: 0);
    }

    /// <summary>
    /// The path with every symbolic link, junction, mount point and subst drive followed, such as <c>C:\Tools\check.exe</c>,
    /// when it exists and ends on a fixed or RAM drive. Null when it is missing, ends on a share or another drive type,
    /// or has no drive letter at the end.
    /// </summary>
    internal static string? ResolveLocal(string path)
    {
        var final = ScriptNative.FinalPathOf(path);
        if (final is null || FromFinalPath(final) is not { } resolved)
            return null;
        return IsOnLocalDrive(path) && IsOnLocalDrive(resolved) ? resolved : null;
    }

    /// <summary>
    /// <c>\\?\C:\Tools\check.exe</c> becomes <c>C:\Tools\check.exe</c>. A share, <c>\\?\UNC\server\share\...</c>,
    /// or anything without a drive letter gives null.
    /// </summary>
    internal static string? FromFinalPath(string final)
    {
        var text = final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        if (text.Length < 3 || !char.IsAsciiLetter(text[0]) || text[1] != ':' || text[2] != '\\')
            return null;
        return text;
    }

    private static bool IsLocalDriveLetter(char letter, int depth)
    {
        if (depth > 4)
            return false;
        try
        {
            var type = new DriveInfo(letter + @":\").DriveType;
            if (type is not (DriveType.Fixed or DriveType.Ram))
                return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        // A subst drive shows the type of the folder it points at, which can itself be on a mapped drive or a share.
        var target = ScriptNative.DosDeviceOf(letter + ":");
        if (target is null)
            return false;
        if (target.StartsWith(@"\??\UNC\", StringComparison.OrdinalIgnoreCase)
            || target.Contains(@"\Device\Mup", StringComparison.OrdinalIgnoreCase)
            || target.Contains("LanmanRedirector", StringComparison.OrdinalIgnoreCase)
            || target.Contains("WebDav", StringComparison.OrdinalIgnoreCase))
            return false;
        if (target.StartsWith(@"\??\", StringComparison.Ordinal) && target.Length >= 6 && char.IsAsciiLetter(target[4]) && target[5] == ':')
            return IsLocalDriveLetter(target[4], depth + 1);
        return true;
    }

    private static async Task<ScriptRunResult> RunCoreAsync(ScriptSpec spec, CancellationToken cancellationToken)
    {
        if (spec.Validate() != ScriptSpecProblem.None)
            return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
        if (!File.Exists(spec.Executable) || (spec.WorkingFolder is { } start && !Directory.Exists(start)))
            return new ScriptRunResult(ScriptRunStatus.CouldNotStart);

        // Links are followed before the drive check, so a link or junction on C: cannot lead to a share or a USB stick.
        // The program then runs from the path that was checked, so the link cannot be moved in between.
        var program = ResolveLocal(spec.Executable);
        var folder = spec.WorkingFolder is { } working ? ResolveLocal(working) : null;
        if (program is null || (spec.WorkingFolder is not null && folder is null))
            return new ScriptRunResult(ScriptRunStatus.NetworkDrive);
        cancellationToken.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            WorkingDirectory = folder ?? Path.GetDirectoryName(program) ?? string.Empty,
        };
        foreach (var argument in spec.Arguments)
            info.ArgumentList.Add(argument);

        ScriptJob job;
        try
        {
            job = ScriptJob.Create();
        }
        catch (Win32Exception)
        {
            return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
        }

        // Disposing the job last ends anything the program left running.
        using (job)
        using (var process = new Process { StartInfo = info })
        {
            try
            {
                if (!process.Start())
                    return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
            {
                return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
            }

            if (!job.TryAdd(process))
            {
                Stop(job, process);
                return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
            }

            // A program that waits for input gets the end of input at once.
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // It already ended.
            }

            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(ScriptSpec.RunTimeout);
            var output = process.StandardOutput.BaseStream;
            try
            {
                // Pipe reads do not stop on a token, so the read runs on its own and a stop ends it by ending the program.
                var (bytes, overCap) = await Task.Run(() => ReadCapped(output, ScriptSpec.MaxOutputBytes), CancellationToken.None).WaitAsync(limit.Token);
                if (overCap)
                {
                    Stop(job, process);
                    return new ScriptRunResult(ScriptRunStatus.TooMuchOutput);
                }
                await process.WaitForExitAsync(limit.Token);
                return new ScriptRunResult(ScriptRunStatus.Done, bytes, process.ExitCode);
            }
            catch (OperationCanceledException)
            {
                Stop(job, process);
                cancellationToken.ThrowIfCancellationRequested();
                return new ScriptRunResult(ScriptRunStatus.TimedOut);
            }
            catch (IOException)
            {
                Stop(job, process);
                return new ScriptRunResult(ScriptRunStatus.CouldNotStart);
            }
        }
    }

    private static void Stop(ScriptJob job, Process process)
    {
        job.Terminate();
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
            // It already ended, or the job ends what is left when it closes.
        }
    }
}

/// <summary>A job object that ends every process in it when its handle closes.</summary>
internal sealed class ScriptJob : IDisposable
{
    private readonly SafeFileHandle _handle;

    private ScriptJob(SafeFileHandle handle) => _handle = handle;

    /// <exception cref="Win32Exception">Windows refused to make the job.</exception>
    public static ScriptJob Create()
    {
        var handle = ScriptNative.CreateJobObjectW(IntPtr.Zero, null);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }

        var limits = new ScriptNative.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new ScriptNative.JobObjectBasicLimitInformation
            {
                // No breakaway flag, so nothing in the job can start a process outside it.
                LimitFlags = ScriptNative.JobObjectLimitKillOnJobClose | ScriptNative.JobObjectLimitDieOnUnhandledException,
            },
        };
        if (!ScriptNative.SetInformationJobObject(
                handle, ScriptNative.JobObjectExtendedLimitInformationClass, ref limits, (uint)Marshal.SizeOf<ScriptNative.JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return new ScriptJob(handle);
    }

    public bool TryAdd(Process process)
    {
        try
        {
            return ScriptNative.AssignProcessToJobObject(_handle, process.SafeHandle);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Ends every process in the job now.</summary>
    public void Terminate()
    {
        if (!_handle.IsClosed)
            ScriptNative.TerminateJobObject(_handle, 1);
    }

    public void Dispose() => _handle.Dispose();
}

internal static class ScriptNative
{
    public const uint JobObjectLimitDieOnUnhandledException = 0x400;
    public const uint JobObjectLimitKillOnJobClose = 0x2000;
    public const int JobObjectExtendedLimitInformationClass = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetInformationJobObject(SafeHandle job, int infoClass, ref JobObjectExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AssignProcessToJobObject(SafeHandle job, SafeHandle process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateJobObject(SafeHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryDosDeviceW")]
    private static extern uint QueryDosDevice(string deviceName, char[] targetPath, uint max);

    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
    private const uint OpenExisting = 3;

    // Opens folders too. Links are followed, since FILE_FLAG_OPEN_REPARSE_POINT is not set.
    private const uint FileFlagBackupSemantics = 0x02000000;

    // FILE_NAME_NORMALIZED with VOLUME_NAME_DOS: a drive letter path, or a failure for a volume that has none.
    private const uint FinalPathFlags = 0;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr securityAttributes, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, char[] path, uint length, uint flags);

    /// <summary>
    /// Where a file or folder really is once every link is followed, such as <c>\\?\C:\Tools\check.exe</c> or
    /// <c>\\?\UNC\server\share\check.exe</c>. Null when it cannot be opened or has no drive letter at the end.
    /// </summary>
    public static string? FinalPathOf(string path)
    {
        // Access 0 reads only the name, so even a program that is open elsewhere can be checked.
        using var handle = CreateFile(path, 0, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
            return null;
        var buffer = new char[1024];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, FinalPathFlags);
        if (length == 0 || length >= buffer.Length)
            return null;
        return new string(buffer, 0, (int)length);
    }

    /// <summary>What a drive such as "C:" points at, such as <c>\Device\HarddiskVolume3</c>, or null when it is not there.</summary>
    public static string? DosDeviceOf(string drive)
    {
        var buffer = new char[1024];
        var length = QueryDosDevice(drive, buffer, (uint)buffer.Length);
        if (length == 0)
            return null;
        // The result is a list of strings, each ending in NUL. The first is the one in use.
        var text = new string(buffer, 0, (int)length);
        var end = text.IndexOf('\0', StringComparison.Ordinal);
        return end < 0 ? text : text[..end];
    }
}
