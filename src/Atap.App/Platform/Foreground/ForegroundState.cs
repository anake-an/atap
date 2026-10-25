using Atap.Core.Notch;

namespace Atap.Platform.Foreground;

/// <summary>
/// Reports when a full-screen app (game, video, presentation) is in front, and the front app's exe name while
/// someone holds <see cref="WantProcessNames"/>.
/// Event-driven: re-checks when the foreground window changes, and when that window moves or resizes, so a
/// window that goes full screen by itself (F11, a video's full screen button) is noticed too.
/// </summary>
/// <remarks>
/// Must be created on a thread with a message loop (the UI thread); events are raised there.
/// The exe name is only the file name, such as "valorant.exe". It is read only while a lease is held and never logged.
/// </remarks>
internal sealed class ForegroundState : IForegroundState, IDisposable
{
    // Held in fields so the GC does not collect the delegates while Windows still calls them.
    private readonly ForegroundNative.WinEventProc _callback;
    private readonly ForegroundNative.WinEventProc _moveCallback;
    private IntPtr _hook;
    private IntPtr _moveHook;
    private IntPtr _front;
    private int _nameLeases;

    public ForegroundState()
    {
        _callback = OnForegroundChanged;
        _moveCallback = OnLocationChanged;
        _hook = ForegroundNative.SetWinEventHook(
            ForegroundNative.EVENT_SYSTEM_FOREGROUND,
            ForegroundNative.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            ForegroundNative.WINEVENT_OUTOFCONTEXT | ForegroundNative.WINEVENT_SKIPOWNPROCESS);
        WatchMoves(ForegroundNative.GetForegroundWindow());
        FullScreen = QueryFullScreen();
    }

    public FullScreenKind FullScreen { get; private set; }

    public string? ProcessName { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Reads the front app's exe name from now on, until every lease is disposed.</summary>
    public IDisposable WantProcessNames()
    {
        if (_nameLeases++ == 0)
            UpdateName(ForegroundNative.GetForegroundWindow(), raise: true);
        return new NameLease(this);
    }

    public void Dispose()
    {
        StopWatchingMoves();
        if (_hook == IntPtr.Zero)
            return;
        ForegroundNative.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnForegroundChanged(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (_hook == IntPtr.Zero)
            return;
        WatchMoves(hwnd);
        var fullScreen = QueryFullScreen();
        var changed = fullScreen != FullScreen;
        FullScreen = fullScreen;
        if (_nameLeases > 0)
            changed |= UpdateName(hwnd, raise: false);
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The front window moved or resized, such as going full screen with F11. Other objects and windows are ignored.</summary>
    private void OnLocationChanged(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (hwnd != _front || idObject != ForegroundNative.OBJID_WINDOW || idChild != ForegroundNative.CHILDID_SELF || _hook == IntPtr.Zero)
            return;
        var fullScreen = QueryFullScreen();
        if (fullScreen == FullScreen)
            return;
        FullScreen = fullScreen;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Listens for moves in the front window's process only, so the many location events of other apps (and of
    /// the mouse pointer) never reach Atap. Atap's own windows are not watched.
    /// </summary>
    private void WatchMoves(IntPtr window)
    {
        if (window == _front && _moveHook != IntPtr.Zero)
            return;
        StopWatchingMoves();
        _front = window;
        if (window == IntPtr.Zero || ForegroundNative.GetWindowThreadProcessId(window, out var processId) == 0
            || processId == 0 || processId == (uint)Environment.ProcessId)
            return;
        _moveHook = ForegroundNative.SetWinEventHook(
            ForegroundNative.EVENT_OBJECT_LOCATIONCHANGE,
            ForegroundNative.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero,
            _moveCallback,
            processId,
            0,
            ForegroundNative.WINEVENT_OUTOFCONTEXT);
    }

    private void StopWatchingMoves()
    {
        _front = IntPtr.Zero;
        if (_moveHook == IntPtr.Zero)
            return;
        ForegroundNative.UnhookWinEvent(_moveHook);
        _moveHook = IntPtr.Zero;
    }

    private void Release()
    {
        if (_nameLeases == 0 || --_nameLeases > 0)
            return;
        if (ProcessName is null)
            return;
        ProcessName = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool UpdateName(IntPtr window, bool raise)
    {
        var name = ProcessNameOf(window);
        if (string.Equals(name, ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;
        ProcessName = name;
        if (raise)
            Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>"valorant.exe", or null when the process cannot be opened (an elevated app, for one).</summary>
    private static string? ProcessNameOf(IntPtr window)
    {
        if (window == IntPtr.Zero || ForegroundNative.GetWindowThreadProcessId(window, out var processId) == 0 || processId == 0)
            return null;
        var process = ForegroundNative.OpenProcess(ForegroundNative.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero)
            return null;
        try
        {
            var buffer = new char[1024];
            var size = (uint)buffer.Length;
            return ForegroundNative.QueryFullProcessImageName(process, 0, buffer, ref size)
                ? System.IO.Path.GetFileName(new string(buffer, 0, (int)size))
                : null;
        }
        finally
        {
            ForegroundNative.CloseHandle(process);
        }
    }

    private static FullScreenKind QueryFullScreen()
    {
        if (ForegroundNative.SHQueryUserNotificationState(out var state) != 0)
            return FullScreenKind.None;
        return state switch
        {
            ForegroundNative.QueryUserNotificationState.Busy => FullScreenKind.FullScreen,
            ForegroundNative.QueryUserNotificationState.RunningD3DFullScreen => FullScreenKind.Game,
            ForegroundNative.QueryUserNotificationState.PresentationMode => FullScreenKind.Presentation,
            _ => FullScreenKind.None,
        };
    }

    private sealed class NameLease(ForegroundState owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            owner.Release();
        }
    }
}
