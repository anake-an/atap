using Atap.Core.Input;

namespace Atap.Core.Tests.Input;

public class HotkeyChordTests
{
    // Text as typed in settings.json or the hotkeys card on the left, the canonical text on the right.
    private const string Valid = """
        Ctrl+Alt+Space          => Ctrl+Alt+Space
        Ctrl+Alt+K              => Ctrl+Alt+K
        ctrl + alt + k          => Ctrl+Alt+K
        Alt+Ctrl+Space          => Ctrl+Alt+Space
        Shift+Ctrl+F5           => Ctrl+Shift+F5
        Win+Alt+Ctrl+Shift+A    => Ctrl+Alt+Shift+Win+A
        Control+Escape          => Ctrl+Esc
        Windows+Shift+S         => Shift+Win+S
        Ctrl+Return             => Ctrl+Enter
        Ctrl++                  => Ctrl+Plus
        Ctrl+=                  => Ctrl+Plus
        Ctrl+-                  => Ctrl+Minus
        Ctrl+Alt+PgDn           => Ctrl+Alt+PageDown
        Alt+F12                 => Alt+F12
        Shift+F5                => Shift+F5
        Ctrl+Shift+NumPad5      => Ctrl+Shift+NumPad5
        Ctrl+Alt+/              => Ctrl+Alt+/
        Ctrl+Alt+\              => Ctrl+Alt+\
        Ctrl+;                  => Ctrl+;
        Ctrl+Alt+1              => Ctrl+Alt+1
        Win+PrtSc               => Win+PrintScreen
        Ctrl+Alt+f24            => Ctrl+Alt+F24
        """;

    private const string Invalid = """
        K                       => MissingModifier
        F5                      => MissingModifier
        Space                   => MissingModifier
        +                       => MissingModifier
        Ctrl+Alt                => MissingKey
        Ctrl+                   => MissingKey
        Ctrl+Shift              => MissingKey
        +K                      => MissingKey
        Ctrl+Ctrl+K             => DuplicateModifier
        Ctrl+Control+K          => DuplicateModifier
        Ctrl+A+B                => MoreThanOneKey
        Ctrl+Foo                => UnknownKey
        Ctrl+Alt+LWin           => UnknownKey
        Ctrl+F25                => UnknownKey
        Shift+K                 => ShiftOnly
        Shift+Space             => ShiftOnly
        Shift+Tab               => ShiftOnly
        """;

    public static TheoryData<string, string> ValidCases => Table(Valid);

    public static TheoryData<string, string> InvalidCases => Table(Invalid);

    [Theory]
    [MemberData(nameof(ValidCases))]
    public void Parse_GivesTheCanonicalText(string text, string canonical)
    {
        Assert.True(HotkeyChord.TryParse(text, out var chord, out var error));
        Assert.Equal(HotkeyError.None, error);
        Assert.Equal(canonical, chord.ToString());
        Assert.Equal(chord, HotkeyChord.Parse(canonical));
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void Parse_RejectsWithAReason(string text, string expected)
    {
        Assert.False(HotkeyChord.TryParse(text, out var chord, out var error));
        Assert.Equal(Enum.Parse<HotkeyError>(expected), error);
        Assert.True(chord.IsEmpty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankTextIsEmpty(string? text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _, out var error));
        Assert.Equal(HotkeyError.Empty, error);
    }

    [Fact]
    public void Parse_ThrowsOnlyForInvalidText()
    {
        Assert.Throws<FormatException>(() => HotkeyChord.Parse("K"));
        Assert.Equal(0x4B, HotkeyChord.Parse("Ctrl+K").VirtualKey);
    }

    [Fact]
    public void EveryKey_RoundTrips()
    {
        foreach (var virtualKey in HotkeyKeys.All)
        {
            Assert.True(HotkeyChord.TryCreate(HotkeyModifiers.Control | HotkeyModifiers.Alt, virtualKey, out var chord, out _));

            var text = chord.ToString();

            Assert.True(HotkeyChord.TryParse(text, out var parsed), text);
            Assert.Equal(chord, parsed);
            Assert.Equal(text, parsed.ToString());
        }
    }

    [Fact]
    public void EveryModifierSet_RoundTrips()
    {
        for (var flags = 1; flags <= 15; flags++)
        {
            var modifiers = (HotkeyModifiers)flags;
            Assert.True(HotkeyChord.TryCreate(modifiers, 0x74, out var chord, out _));

            var parsed = HotkeyChord.Parse(chord.ToString());

            Assert.Equal(modifiers, parsed.Modifiers);
            Assert.Equal(0x74, parsed.VirtualKey);
        }
    }

    [Fact]
    public void Defaults_AreValidAndCanonical()
    {
        var panel = HotkeyChord.Parse(HotkeyDefaults.Panel);
        var commandBar = HotkeyChord.Parse(HotkeyDefaults.CommandBar);

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, panel.Modifiers);
        Assert.Equal(0x20, panel.VirtualKey);
        Assert.Equal(HotkeyDefaults.Panel, panel.ToString());
        Assert.Equal(0x4B, commandBar.VirtualKey);
        Assert.Equal(HotkeyDefaults.CommandBar, commandBar.ToString());
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x4B, HotkeyError.MissingModifier)]
    [InlineData(HotkeyModifiers.Control, 0x11, HotkeyError.MissingKey)]
    [InlineData(HotkeyModifiers.Control, 0xA2, HotkeyError.MissingKey)]
    [InlineData(HotkeyModifiers.Control, 0x5B, HotkeyError.MissingKey)]
    [InlineData(HotkeyModifiers.Control, 0xFF, HotkeyError.UnknownKey)]
    [InlineData(HotkeyModifiers.Control, 0, HotkeyError.UnknownKey)]
    [InlineData(HotkeyModifiers.Control | (HotkeyModifiers)0x4000, 0x4B, HotkeyError.UnknownModifier)]
    [InlineData(HotkeyModifiers.Shift, 0x4B, HotkeyError.ShiftOnly)]
    [InlineData(HotkeyModifiers.Shift | HotkeyModifiers.Alt, 0x4B, HotkeyError.None)]
    public void TryCreate_ChecksTheCapturedPress(HotkeyModifiers modifiers, int virtualKey, HotkeyError expected)
    {
        Assert.Equal(expected == HotkeyError.None, HotkeyChord.TryCreate(modifiers, virtualKey, out _, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void Default_IsEmpty()
    {
        HotkeyChord chord = default;

        Assert.True(chord.IsEmpty);
        Assert.Equal("", chord.ToString());
    }

    [Fact]
    public void Equality_IgnoresHowTheTextWasWritten()
    {
        var set = new HashSet<HotkeyChord> { HotkeyChord.Parse("Ctrl+Alt+Space") };

        Assert.Contains(HotkeyChord.Parse("alt + CONTROL + spacebar"), set);
    }

    [Fact]
    public void Modifiers_MatchTheWin32Flags()
    {
        Assert.Equal(0x1, (int)HotkeyModifiers.Alt);
        Assert.Equal(0x2, (int)HotkeyModifiers.Control);
        Assert.Equal(0x4, (int)HotkeyModifiers.Shift);
        Assert.Equal(0x8, (int)HotkeyModifiers.Windows);
    }

    private static TheoryData<string, string> Table(string fixture)
    {
        var data = new TheoryData<string, string>();
        foreach (var line in fixture.Split('\n'))
        {
            var arrow = line.IndexOf(" => ", StringComparison.Ordinal);
            data.Add(line[..arrow].Trim(), line[(arrow + 4)..].Trim());
        }

        return data;
    }
}
