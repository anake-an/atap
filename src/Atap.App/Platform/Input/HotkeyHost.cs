using System.Windows.Threading;
using Atap.Core.Input;
using Atap.Core.Shared;
using Atap.Interop;

namespace Atap.Platform.Input;

/// <summary>Registers hotkeys with Windows and reports presses. Tests pass their own.</summary>
internal interface IHotkeyHost : IDisposable
{
    /// <summary>False when Windows refused, usually because another app holds the same keys.</summary>
    bool Register(int id, HotkeyChord chord);

    void Unregister(int id);

    /// <summary>True while the key is down right now.</summary>
    bool IsKeyDown(int virtualKey);

    /// <summary>Raised on the UI thread with the id of the pressed hotkey.</summary>
    event EventHandler<int>? Pressed;
}

/// <summary>Repeats a short check while a key is held. Tests pass their own.</summary>
internal interface ITicker
{
    IDisposable Start(TimeSpan every, Action tick);
}

/// <summary>Hotkeys on the shared message window, with <c>MOD_NOREPEAT</c> so holding a key presses once.</summary>
internal sealed class WindowsHotkeyHost : IHotkeyHost
{
    private readonly Lease<MessageWindow> _window;
    private readonly IDisposable _handler;
    private readonly HashSet<int> _ids = [];

    public WindowsHotkeyHost(Lease<MessageWindow> window)
    {
        _window = window;
        _handler = window.Value.AddHandler(OnMessage);
    }

    public event EventHandler<int>? Pressed;

    public bool Register(int id, HotkeyChord chord)
    {
        var ok = NativeMethods.RegisterHotKey(_window.Value.Handle, id, (uint)chord.Modifiers | NativeMethods.MOD_NOREPEAT, (uint)chord.VirtualKey);
        if (ok)
            _ids.Add(id);
        return ok;
    }

    public void Unregister(int id)
    {
        if (_ids.Remove(id))
            NativeMethods.UnregisterHotKey(_window.Value.Handle, id);
    }

    public bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        foreach (var id in _ids.ToList())
            Unregister(id);
        _handler.Dispose();
        _window.Dispose();
    }

    private bool OnMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_HOTKEY)
            return false;
        Pressed?.Invoke(this, (int)wParam);
        return true;
    }
}

/// <summary>
/// A dispatcher timer for the 50 ms release check of the panel hotkey: the one short timer outside the scheduler,
/// which cannot go below a second. It runs only while the key is held.
/// </summary>
internal sealed class DispatcherTicker : ITicker
{
    public IDisposable Start(TimeSpan every, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = every };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new Stop(timer);
    }

    private sealed class Stop(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}

/// <summary>
/// Hotkeys Windows refused because another app holds the same keys, by setting key. The hotkeys feature writes
/// it and Settings shows "Used by another app" under those boxes.
/// </summary>
internal static class HotkeyConflicts
{
    public static IReadOnlySet<string> Keys { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    public static void Set(IEnumerable<string> keys) => Keys = new HashSet<string>(keys, StringComparer.Ordinal);
}
