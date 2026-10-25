using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Atap.Core.Custom;

/// <summary>What is wrong with a <see cref="ScriptSpec"/>.</summary>
public enum ScriptSpecProblem
{
    None,

    MissingExecutable,

    /// <summary>
    /// The program, the working folder or an argument holds a character that does not show or looks blank, such as a
    /// text-direction mark, a zero-width space, a Hangul filler, a line separator, a space other than U+0020 or a lone surrogate.
    /// The approval dialog would show a different name than what runs.
    /// </summary>
    HiddenCharacter,

    /// <summary>
    /// The program is not a full path with a drive letter, such as <c>C:\Tools\check.exe</c>.
    /// UNC, device and relative paths are refused, as is a folder or file name that ends in a space or dot.
    /// A mapped or subst drive letter passes, so the runner checks the drive type.
    /// </summary>
    ExecutableNotLocalPath,

    /// <summary>The program does not end in <c>.exe</c>. Scripts run through their interpreter, such as <c>powershell.exe -File check.ps1</c>.</summary>
    ExecutableNotExe,

    /// <summary>The working folder is set and is not a full path with a drive letter.</summary>
    WorkingFolderNotLocalPath,

    /// <summary>More than <see cref="ScriptSpec.MaxArguments"/> arguments.</summary>
    TooManyArguments,

    /// <summary>An argument is over <see cref="ScriptSpec.MaxArgumentLength"/> characters.</summary>
    ArgumentTooLong,

    /// <summary>An argument holds a NUL or another control character other than a tab.</summary>
    InvalidArgument,

    /// <summary>The whole command line would be over the Windows limit.</summary>
    CommandTooLong,
}

/// <summary>
/// What a script widget runs: a program, its arguments and an optional working folder.
/// It lives only in machine state and never in a synced file. Each argument is passed on its own,
/// with no shell, so nothing in an argument is expanded or split.
/// </summary>
/// <remarks>
/// <para>
/// Running needs the user's approval on this PC. The approval stores <see cref="ComputeApproval"/>, and any change
/// to the program, an argument, the argument order or the working folder gives a different value, so it needs approval again.
/// Paths are compared exactly, so changing only the letter case also needs approval again.
/// </para>
/// <para>
/// The approval is an HMAC keyed with a random key made once per install by <see cref="CreateApprovalKey"/>.
/// The App keeps that key under DPAPI for the current user in machine state. So an approval written by hand,
/// or copied to another PC by a backup, sync or export, does not match.
/// </para>
/// <para>
/// The approval covers the path text, not the file. Before each run the runner checks that the program and the
/// working folder are not on a network drive, since a mapped or subst drive letter passes <see cref="Validate"/>.
/// Validate also refuses characters that do not show or look blank, and names that Windows would trim,
/// so the approval dialog shows the text that runs. <see cref="IsApprovedBy"/> never approves a spec that fails Validate.
/// </para>
/// </remarks>
public sealed class ScriptSpec : IEquatable<ScriptSpec>
{
    /// <summary>Shortest refresh interval.</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

    /// <summary>A run that takes longer is stopped, with its child processes.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Most output read from one run, in bytes.</summary>
    public const int MaxOutputBytes = 64 * 1024;

    public const int MaxArguments = 32;
    public const int MaxArgumentLength = 2048;
    public const int MaxPathLength = 1024;

    /// <summary>The CreateProcess command line limit, in characters.</summary>
    private const int MaxCommandLine = 32_767;

    /// <summary>Size of the per-install approval key, in bytes. Shorter keys are refused.</summary>
    public const int ApprovalKeyBytes = 32;

    /// <summary>Starts every hashed input, so a later format can never produce the same approval.</summary>
    private const string HashDomain = "atap.script-approval.v1";

