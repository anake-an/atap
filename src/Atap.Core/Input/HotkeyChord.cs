using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.Input;

/// <summary>Why a hotkey was not accepted.</summary>
public enum HotkeyError
{
    None,

    /// <summary>The text is blank. In settings an empty value means no hotkey.</summary>
    Empty,

    /// <summary>A part is neither a modifier nor a key in <see cref="HotkeyKeys"/>.</summary>
    UnknownKey,

    /// <summary>Only modifiers were given, or a modifier was given as the key.</summary>
    MissingKey,

    /// <summary>No Ctrl, Alt, Shift or Win. A global hotkey without one would take the key from every app.</summary>
    MissingModifier,

    /// <summary>A modifier appears twice, as in "Ctrl+Control+K".</summary>
    DuplicateModifier,

    /// <summary>More than one key that is not a modifier, as in "Ctrl+A+B".</summary>
    MoreThanOneKey,

    /// <summary>
    /// Shift is the only modifier and the key is not F1 to F24. Shift with other keys is needed for typing and selecting text.
    /// </summary>
    ShiftOnly,

    /// <summary>The modifier flags hold a bit that is not a <see cref="HotkeyModifiers"/> value.</summary>
    UnknownModifier,
}

/// <summary>
/// A global hotkey: one key plus at least one modifier. Stored in settings as text such as "Ctrl+Alt+Space".
/// </summary>
/// <remarks>
/// <see cref="ToString"/> gives the canonical text: modifiers in the order Ctrl, Alt, Shift, Win, then the key,
/// joined by "+". <see cref="TryParse(string?, out HotkeyChord, out HotkeyError)"/> reads it back, and also accepts
/// any case, spaces around "+", modifiers in any order and aliases such as "Control", "Windows" and "Escape".
/// Use <see cref="TryCreate"/> to build a chord from a captured key press.
/// </remarks>
public readonly record struct HotkeyChord
{
    private static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Control, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Windows, "Win"),
    ];

    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Windows;

    private HotkeyChord(HotkeyModifiers modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    public HotkeyModifiers Modifiers { get; }

    /// <summary>The Win32 virtual-key code, ready for RegisterHotKey.</summary>
    public int VirtualKey { get; }

    /// <summary>True only for <c>default</c>, which stands for no hotkey.</summary>
    public bool IsEmpty => VirtualKey == 0;

    /// <summary>Builds a chord from modifier flags and a virtual-key code, as captured from a key press.</summary>
    public static bool TryCreate(HotkeyModifiers modifiers, int virtualKey, out HotkeyChord chord, out HotkeyError error)
    {
        chord = default;
        error = Validate(modifiers, virtualKey);
        if (error != HotkeyError.None)
            return false;

        chord = new HotkeyChord(modifiers, virtualKey);
        return true;
    }

    /// <summary>Reads text such as "Ctrl+Alt+Space". Never throws.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out HotkeyChord chord) => TryParse(text, out chord, out _);

    /// <summary>Reads text such as "Ctrl+Alt+Space" and says what is wrong when it fails. Never throws.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out HotkeyChord chord, out HotkeyError error)
    {
        chord = default;
        error = ReadParts(text, out var modifiers, out var virtualKey);
        if (error != HotkeyError.None)
            return false;
        return TryCreate(modifiers, virtualKey, out chord, out error);
    }

    /// <summary>Reads text that is known to be valid, such as a default value.</summary>
    /// <exception cref="FormatException">The text is not a valid hotkey.</exception>
    public static HotkeyChord Parse(string text) =>
        TryParse(text, out var chord, out var error)
            ? chord
            : throw new FormatException($"'{text}' is not a valid hotkey ({error}).");

    /// <summary>The canonical text, such as "Ctrl+Alt+Space". Empty for <c>default</c>.</summary>
    public override string ToString()
    {
        if (IsEmpty || !HotkeyKeys.TryGetName(VirtualKey, out var keyName))
            return "";

        var parts = new List<string>(5);
        foreach (var (flag, name) in ModifierNames)
        {
            if ((Modifiers & flag) != 0)
                parts.Add(name);
        }

        parts.Add(keyName);
        return string.Join('+', parts);
    }

    private static HotkeyError Validate(HotkeyModifiers modifiers, int virtualKey)
    {
        if ((modifiers & ~AllModifiers) != 0)
            return HotkeyError.UnknownModifier;
        if (HotkeyKeys.IsModifierKey(virtualKey))
            return HotkeyError.MissingKey;
        if (!HotkeyKeys.TryGetName(virtualKey, out _))
            return HotkeyError.UnknownKey;
        if (modifiers == HotkeyModifiers.None)
            return HotkeyError.MissingModifier;
        if (modifiers == HotkeyModifiers.Shift && !HotkeyKeys.IsFunctionKey(virtualKey))
            return HotkeyError.ShiftOnly;
        return HotkeyError.None;
    }

    private static HotkeyError ReadParts(string? text, out HotkeyModifiers modifiers, out int virtualKey)
    {
        modifiers = HotkeyModifiers.None;
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text))
            return HotkeyError.Empty;

        var parts = new List<string>(text.Split('+'));
        for (var i = 0; i < parts.Count; i++)
            parts[i] = parts[i].Trim();

        // "Ctrl++" splits into "Ctrl", "" and "". The two empty parts at the end are the + key.
        if (parts.Count >= 2 && parts[^1].Length == 0 && parts[^2].Length == 0)
        {
            parts.RemoveRange(parts.Count - 2, 2);
            parts.Add("+");
        }

        foreach (var part in parts)
        {
            if (part.Length == 0)
                return HotkeyError.MissingKey;

            var flag = ModifierFlag(part);
            if (flag != HotkeyModifiers.None)
            {
                if ((modifiers & flag) != 0)
                    return HotkeyError.DuplicateModifier;
                modifiers |= flag;
                continue;
            }

            if (virtualKey != 0)
                return HotkeyError.MoreThanOneKey;
            if (!HotkeyKeys.TryGetVirtualKey(part, out virtualKey))
                return HotkeyError.UnknownKey;
        }

        return virtualKey == 0 ? HotkeyError.MissingKey : HotkeyError.None;
    }

    private static HotkeyModifiers ModifierFlag(string part)
    {
        if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            return HotkeyModifiers.Control;
        if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            return HotkeyModifiers.Alt;
        if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            return HotkeyModifiers.Shift;
        if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return HotkeyModifiers.Windows;
        return HotkeyModifiers.None;
    }
}
