using System.Text.Json;
using Atap.Core.Custom;

namespace Atap.Core.Tests.Custom;

public class ScriptSpecTests
{
    private const string PowerShell = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
    private const string Folder = @"C:\Users\aniq\Scripts";

    /// <summary>A fixed key, so approvals are the same on every run.</summary>
    private static readonly byte[] Key = Enumerable.Range(0, ScriptSpec.ApprovalKeyBytes).Select(i => (byte)i).ToArray();

    private static ScriptSpec Sample() =>
        new(PowerShell, ["-NoProfile", "-File", @"C:\Users\aniq\Scripts\nas-disk.ps1"], Folder);

    [Fact]
    public void Approval_IsStableAndLowercaseHex()
    {
        var approval = Sample().ComputeApproval(Key);

        Assert.Equal(approval, Sample().ComputeApproval(Key));
        Assert.Matches("^[0-9a-f]{64}$", approval);
    }

    [Fact]
    public void Approval_IsPinned()
    {
        // Approvals stored on users' PCs depend on this exact value. Changing the input voids them all.
        Assert.Equal("a0e8159be76185c0855f2f00b9fdc8424fe91032fca26763c1960ac2b99989f0", Sample().ComputeApproval(Key));
    }

    [Fact]
    public void Approval_DependsOnTheMachineKey()
    {
        var otherKey = Key.Select(b => (byte)(b ^ 0x5A)).ToArray();
        var approval = Sample().ComputeApproval(Key);

        Assert.NotEqual(approval, Sample().ComputeApproval(otherKey));
        Assert.True(Sample().IsApprovedBy(approval, Key));
        Assert.False(Sample().IsApprovedBy(approval, otherKey));
    }

    [Fact]
    public void Approval_RefusesAShortKey()
    {
        var shortKey = new byte[ScriptSpec.ApprovalKeyBytes - 1];

        Assert.Throws<ArgumentException>(() => Sample().ComputeApproval(shortKey));
        Assert.Throws<ArgumentException>(() => Sample().ComputeApproval([]));
        Assert.False(Sample().IsApprovedBy(Sample().ComputeApproval(Key), shortKey));
        Assert.False(Sample().IsApprovedBy(Sample().ComputeApproval(Key), []));
    }

    [Fact]
    public void CreateApprovalKey_IsRandomAndFullSize()
    {
        var first = ScriptSpec.CreateApprovalKey();
        var second = ScriptSpec.CreateApprovalKey();

        Assert.Equal(ScriptSpec.ApprovalKeyBytes, first.Length);
        Assert.NotEqual(first, second);
        Assert.True(Sample().IsApprovedBy(Sample().ComputeApproval(first), first));
    }