    /// <param name="arguments">
    /// Copied, so changing the list later does not change this spec. A null item, as from a damaged state file,
    /// becomes an empty argument instead of throwing while the file loads. An approval then matches only if an empty argument was approved there.
    /// </param>
    public ScriptSpec(string executable, IReadOnlyList<string>? arguments = null, string? workingFolder = null)
    {
        Executable = executable ?? string.Empty;
        WorkingFolder = string.IsNullOrEmpty(workingFolder) ? null : workingFolder;
        if (arguments is null || arguments.Count == 0)
        {
            Arguments = [];
            return;
        }

        var copy = new string[arguments.Count];
        for (var i = 0; i < copy.Length; i++)
            copy[i] = arguments[i] ?? string.Empty;
        Arguments = copy;
    }

    /// <summary>Full path of the program, such as <c>C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe</c>.</summary>
    public string Executable { get; }

    /// <summary>Arguments in order, each passed as one argument.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The folder the program starts in, or null for the program's own folder.</summary>
    public string? WorkingFolder { get; }

    public ScriptSpecProblem Validate()
    {
        if (string.IsNullOrWhiteSpace(Executable))
            return ScriptSpecProblem.MissingExecutable;
        if (HasHiddenCharacter(Executable) || (WorkingFolder is not null && HasHiddenCharacter(WorkingFolder)))
            return ScriptSpecProblem.HiddenCharacter;
        if (!IsLocalAbsolutePath(Executable))
            return ScriptSpecProblem.ExecutableNotLocalPath;
        if (!Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return ScriptSpecProblem.ExecutableNotExe;
        if (WorkingFolder is not null && !IsLocalAbsolutePath(WorkingFolder))
            return ScriptSpecProblem.WorkingFolderNotLocalPath;
        if (Arguments.Count > MaxArguments)
            return ScriptSpecProblem.TooManyArguments;

        // Quotes around the program and each argument, a space between each, and escaping that can double backslashes.
        long commandLine = Executable.Length + 2;
        foreach (var argument in Arguments)
        {
            if (argument.Length > MaxArgumentLength)
                return ScriptSpecProblem.ArgumentTooLong;
            foreach (var c in argument)
            {
                if (char.IsControl(c) && c != '\t')
                    return ScriptSpecProblem.InvalidArgument;
            }
            if (HasHiddenCharacter(argument))
                return ScriptSpecProblem.HiddenCharacter;
            commandLine += 3 + 2L * argument.Length;
        }

        return commandLine > MaxCommandLine ? ScriptSpecProblem.CommandTooLong : ScriptSpecProblem.None;
    }

    /// <summary>A new random approval key. The App makes one per install and keeps it under DPAPI.</summary>
    public static byte[] CreateApprovalKey() => RandomNumberGenerator.GetBytes(ApprovalKeyBytes);

    /// <summary>
    /// HMAC-SHA256 over the program, the working folder and each argument, as 64 lowercase hex characters.
    /// Every field is length-prefixed UTF-16, so <c>["a b"]</c> and <c>["a", "b"]</c> differ.
    /// A null and an empty working folder are the same.
    /// </summary>
    /// <param name="machineKey">This install's key from <see cref="CreateApprovalKey"/>.</param>
    /// <exception cref="ArgumentException">The key is shorter than <see cref="ApprovalKeyBytes"/>.</exception>
    public string ComputeApproval(ReadOnlySpan<byte> machineKey)
    {
        if (machineKey.Length < ApprovalKeyBytes)
            throw new ArgumentException($"The approval key must be at least {ApprovalKeyBytes} bytes.", nameof(machineKey));

        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, machineKey);
        AppendText(hash, HashDomain);
        AppendText(hash, Executable);
        AppendText(hash, WorkingFolder ?? string.Empty);
        AppendCount(hash, Arguments.Count);
        foreach (var argument in Arguments)
            AppendText(hash, argument);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// True when <paramref name="approval"/> is this spec's approval under <paramref name="machineKey"/>.
    /// Letter case of the hex is ignored. A key shorter than <see cref="ApprovalKeyBytes"/> never approves.
    /// A spec that fails <see cref="Validate"/> is never approved, not even by its own approval.
    /// So a runner that forgets to validate still cannot run it, and stricter rules in a later version void old approvals they refuse.
    /// </summary>
    public bool IsApprovedBy(string? approval, ReadOnlySpan<byte> machineKey)
    {
        if (approval is null || machineKey.Length < ApprovalKeyBytes || Validate() != ScriptSpecProblem.None)
            return false;
        var expected = Encoding.ASCII.GetBytes(ComputeApproval(machineKey));
        var actual = Encoding.ASCII.GetBytes(approval.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool Equals(ScriptSpec? other) =>
        other is not null
        && string.Equals(Executable, other.Executable, StringComparison.Ordinal)
        && string.Equals(WorkingFolder, other.WorkingFolder, StringComparison.Ordinal)
        && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as ScriptSpec);

    public override int GetHashCode()
    {
        var code = new HashCode();
        code.Add(Executable, StringComparer.Ordinal);
        code.Add(WorkingFolder, StringComparer.Ordinal);
        foreach (var argument in Arguments)
            code.Add(argument, StringComparer.Ordinal);
        return code.ToHashCode();
    }

    /// <summary>
    /// True for a full path that starts with a drive letter, such as <c>C:\Tools</c>. Refuses relative paths, UNC paths,
    /// device paths such as <c>\\?\</c>, alternate data streams, wildcards, and any folder or file name in the path
    /// that ends in a space or dot, which includes <c>.</c> and <c>..</c>. Windows drops a trailing space or dot from
    /// each name, so <c>C:\Tools.\a.exe</c> would run <c>C:\Tools\a.exe</c>.
    /// Pure string rules, so it gives the same answer on every OS. It cannot tell a mapped or subst network drive
    /// such as <c>Z:\</c> from a local one, so the runner checks the drive type before each run.
    /// </summary>
    internal static bool IsLocalAbsolutePath(string path)
    {
        if (path.Length < 3 || path.Length > MaxPathLength)
            return false;
        if (!char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/'))
            return false;

        for (var i = 3; i < path.Length; i++)
        {
            var c = path[i];
            // A colon after the drive names an alternate data stream.
            if (c is ':' or '<' or '>' or '"' or '|' or '?' or '*' || char.IsControl(c))
                return false;
            // The end of a name: the end of the path or a separator comes next.
            if (c is ' ' or '.' && (i + 1 == path.Length || path[i + 1] is '\\' or '/'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when the text holds a character that does not show or looks blank, or a lone surrogate. That is a format
    /// character such as U+202E or a zero-width space, a line or paragraph separator, any space other than U+0020,
    /// a default-ignorable character such as a Hangul filler, the combining grapheme joiner or a variation selector,
    /// and the blank Braille pattern U+2800. Uses .NET's own Unicode data and a fixed list, so it gives the same answer on every OS.
    /// </summary>
    internal static bool HasHiddenCharacter(string text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var used) != OperationStatus.Done || IsHidden(rune))
                return true;
            rest = rest[used..];
        }
        return false;
    }

    private static bool IsHidden(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
            || (category == UnicodeCategory.SpaceSeparator && rune.Value != ' '))
            return true;

        // Default_Ignorable_Code_Point from the Unicode data files, for the ones outside the Format category.
        // Reserved ones are listed too, so a later Unicode version cannot add a hidden character here. Then U+2800.
        return rune.Value switch
        {
            0x034F or 0x2065 or 0x3164 or 0xFFA0 => true,
            >= 0x115F and <= 0x1160 => true,
            >= 0x17B4 and <= 0x17B5 => true,
            >= 0x180B and <= 0x180F => true,
            >= 0xFE00 and <= 0xFE0F => true,
            >= 0xFFF0 and <= 0xFFF8 => true,
            >= 0xE0000 and <= 0xE0FFF => true,
            0x2800 => true,
            _ => false,
        };
    }

    private static void AppendCount(IncrementalHash hash, int count)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, count);
        hash.AppendData(buffer);
    }

    /// <summary>Length, then the UTF-16 code units in little-endian order. Lossless, even for lone surrogates.</summary>
    private static void AppendText(IncrementalHash hash, string text)
    {
        AppendCount(hash, text.Length);
        var bytes = new byte[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), text[i]);
        hash.AppendData(bytes);
    }
}
