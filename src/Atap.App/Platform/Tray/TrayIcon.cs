using System.Runtime.InteropServices;
using System.Windows.Interop;
using Atap.Interop;

namespace Atap.Platform.Tray;

/// <summary>
/// Atap's icon in the notification area by the clock. A click, Enter or Space raises <see cref="Opened"/>, and a
/// right click or the menu key raises <see cref="MenuRequested"/>. It puts itself back when Explorer restarts.
/// Create and use it on the UI thread. It runs nothing between clicks.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int CallbackMessage = NativeMethods.WM_APP + 16;

    private readonly HwndSource _window;
    private readonly int _taskbarCreated;
    private readonly IntPtr _icon;
    private readonly bool _ownsIcon;
    private string _tooltip;
    private bool _disposed;

    public TrayIcon(string tooltip)
    {
        _tooltip = tooltip;
        // A hidden window of its own, never shown. It takes the icon's messages, and it can take the foreground
        // so the menu closes when the user clicks elsewhere.
        _window = new HwndSource(new HwndSourceParameters("Atap tray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = NativeMethods.WS_POPUP,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
        });
        _window.AddHook(WndProc);
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        (_icon, _ownsIcon) = LoadIcon();
        Add();
    }

    public event EventHandler? Opened;

    public event EventHandler? MenuRequested;

    /// <summary>The hidden window behind the icon. The menu brings it to the front before it opens.</summary>
    public IntPtr Handle => _window.Handle;

    /// <summary>The words shown when the pointer rests on the icon.</summary>
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            if (_tooltip == value)
                return;
            _tooltip = value;
            var data = Data(NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var data = Data(0);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        if (_ownsIcon)
            NativeMethods.DestroyIcon(_icon);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }

    // The exe carries the Atap icon, so the tray matches the taskbar and Start. Windows' plain app icon otherwise.
    private static (IntPtr Icon, bool Owned) LoadIcon()
    {
        if (Environment.ProcessPath is { } path)
        {
            var small = new IntPtr[1];
            if (NativeMethods.ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                return (small[0], true);
        }
        return (NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION), false);
    }

    private void Add()
    {
        var data = Data(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        // Version 4 sends select and context menu events, keyboard included.
        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private NativeMethods.NOTIFYICONDATAW Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            switch ((int)((long)lParam & 0xFFFF))
            {
                case NativeMethods.NIN_SELECT:
                case NativeMethods.NIN_KEYSELECT:
                    Opened?.Invoke(this, EventArgs.Empty);
                    break;
                case NativeMethods.WM_CONTEXTMENU:
                    MenuRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            handled = true;
        }
        else if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            // Explorer restarted and forgot every icon.
            Add();
        }
        return IntPtr.Zero;
    }
}
