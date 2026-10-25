using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Atap.Core.Privacy;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Atap.Platform.Privacy;

/// <summary>The apps using the camera or the microphone. Faked in tests.</summary>
internal interface ISensorWatcher : IDisposable
{
    IReadOnlyList<SensorUse> Current { get; }

    /// <summary>Raised on the UI thread when <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Reads the registry again. Drops apps that closed without telling Windows.</summary>
    void Refresh();
}

/// <summary>
/// Watches HKCU\...\CapabilityAccessManager\ConsentStore with RegNotifyChangeKeyValue, so a change shows within a
/// moment and nothing polls. The watch lives only while this object does.
/// </summary>
internal sealed class RegistrySensorWatcher : ISensorWatcher
{
    private const string StorePath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    private const uint NotifyName = 0x1;
    private const uint NotifyLastSet = 0x4;
    private const uint NotifyThreadAgnostic = 0x10000000;

    private readonly SynchronizationContext _ui;
    private readonly RegistryKey? _store;
    private readonly AutoResetEvent _signal = new(false);
    private readonly RegisteredWaitHandle? _wait;
    private readonly object _gate = new();
    private IReadOnlyList<SensorUse> _current = [];
    private bool _disposed;

    public RegistrySensorWatcher(SynchronizationContext ui)
    {
        _ui = ui;
        _store = Registry.CurrentUser.OpenSubKey(StorePath);
        if (_store is null)
            return;
        _current = Read(_store);
        if (Arm())
            _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) => OnSignal(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public IReadOnlyList<SensorUse> Current => _current;

    public event EventHandler? Changed;

    public void Refresh() => ThreadPool.QueueUserWorkItem(_ => Reread());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _wait?.Unregister(null);
            _store?.Dispose();
            _signal.Dispose();
        }
    }

    private bool Arm()
    {
        if (_store is null)
            return false;
        return RegNotifyChangeKeyValue(_store.Handle, true, NotifyName | NotifyLastSet | NotifyThreadAgnostic, _signal.SafeWaitHandle, true) == 0;
    }

    // On a pool thread. Re-arm first, so a change made while reading is not missed.
    private void OnSignal()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            Arm();
        }
        Reread();
    }

    private void Reread()
    {
        IReadOnlyList<SensorUse> next;
        lock (_gate)
        {
            if (_disposed || _store is null)
                return;
            next = Read(_store);
        }
        _ui.Post(_ =>
        {
            if (_disposed || next.SequenceEqual(_current))
                return;
            _current = next;
            Changed?.Invoke(this, EventArgs.Empty);
        }, null);
    }

    private static IReadOnlyList<SensorUse> Read(RegistryKey store)
    {
        var entries = new List<ConsentEntry>();
        try
        {
            ReadSensor(store, "webcam", Sensor.Camera, entries);
            ReadSensor(store, "microphone", Sensor.Microphone, entries);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
        {
            return [];
        }
        return ConsentUsage.InUse(entries, IsRunning);
    }

    private static void ReadSensor(RegistryKey store, string name, Sensor sensor, List<ConsentEntry> entries)
    {
        using var key = store.OpenSubKey(name);
        if (key is null)
            return;
        foreach (var app in key.GetSubKeyNames())
        {
            if (app.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                using var nonPackaged = key.OpenSubKey(app);
                if (nonPackaged is null)
                    continue;
                foreach (var exe in nonPackaged.GetSubKeyNames())
                    Add(nonPackaged, exe, false);
            }
            else
            {
                Add(key, app, true);
            }
        }

        void Add(RegistryKey parent, string keyName, bool packaged)
        {
            using var entry = parent.OpenSubKey(keyName);
            if (entry is null)
                return;
            entries.Add(new ConsentEntry(sensor, keyName, packaged, Qword(entry, "LastUsedTimeStart"), Qword(entry, "LastUsedTimeStop")));
        }
    }

    private static long Qword(RegistryKey key, string name) => key.GetValue(name) is long value ? value : 0;

    private static bool IsRunning(string exePath)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
        foreach (var process in processes)
            process.Dispose();
        return processes.Length > 0;
    }

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter, SafeWaitHandle signal, bool asynchronous);
}
