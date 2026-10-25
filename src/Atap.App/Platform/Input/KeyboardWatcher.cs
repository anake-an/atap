using System.Globalization;
using System.Runtime.InteropServices;

namespace Atap.Platform.Input;

/// <summary>Caps Lock and the input language. Faked in tests.</summary>
internal interface IKeyboardWatcher : IDisposable
{
    /// <summary>Raised on the UI thread with the new Caps Lock state.</summary>
    event EventHandler<bool>? CapsLockChanged;

    /// <summary>Raised on the UI thread with the new input language's name, such as "English (United States)".</summary>
    event EventHandler<string>? LanguageChanged;
}

/// <summary>
/// Tells a language switch apart from moving to a window that already used another language. Only a change in the
/// same window counts. Pure, so it is tested without Windows.
/// </summary>
internal sealed class LanguageSwitchTracker
{
    private nint _window;
    private nint _layout;

    /// <summary>True when the layout changed while the same window stayed in front.</summary>
    public bool Changed(nint window, nint layout)
    {
        var switched = window == _window && layout != _layout && _layout != 0;
        _window = window;
        _layout = layout;
        return switched;
    }
}

/// <summary>
/// A low-level keyboard hook on the UI thread. It only looks at which key went down or up and never reads what is
/// typed. Caps Lock is read from Windows on each press. A language check runs shortly after the switch keys (Win+Space,
/// Alt+Shift, Ctrl+Shift) are let go. The hook lives only while this object does.
/// </summary>
internal sealed class HookKeyboardWatcher : IKeyboardWatcher
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkCapital = 0x14;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkSpace = 0x20;
    private static readonly TimeSpan LanguageCheckDelay = TimeSpan.FromMilliseconds(250);

    private readonly SynchronizationContext _ui;
    private readonly HookProc _proc;
    private readonly LanguageSwitchTracker _tracker = new();
    private nint _hook;
    private bool _capsDown;
    private int _checkVersion;
    private bool _disposed;

    public HookKeyboardWatcher(SynchronizationContext ui)
    {
        _ui = ui;
        _proc = OnHook;
        CheckLanguage();
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    public bool IsSupported => _hook != 0;

    public event EventHandler<bool>? CapsLockChanged;

    public event EventHandler<string>? LanguageChanged;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hook != 0)
        {
            UnhookWindowsHookEx(_hook);
            _hook = 0;
        }
    }

    // Runs on the UI thread inside the hook, so it only notes the key and posts the rest.
    private nint OnHook(int code, nint message, nint data)
    {
        if (code >= 0 && !_disposed)
        {
            var key = Marshal.ReadInt32(data);
            var down = message is WmKeyDown or WmSysKeyDown;
            var up = message is WmKeyUp or WmSysKeyUp;
            if (key == VkCapital)
            {
                if (down && !_capsDown)
                {
                    _capsDown = true;
                    // Read from Windows on each press, so a change Atap did not see (on the sign-in screen, for one)
                    // never leaves the indicator showing the opposite.
                    var on = CapsLockAfterPress(GetKeyState(VkCapital));
                    _ui.Post(_ => Raise(CapsLockChanged, on), null);
                }
                else if (up)
                {
                    _capsDown = false;
                }
            }
            else if (up && IsSwitchKey(key))
            {
                var version = ++_checkVersion;
                _ = CheckLaterAsync(version);
            }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    /// <summary>
    /// Caps Lock once this press is done. The hook sees the press before Windows flips the light, so
    /// <paramref name="keyState"/> (from GetKeyState) still holds the state before it, in its low bit.
    /// </summary>
    internal static bool CapsLockAfterPress(short keyState) => (keyState & 1) == 0;

    private static bool IsSwitchKey(int key) =>
        key is VkShift or VkControl or VkMenu or VkSpace or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private async Task CheckLaterAsync(int version)
    {
        await Task.Delay(LanguageCheckDelay).ConfigureAwait(false);
        _ui.Post(_ =>
        {
            if (!_disposed && version == _checkVersion)
                CheckLanguage();
        }, null);
    }

    private void CheckLanguage()
    {
        var window = GetForegroundWindow();
        var thread = window == 0 ? 0 : GetWindowThreadProcessId(window, out _);
        var layout = GetKeyboardLayout(thread);
        if (!_tracker.Changed(window, layout))
            return;
        if (NameOf(layout) is { } name)
            LanguageChanged?.Invoke(this, name);
    }

    private static string? NameOf(nint layout)
    {
        try
        {
            return CultureInfo.GetCultureInfo((int)(layout & 0xFFFF)).NativeName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private void Raise<T>(EventHandler<T>? handler, T value)
    {
        if (!_disposed)
            handler?.Invoke(this, value);
    }

    private delegate nint HookProc(int code, nint message, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint thread);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
