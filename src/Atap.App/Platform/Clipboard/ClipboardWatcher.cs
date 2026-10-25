using System.IO;
using System.Runtime.InteropServices;
using Atap.Core.Shared;
using Atap.Platform.Input;
using CoreHistory = Atap.Core.Clipboard.ClipboardHistory;
using WpfClipboard = System.Windows.Clipboard;

namespace Atap.Platform.Clipboard;

/// <summary>Copied text as it happens. Faked in tests.</summary>
internal interface IClipboardWatcher : IDisposable
{
    /// <summary>Raised on the UI thread with copied text that no app asked to keep private.</summary>
    event EventHandler<string>? Copied;

    /// <summary>Puts text back on the clipboard.</summary>
    void Copy(string text);
}

/// <summary>
/// AddClipboardFormatListener on the shared message window. The listener exists only while this object does,
/// so a widget that is off never sees the clipboard. Copies marked private (password managers) are skipped.
/// </summary>
internal sealed class WindowsClipboardWatcher : IClipboardWatcher
{
    private const int WmClipboardUpdate = 0x031D;

    private readonly Lease<MessageWindow> _window;
    private readonly IDisposable _handler;
    private readonly bool _listening;
    private string? _ownCopy;
    private bool _disposed;

    public WindowsClipboardWatcher(Lease<MessageWindow> window)
    {
        _window = window;
        _handler = window.Value.AddHandler(OnMessage);
        _listening = AddClipboardFormatListener(window.Value.Handle);
    }

    public event EventHandler<string>? Copied;

    public void Copy(string text)
    {
        try
        {
            _ownCopy = text;
            WpfClipboard.SetText(text);
        }
        catch (Exception e) when (e is COMException or ExternalException)
        {
            // Another app holds the clipboard. The user can click again.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_listening)
            RemoveClipboardFormatListener(_window.Value.Handle);
        _handler.Dispose();
        _window.Dispose();
    }

    private bool OnMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != WmClipboardUpdate || _disposed)
            return false;
        if (Read() is { } text)
        {
            // Our own copy back moves the item up through the same path, but is not a new copy.
            if (text != _ownCopy)
                Copied?.Invoke(this, text);
            _ownCopy = null;
        }
        return true;
    }

    private static string? Read()
    {
        try
        {
            var data = WpfClipboard.GetDataObject();
            if (data is null || !data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
                return null;
            if (CoreHistory.ShouldSkip(data.GetFormats(false), format => Dword(data, format)))
                return null;
            return data.GetData(System.Windows.DataFormats.UnicodeText) as string;
        }
        catch (Exception e) when (e is COMException or ExternalException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static int? Dword(System.Windows.IDataObject data, string format)
    {
        try
        {
            if (data.GetData(format) is MemoryStream stream && stream.Length >= 4)
            {
                var bytes = new byte[4];
                stream.Position = 0;
                stream.ReadExactly(bytes);
                return BitConverter.ToInt32(bytes);
            }
        }
        catch (Exception e) when (e is COMException or ExternalException or IOException)
        {
            // Unreadable counts as not set.
        }
        return null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr window);
}
