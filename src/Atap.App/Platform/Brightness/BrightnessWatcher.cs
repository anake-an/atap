using System.Management;

namespace Atap.Platform.Brightness;

/// <summary>The built-in screen's brightness. Faked in tests.</summary>
internal interface IBrightnessWatcher : IDisposable
{
    /// <summary>False on PCs whose screen brightness Windows cannot report, such as most desktop monitors.</summary>
    bool IsSupported { get; }

    /// <summary>Raised on the UI thread with the new level, 0 to 100.</summary>
    event EventHandler<int>? Changed;
}

/// <summary>
/// WMI's WmiMonitorBrightnessEvent, raised by laptop and tablet panels when the brightness changes. The event
/// watcher runs only while this object lives, so turning the setting off stops it.
/// </summary>
internal sealed class WmiBrightnessWatcher : IBrightnessWatcher
{
    private readonly SynchronizationContext _ui;
    private readonly ManagementEventWatcher? _watcher;
    private bool _disposed;

    public WmiBrightnessWatcher(SynchronizationContext ui)
    {
        _ui = ui;
        if (!HasPanel())
            return;
        try
        {
            _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += OnEventArrived;
            _watcher.Start();
            IsSupported = true;
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    public bool IsSupported { get; }

    public event EventHandler<int>? Changed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_watcher is null)
            return;
        _watcher.EventArrived -= OnEventArrived;
        try
        {
            _watcher.Stop();
        }
        catch (ManagementException)
        {
            // Already stopped.
        }
        _watcher.Dispose();
    }

    /// <summary>A screen that reports its brightness. Desktop monitors usually do not.</summary>
    private static bool HasPanel()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            using var results = searcher.Get();
            return results.Count > 0;
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    // Raised on a WMI thread, so it only posts to the UI thread.
    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        if (e.NewEvent?["Brightness"] is not { } value)
            return;
        var level = Math.Clamp(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture), 0, 100);
        _ui.Post(_ =>
        {
            if (!_disposed)
                Changed?.Invoke(this, level);
        }, null);
    }
}
