using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.Input;

/// <summary>
/// The keys a hotkey can use, by Win32 virtual-key code, with their names in settings.
/// </summary>
/// <remarks>
/// Names are layout-neutral and match how keys are printed on a US keyboard, the usual layout in Malaysia.
/// The key that types = and + is "Plus" and the one that types - is "Minus", as in Windows shortcuts such as Ctrl+Plus.
/// Lock keys other than Scroll Lock, mouse buttons and media keys are left out.
/// </remarks>
public static class HotkeyKeys
{
    private static readonly (int VirtualKey, string Name)[] Named =
    [
        (0x08, "Backspace"),
        (0x09, "Tab"),
        (0x0D, "Enter"),
        (0x13, "Pause"),
        (0x1B, "Esc"),
        (0x20, "Space"),
        (0x21, "PageUp"),
        (0x22, "PageDown"),
        (0x23, "End"),
        (0x24, "Home"),
        (0x25, "Left"),
        (0x26, "Up"),
        (0x27, "Right"),
        (0x28, "Down"),
        (0x2C, "PrintScreen"),
        (0x2D, "Insert"),
        (0x2E, "Delete"),
        (0x6A, "NumPadMultiply"),
        (0x6B, "NumPadAdd"),
        (0x6D, "NumPadSubtract"),
        (0x6E, "NumPadDecimal"),
        (0x6F, "NumPadDivide"),
        (0x91, "ScrollLock"),
        (0xBA, ";"),
        (0xBB, "Plus"),
        (0xBC, ","),
        (0xBD, "Minus"),
        (0xBE, "."),
        (0xBF, "/"),
        (0xC0, "`"),
        (0xDB, "["),
        (0xDC, "\\"),
        (0xDD, "]"),
        (0xDE, "'"),
    ];

    private static readonly (string Alias, int VirtualKey)[] Aliases =
    [
        ("Escape", 0x1B),
        ("Return", 0x0D),
        ("Spacebar", 0x20),
        ("PgUp", 0x21),
        ("PgDn", 0x22),
        ("PrtSc", 0x2C),
        ("Ins", 0x2D),
        ("Del", 0x2E),
        ("Break", 0x13),
        ("=", 0xBB),
        ("+", 0xBB),
        ("-", 0xBD),
    ];

    private static readonly Dictionary<int, string> NameByKey = BuildNames();

    private static readonly Dictionary<string, int> KeyByName = BuildLookup();

    /// <summary>Every supported virtual-key code, in ascending order.</summary>
    public static IReadOnlyList<int> All { get; } = [.. NameByKey.Keys.Order()];

    /// <summary>The settings name of a key, such as "Space", "K" or "F5".</summary>
    public static bool TryGetName(int virtualKey, [NotNullWhen(true)] out string? name) =>
        NameByKey.TryGetValue(virtualKey, out name);

    /// <summary>Finds a key by name or alias, without case. "Escape" and "Esc" both give 0x1B.</summary>
    public static bool TryGetVirtualKey(string? name, out int virtualKey)
    {
        virtualKey = 0;
        return name is not null && KeyByName.TryGetValue(name.Trim(), out virtualKey);
    }

    /// <summary>True for Shift, Ctrl, Alt and the Windows keys, left, right or either.</summary>
    public static bool IsModifierKey(int virtualKey) =>
        virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>F1 to F24.</summary>
    public static bool IsFunctionKey(int virtualKey) => virtualKey is >= 0x70 and <= 0x87;

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>();
        for (var c = '0'; c <= '9'; c++)
            names[c] = c.ToString();
        for (var c = 'A'; c <= 'Z'; c++)
            names[c] = c.ToString();
        for (var n = 0; n <= 9; n++)
            names[0x60 + n] = $"NumPad{n}";
        for (var n = 1; n <= 24; n++)
            names[0x6F + n] = $"F{n}";
        foreach (var (virtualKey, name) in Named)
            names[virtualKey] = name;
        return names;
    }

    private static Dictionary<string, int> BuildLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (virtualKey, name) in NameByKey)
            lookup[name] = virtualKey;
        foreach (var (alias, virtualKey) in Aliases)
            lookup[alias] = virtualKey;
        return lookup;
    }
}
