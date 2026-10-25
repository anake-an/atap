using Atap.Core.Notch;

namespace Atap.Platform.Foreground;

/// <summary>
/// Reports when a full-screen app (game, video, presentation) is in front, and the front app's exe name while
/// someone holds <see cref="WantProcessNames"/>.
/// Event-driven: re-checks only when the foreground window changes.
/// </summary>
/// <remarks>
/// Must be created on a thread with a message loop (the UI thread); events are raised there.
/// The exe name is only the file name, such as "valorant.exe". It is read only while a lease is held and never logged.
/// </remarks>
internal sealed class ForegroundState : IForegroundState, IDisposable
{
    // Held in a field so the GC does not collect the delegate while Windows still calls it.
    private readonly ForegroundNative.WinEventProc _callback;
    private IntPtr _hook;
    private int _nameLeases;

    public ForegroundState()
    {
        _callback = OnForegroundChanged;
        _hook = ForegroundNative.SetWinEventHook(
            ForegroundNative.EVENT_SYSTEM_FOREGROUND,
            ForegroundNative.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            ForegroundNative.WINEVENT_OUTOFCONTEXT | ForegroundNative.WINEVENT_SKIPOWNPROCESS);
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
        if (_hook == IntPtr.Zero)
            return;
        ForegroundNative.UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    private void OnForegroundChanged(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        var fullScreen = QueryFullScreen();
        var changed = fullScreen != FullScreen;
        FullScreen = fullScreen;
        if (_nameLeases > 0)
            changed |= UpdateName(hwnd, raise: false);
        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
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