    public static TheoryData<ScriptSpec> Changed => new()
    {
        new ScriptSpec(@"C:\Windows\System32\WindowsPowerShell\v1.0\pwsh.exe", Sample().Arguments, Folder),
        new ScriptSpec(PowerShell.ToUpperInvariant(), Sample().Arguments, Folder),
        new ScriptSpec(PowerShell, ["-NoProfile", "-File", @"C:\Users\aniq\Scripts\other.ps1"], Folder),
        new ScriptSpec(PowerShell, ["-File", "-NoProfile", @"C:\Users\aniq\Scripts\nas-disk.ps1"], Folder),
        new ScriptSpec(PowerShell, ["-NoProfile", "-File", @"C:\Users\aniq\Scripts\nas-disk.ps1", ""], Folder),
        new ScriptSpec(PowerShell, ["-NoProfile", "-File"], Folder),
        new ScriptSpec(PowerShell, Sample().Arguments, @"C:\Users\aniq\Scripts\old"),
        new ScriptSpec(PowerShell, Sample().Arguments, null),
        new ScriptSpec(PowerShell, Sample().Arguments, Folder + @"\"),
    };

    [Theory]
    [MemberData(nameof(Changed))]
    public void Approval_ChangesWithExecutableArgumentsOrWorkingFolder(ScriptSpec changed)
    {
        Assert.NotEqual(Sample().ComputeApproval(Key), changed.ComputeApproval(Key));
        Assert.False(changed.IsApprovedBy(Sample().ComputeApproval(Key), Key));
        Assert.NotEqual(Sample(), changed);
    }

    [Fact]
    public void Approval_SeparatesArgumentBoundaries()
    {
        var joined = new ScriptSpec(PowerShell, ["a b"]);
        var split = new ScriptSpec(PowerShell, ["a", "b"]);
        var moved = new ScriptSpec(PowerShell, ["ab", ""]);
        var shifted = new ScriptSpec(PowerShell, ["a", "b", ""]);

        var approvals = new[] { joined, split, moved, shifted, new ScriptSpec(PowerShell) }.Select(s => s.ComputeApproval(Key)).ToList();

        Assert.Equal(approvals.Count, approvals.Distinct().Count());
    }

    [Fact]
    public void Approval_SeparatesFieldBoundaries()
    {
        // The same characters spread over different fields.
        var a = new ScriptSpec(@"C:\a.exe", [@"C:\b"]);
        var b = new ScriptSpec(@"C:\a.exe", [], @"C:\b");

        Assert.NotEqual(a.ComputeApproval(Key), b.ComputeApproval(Key));
    }

    [Fact]
    public void Approval_KeepsLoneSurrogatesApart()
    {
        var first = new ScriptSpec(PowerShell, ["\uD800"]);
        var second = new ScriptSpec(PowerShell, ["\uD801"]);

        Assert.NotEqual(first.ComputeApproval(Key), second.ComputeApproval(Key));
    }

    [Fact]
    public void NullAndEmptyWorkingFolder_AreTheSame()
    {
        var withNull = new ScriptSpec(PowerShell, ["-NoProfile"], null);
        var withEmpty = new ScriptSpec(PowerShell, ["-NoProfile"], "");

        Assert.Null(withEmpty.WorkingFolder);
        Assert.Equal(withNull.ComputeApproval(Key), withEmpty.ComputeApproval(Key));
        Assert.Equal(withNull, withEmpty);
    }

    [Fact]
    public void NullArguments_AreEmpty()
    {
        var spec = new ScriptSpec(PowerShell, null);

        Assert.Empty(spec.Arguments);
        Assert.Equal(new ScriptSpec(PowerShell, []).ComputeApproval(Key), spec.ComputeApproval(Key));
    }

    [Fact]
    public void NullArgument_BecomesEmpty()
    {
        var spec = new ScriptSpec(PowerShell, ["-File", null!]);

        Assert.Equal(["-File", ""], spec.Arguments);
        Assert.Equal(new ScriptSpec(PowerShell, ["-File", ""]), spec);
        Assert.Equal([""], new ScriptSpec(PowerShell, [null!]).Arguments);
    }

    [Fact]
    public void JsonWithANullArgument_LoadsWithoutThrowing()
    {
        // A damaged state file. The loader falls back on JsonException, so the constructor must not throw anything else.
        var json = JsonSerializer.Serialize(Sample()).Replace("\"-NoProfile\"", "null", StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<ScriptSpec>(json)!;

        Assert.Equal("", back.Arguments[0]);
        Assert.False(back.IsApprovedBy(Sample().ComputeApproval(Key), Key));
    }

    [Fact]
    public void Arguments_AreCopied()
    {
        var arguments = new List<string> { "-NoProfile", "-File", @"C:\Users\aniq\Scripts\nas-disk.ps1" };
        var spec = new ScriptSpec(PowerShell, arguments, Folder);
        var approval = spec.ComputeApproval(Key);
        var code = spec.GetHashCode();

        arguments[2] = @"C:\Users\aniq\Scripts\other.ps1";
        arguments.Add("-Extra");

        Assert.Equal(3, spec.Arguments.Count);
        Assert.Equal(@"C:\Users\aniq\Scripts\nas-disk.ps1", spec.Arguments[2]);
        Assert.Equal(approval, spec.ComputeApproval(Key));
        Assert.True(spec.IsApprovedBy(approval, Key));
        Assert.Equal(code, spec.GetHashCode());
        Assert.Equal(Sample(), spec);
    }

    [Fact]
    public void IsApprovedBy_MatchesOnlyTheSameApproval()
    {
        var spec = Sample();
        var approval = spec.ComputeApproval(Key);

        Assert.True(spec.IsApprovedBy(approval, Key));
        Assert.True(spec.IsApprovedBy(approval.ToUpperInvariant(), Key));
        Assert.True(spec.IsApprovedBy(" " + approval + " ", Key));
        Assert.False(spec.IsApprovedBy(null, Key));
        Assert.False(spec.IsApprovedBy("", Key));
        Assert.False(spec.IsApprovedBy(approval[..^1], Key));
        Assert.False(spec.IsApprovedBy(new string('0', 64), Key));
    }

    [Fact]
    public void IsApprovedBy_RefusesASpecThatFailsValidate()
    {
        // Approved with its own approval, as an older version with looser rules could have stored.
        ScriptSpec[] invalid =
        [
            new("C:\\Tools\\\u202Egnp.exe"),
            new(PowerShell, ["-File", "check\u3164.ps1"]),
            new(@"C:\Tools.\check.exe"),
            new("check.exe"),
            new(PowerShell, ["a\0b"]),
        ];

        foreach (var spec in invalid)
        {
            Assert.NotEqual(ScriptSpecProblem.None, spec.Validate());
            Assert.False(spec.IsApprovedBy(spec.ComputeApproval(Key), Key));
        }
    }

    [Fact]
    public void JsonRoundTrip_KeepsTheApproval()
    {
        var spec = Sample();

        var json = JsonSerializer.Serialize(spec);
        var back = JsonSerializer.Deserialize<ScriptSpec>(json)!;

        Assert.Equal(spec, back);
        Assert.Equal(spec.GetHashCode(), back.GetHashCode());
        Assert.True(back.IsApprovedBy(spec.ComputeApproval(Key), Key));
    }

    [Fact]
    public void Validate_AcceptsAFullLocalExePath()
    {
        Assert.Equal(ScriptSpecProblem.None, Sample().Validate());
        Assert.Equal(ScriptSpecProblem.None, new ScriptSpec(@"D:/tools/check.EXE", ["--json", "a\tb"], @"D:\").Validate());
    }

    [Fact]
    public void Validate_CannotTellAMappedDriveFromALocalOne()
    {
        // Pure string rules. The runner checks the drive type before each run.
        Assert.Equal(ScriptSpecProblem.None, new ScriptSpec(@"Z:\share\x.exe", [], @"Z:\share").Validate());
    }

    [Theory]
    [InlineData("", ScriptSpecProblem.MissingExecutable)]
    [InlineData("   ", ScriptSpecProblem.MissingExecutable)]
    [InlineData("powershell.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"Scripts\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"\Windows\notepad.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"\\nas\scripts\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"\\?\C:\Tools\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"\\.\C:\Tools\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\notes.txt:payload.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\*.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@" C:\Tools\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\check.exe ", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\check.exe.", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools.\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools \check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:/Tools./check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\.\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\..\Windows\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\..\check.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData("C:\\Tools\\che\u0000ck.exe", ScriptSpecProblem.ExecutableNotLocalPath)]
    [InlineData(@"C:\Tools\check.bat", ScriptSpecProblem.ExecutableNotExe)]
    [InlineData(@"C:\Tools\check.cmd", ScriptSpecProblem.ExecutableNotExe)]
    [InlineData(@"C:\Tools\check.ps1", ScriptSpecProblem.ExecutableNotExe)]
    [InlineData(@"C:\Tools\check", ScriptSpecProblem.ExecutableNotExe)]
    public void Validate_RefusesOtherPrograms(string executable, ScriptSpecProblem expected)
    {
        Assert.Equal(expected, new ScriptSpec(executable).Validate());
    }

    [Theory]
    [InlineData("Scripts")]
    [InlineData(@"\\nas\scripts")]
    [InlineData(@"C:\Scripts\..\x:y")]
    [InlineData(@"C:\a \b")]
    [InlineData(@"C:\Scripts.")]
    [InlineData(@"C:\Scripts\..")]
    [InlineData(@"C:\Scripts\.\old")]
    [InlineData(@"C:\Scripts \")]
    public void Validate_RefusesAWorkingFolderThatIsNotLocal(string folder)
    {
        Assert.Equal(ScriptSpecProblem.WorkingFolderNotLocalPath, new ScriptSpec(PowerShell, [], folder).Validate());
    }

    [Theory]
    [InlineData(@"C:\Program Files\Tools\v1.0\check.exe", @"C:\Users\aniq\Scripts\")]
    [InlineData(@"C:\Tools\.hidden\check.exe", @"C:\")]
    [InlineData(@"C:\Tools\ check.exe", @"C:\a.b\c")]
    public void Validate_AcceptsNamesWithInnerDotsAndSpaces(string executable, string folder)
    {
        Assert.Equal(ScriptSpecProblem.None, new ScriptSpec(executable, [], folder).Validate());
    }

    [Fact]
    public void Validate_RefusesHiddenCharacters()
    {
        // Theory data cannot carry lone surrogates, so this is a fact.
        string[] hidden =
        [
            // Format characters and lone surrogates.
            "\u202E", "\u202A", "\u2066", "\u2069", "\u200E", "\u200F", "\u061C", "\u200B", "\uFEFF", "\u00AD", "\uD800", "\uDC00",
            // Blank-looking letters and symbols: Hangul fillers and the blank Braille pattern.
            "\u3164", "\uFFA0", "\u115F", "\u1160", "\u2800",
            // Other default-ignorable characters: the combining grapheme joiner, variation selectors, Mongolian selectors and tags.
            "\u034F", "\uFE00", "\uFE0F", "\u180B", "\u180F", "\U000E0041", "\U000E0100",
            // Line and paragraph separators, and every space other than U+0020.
            "\u2028", "\u2029", "\u00A0", "\u1680", "\u2000", "\u2007", "\u200A", "\u202F", "\u205F", "\u3000",
        ];

        foreach (var c in hidden)
        {
            // A right-to-left override can make "exe.cmd.exe" show as a different name.
            Assert.Equal(ScriptSpecProblem.HiddenCharacter, new ScriptSpec("C:/Tools/" + c + "exe.cmd.exe").Validate());
            Assert.Equal(ScriptSpecProblem.HiddenCharacter, new ScriptSpec(PowerShell, [], @"C:\Tools\" + c + "x").Validate());
            Assert.Equal(ScriptSpecProblem.HiddenCharacter, new ScriptSpec(PowerShell, ["-File", "check" + c + ".ps1"]).Validate());
        }

        // Pairs that make one character, such as an emoji, are fine.
        Assert.Equal(ScriptSpecProblem.None, new ScriptSpec(PowerShell, ["rumah \uD83C\uDFE0"], @"C:\Tools").Validate());
    }

    [Fact]
    public void Validate_AcceptsOrdinaryTextInEveryScript()
    {
        // Malay, Chinese, Tamil with its combining vowel signs, Korean syllables, Arabic and Jawi.
        var spec = new ScriptSpec(
            @"C:\Users\Siti Nurhaliza\Alat\semak.exe",
            ["Selamat pagi", "你好", "வணக்கம்", "안녕하세요", "مرحبا", "ڤرسيدڠن"],
            @"C:\Users\Siti Nurhaliza\文件\வேலை");

        Assert.Equal(ScriptSpecProblem.None, spec.Validate());
    }

    [Fact]
    public void Validate_ChecksArguments()
    {
        Assert.Equal(ScriptSpecProblem.TooManyArguments, new ScriptSpec(PowerShell, Enumerable.Repeat("x", ScriptSpec.MaxArguments + 1).ToArray()).Validate());
        Assert.Equal(ScriptSpecProblem.None, new ScriptSpec(PowerShell, Enumerable.Repeat("x", ScriptSpec.MaxArguments).ToArray()).Validate());
        Assert.Equal(ScriptSpecProblem.ArgumentTooLong, new ScriptSpec(PowerShell, [new string('x', ScriptSpec.MaxArgumentLength + 1)]).Validate());
        Assert.Equal(ScriptSpecProblem.InvalidArgument, new ScriptSpec(PowerShell, ["a\0b"]).Validate());
        Assert.Equal(ScriptSpecProblem.InvalidArgument, new ScriptSpec(PowerShell, ["line1\nline2"]).Validate());
        Assert.Equal(
            ScriptSpecProblem.CommandTooLong,
            new ScriptSpec(PowerShell, Enumerable.Repeat(new string('x', ScriptSpec.MaxArgumentLength), ScriptSpec.MaxArguments).ToArray()).Validate());
    }

    [Fact]
    public void Limits_MatchThePlan()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ScriptSpec.MinInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), ScriptSpec.RunTimeout);
        Assert.Equal(64 * 1024, ScriptSpec.MaxOutputBytes);
    }

    [Fact]
    public void ScriptOutput_MapsLikeAnyPayload()
    {
        var template = CustomFixtures.Template(new ValueTemplateSpec(ValueTemplateKind.Progress, "$.used") { MaxPath = "$.total" });

        var plain = CustomFixtures.Template(new ValueTemplateSpec(ValueTemplateKind.Number) { Unit = "%" })
            .Render(PayloadReader.Read("72\r\n"), null, System.Globalization.CultureInfo.InvariantCulture);
        var json = template.Render(PayloadReader.Read("""{"used": 36, "total": 48}""" + "\n"), null, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("72%", plain.DisplayText);
        Assert.Equal(0.75, json.Fraction);
    }
}
