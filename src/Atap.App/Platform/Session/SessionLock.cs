using Microsoft.Win32;

namespace Atap.Platform.Session;

/// <summary>Whether this Windows session is locked, for reminders that should wait while nobody is at the PC.</summary>
internal interface ISessionLock : IDisposable
{
    bool IsLocked { get; }

    /// <summary>Raised on the UI thread when the session locks or unlocks.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// <see cref="SystemEvents.SessionSwitch"/>, posted to the UI thread. Windows raises it on its own thread, and
/// only for lock and unlock does this pass it on. Unlocked at start, since Atap only runs in a signed-in session.
/// </summary>
internal sealed class WindowsSessionLock : ISessionLock
{
    private readonly SynchronizationContext _ui;
    private bool _disposed;

    public WindowsSessionLock(SynchronizationContext ui)
    {
        _ui = ui;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public bool IsLocked { get; private set; }

    public event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        bool? locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock => true,
            SessionSwitchReason.SessionUnlock => false,
            _ => null,
        };
        if (locked is not { } value)
            return;
        _ui.Post(_ =>
        {
            if (_disposed || IsLocked == value)
                return;
            IsLocked = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }, null);
    }
}
