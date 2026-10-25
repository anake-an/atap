using System.Windows.Interop;
using Atap.Core.Shared;
using Atap.Interop;

namespace Atap.Platform.Input;

/// <summary>
/// One hidden message-only window that hotkeys, and later raw input and the clipboard, share. A shared source:
/// made on the first lease and gone 30 seconds after the last. It never shows and survives monitor changes.
/// </summary>
internal sealed class MessageWindow : ISharedService<MessageWindow>
{
    private readonly HwndSource _source;
    private readonly List<Func<int, IntPtr, IntPtr, bool>> _handlers = [];

    private MessageWindow()
    {
        _source = new HwndSource(new HwndSourceParameters("Atap messages")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    public static MessageWindow Create(SharedServiceContext context) => new();

    /// <summary>Offers every message to <paramref name="handler"/> until disposed. A handler returns true when it used the message.</summary>
    public IDisposable AddHandler(Func<int, IntPtr, IntPtr, bool> handler)
    {
        _handlers.Add(handler);
        return new Registration(() => _handlers.Remove(handler));
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        foreach (var handler in _handlers.ToList())
        {
            if (handler(msg, wParam, lParam))
            {
                handled = true;
                break;
            }
        }
        return IntPtr.Zero;
    }

    private sealed class Registration(Action remove) : IDisposable
    {
        private Action? _remove = remove;

        public void Dispose()
        {
            _remove?.Invoke();
            _remove = null;
        }
    }
}
