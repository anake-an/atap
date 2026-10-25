using Atap.Core.Net;

namespace Atap.Platform.Network;

/// <summary>
/// Whether the PC is online, whether the connection is metered, and the Wi-Fi name while someone wants it.
/// Event-driven: Windows network change events are the only trigger, and nothing is polled.
/// </summary>
/// <remarks>
/// Nothing starts until the state is first read or <see cref="Changed"/> gets a handler, so an Atap that
/// never fetches never touches the Windows network APIs. The Wi-Fi name is read only while a
/// <see cref="WantSsid"/> lease is held: once when the first lease is taken, then on network change events.
/// Windows may hide the name until Atap may use Location. Atap then asks once, and only while a lease is held.
/// The name never leaves the PC and is never logged.
/// </remarks>
internal sealed class NetworkState : INetworkState, IDisposable
{
    private readonly INetworkProbe _probe;
    private readonly SynchronizationContext? _ui;
    private readonly Func<Func<Task>, Task> _runInBackground;
    private readonly object _gate = new();
    private EventHandler? _changed;
    private bool _started;
    private bool _disposed;
    private bool _online = true;
    private bool _metered;
    private string? _ssid;
    private SsidAvailability _ssidState = SsidAvailability.NotRequested;
    private int _wants;
    private bool _askedForLocation;
    private int _readsStarted;
    private int _readsApplied;

    /// <summary>Must be created on the UI thread: <see cref="Changed"/> is raised there.</summary>
    public NetworkState()
        : this(new WindowsNetworkProbe(), SynchronizationContext.Current)
    {
    }

    /// <param name="probe">What Windows says about the network.</param>
    /// <param name="ui">Where <see cref="Changed"/> is raised. Inline when null.</param>
    /// <param name="runInBackground">Runs a read off the calling thread. Task.Run when null. Tests run it inline.</param>
    internal NetworkState(INetworkProbe probe, SynchronizationContext? ui, Func<Func<Task>, Task>? runInBackground = null)
    {
        _probe = probe;
        _ui = ui;
        _runInBackground = runInBackground ?? Task.Run;
    }

    public bool IsOnline
    {
        get
        {
            EnsureStarted();
            lock (_gate)
                return _online;
        }
    }

    public bool IsMetered
    {
        get
        {
            EnsureStarted();
            lock (_gate)
                return _metered;
        }
    }

    public string? WifiSsid
    {
        get
        {
            lock (_gate)
                return _wants > 0 && _ssidState == SsidAvailability.Available ? _ssid : null;
        }
    }

    public SsidAvailability Ssid
    {
        get
        {
            lock (_gate)
                return _wants > 0 ? _ssidState : SsidAvailability.NotRequested;
        }
    }

    /// <summary>True once Windows network events are followed.</summary>
    internal bool IsStarted
    {
        get
        {
            lock (_gate)
                return _started;
        }
    }

    /// <summary>Raised on the UI thread when the connection, its cost or the Wi-Fi name changes.</summary>
    public event EventHandler? Changed
    {
        add
        {
            EnsureStarted();
            lock (_gate)
                _changed += value;
        }
        remove
        {
            lock (_gate)
                _changed -= value;
        }
    }

    public IDisposable WantSsid()
    {
        EnsureStarted();
        bool first;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            first = ++_wants == 1;
        }
        if (first)
            _ = _runInBackground(RefreshAsync);
        return new Want(this);
    }

    public void Dispose()
    {
        bool stop;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            stop = _started;
            _changed = null;
        }
        if (!stop)
            return;
        _probe.StatusChanged -= OnStatusChanged;
        _probe.Stop();
    }

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;
            _started = true;
        }
        _probe.StatusChanged += OnStatusChanged;
        _probe.Start();
        var reading = _probe.Read();
        lock (_gate)
        {
            _online = reading.IsOnline;
            _metered = reading.IsMetered;
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => _ = _runInBackground(RefreshAsync);

    /// <summary>Reads the network, and the Wi-Fi name while it is wanted, then applies the result on the UI thread.</summary>
    private async Task RefreshAsync()
    {
        int read;
        bool wantSsid;
        lock (_gate)
        {
            if (_disposed)
                return;
            read = ++_readsStarted;
            wantSsid = _wants > 0;
        }

        var reading = _probe.Read();
        var ssid = (State: SsidAvailability.NotRequested, Name: (string?)null);
        if (wantSsid)
            ssid = await ReadSsidAsync().ConfigureAwait(false);
        Post(() => Apply(read, reading, wantSsid, ssid.State, ssid.Name));
    }

    private async Task<(SsidAvailability State, string? Name)> ReadSsidAsync()
    {
        var wifi = _probe.ReadWifi();
        if (wifi.Kind == WifiKind.NameHidden)
        {
            bool ask;
            lock (_gate)
            {
                ask = !_askedForLocation && _wants > 0;
                _askedForLocation = true;
            }
            if (ask && await AskForLocationAsync().ConfigureAwait(false))
                wifi = _probe.ReadWifi();
        }
        return wifi.Kind switch
        {
            WifiKind.Named => (SsidAvailability.Available, wifi.Name),
            WifiKind.NotOnWifi => (SsidAvailability.NotOnWifi, null),
            WifiKind.NameHidden or WifiKind.Denied => (SsidAvailability.LocationDenied, null),
            _ => (SsidAvailability.Unavailable, null),
        };
    }

    /// <summary>Asks on the UI thread, where Windows can show its Location prompt.</summary>
    private Task<bool> AskForLocationAsync()
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            return _probe.RequestLocationAsync();
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(
            async _ =>
            {
                try
                {
                    answer.SetResult(await _probe.RequestLocationAsync());
                }
                catch (Exception)
                {
                    // An answer that never comes counts as no.
                    answer.TrySetResult(false);
                }
            },
            null);
        return answer.Task;
    }

    private void Apply(int read, NetworkReading reading, bool readSsid, SsidAvailability ssidState, string? ssid)
    {
        EventHandler? changed;
        lock (_gate)
        {
            // A slower, older read never replaces a newer one.
            if (_disposed || read < _readsApplied)
                return;
            _readsApplied = read;
            var different = _online != reading.IsOnline || _metered != reading.IsMetered;
            _online = reading.IsOnline;
            _metered = reading.IsMetered;
            if (readSsid && _wants > 0)
            {
                different |= _ssidState != ssidState || !string.Equals(_ssid, ssid, StringComparison.Ordinal);
                _ssidState = ssidState;
                _ssid = ssid;
            }
            changed = different ? _changed : null;
        }
        changed?.Invoke(this, EventArgs.Empty);
    }

    private void Release()
    {
        EventHandler? changed = null;
        lock (_gate)
        {
            if (_wants == 0)
                return;
            if (--_wants == 0)
            {
                var had = _ssidState != SsidAvailability.NotRequested;
                _ssid = null;
                _ssidState = SsidAvailability.NotRequested;
                if (had && !_disposed)
                    changed = _changed;
            }
        }
        if (changed is not null)
            Post(() => changed(this, EventArgs.Empty));
    }

    private void Post(Action action)
    {
        if (_ui is null)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private sealed class Want(NetworkState owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release();
        }
    }
}
