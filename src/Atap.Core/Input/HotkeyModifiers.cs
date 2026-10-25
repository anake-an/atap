namespace Atap.Core.Input;

/// <summary>Modifier keys of a hotkey. The values are the Win32 MOD_ flags that RegisterHotKey takes.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}
