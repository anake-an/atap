using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using Windows.Devices.Geolocation;
using Windows.Networking.Connectivity;

namespace Atap.Platform.Network;

/// <summary>What Windows says about the network. Faked in tests.</summary>
internal interface INetworkProbe
{
    /// <summary>Raised on a worker thread when Windows reports a network change.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Starts following Windows network events.</summary>
    void Start();

    void Stop();

    NetworkReading Read();

    /// <summary>The connected Wi-Fi network's name. Called only while someone wants it.</summary>
    WifiReading ReadWifi();

    /// <summary>Asks Windows for Location, which it may show as a prompt. True when allowed.</summary>
    Task<bool> RequestLocationAsync();
}

/// <param name="IsOnline">Any connection Windows can use, including one without internet.</param>
/// <param name="IsMetered">A connection with a data limit or a cost, such as a phone hotspot.</param>
internal readonly record struct NetworkReading(bool IsOnline, bool IsMetered);

internal enum WifiKind
{
    NotOnWifi,
    Named,

    /// <summary>On Wi-Fi, but Windows gave no name. Usually because Atap may not use Location.</summary>
    NameHidden,

    /// <summary>Windows refused to say.</summary>
    Denied,
    Failed,
}

internal readonly record struct WifiReading(WifiKind Kind, string? Name);

/// <summary>
/// Reads Windows.Networking.Connectivity. The Windows Runtime is reached only inside the methods, so
/// creating this class loads none of it: <see cref="NetworkState"/> calls them only once it starts.
/// </summary>
internal sealed class WindowsNetworkProbe : INetworkProbe
{
    // A NetworkStatusChangedEventHandler, kept as object so this class's layout needs no Windows Runtime type.
    private object? _handler;

    public event EventHandler? StatusChanged;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Start()
    {
        if (_handler is not null)
            return;
        try
        {
            var handler = new NetworkStatusChangedEventHandler(_ => StatusChanged?.Invoke(this, EventArgs.Empty));
            NetworkInformation.NetworkStatusChanged += handler;
            _handler = handler;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or TypeLoadException)
        {
            // Without events the state stays as first read. Requests still fail fast on their own.
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Stop()
    {
        if (_handler is not NetworkStatusChangedEventHandler handler)
            return;
        _handler = null;
        try
        {
            NetworkInformation.NetworkStatusChanged -= handler;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public NetworkReading Read()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                // No internet profile. Being wrong about offline would stop every request, so any usable
                // adapter counts as online, and a failed request shows the saved copy anyway.
                return new NetworkReading(NetworkInterface.GetIsNetworkAvailable(), false);
            }
            var online = profile.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None;
            var cost = profile.GetConnectionCost();
            var metered = cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable
                || cost.Roaming
                || cost.OverDataLimit;
            return new NetworkReading(online, metered);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new NetworkReading(NetworkInterface.GetIsNetworkAvailable(), false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WifiReading ReadWifi()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is not { IsWlanConnectionProfile: true })
            {
                profile = NetworkInformation.GetConnectionProfiles()
                    .FirstOrDefault(p => p.IsWlanConnectionProfile && p.GetNetworkConnectivityLevel() != NetworkConnectivityLevel.None);
            }
            if (profile is null)
                return new WifiReading(WifiKind.NotOnWifi, null);

            var name = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
            return string.IsNullOrEmpty(name) ? new WifiReading(WifiKind.NameHidden, null) : new WifiReading(WifiKind.Named, name);
        }
        catch (UnauthorizedAccessException)
        {
            return new WifiReading(WifiKind.Denied, null);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return new WifiReading(WifiKind.Failed, null);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task<bool> RequestLocationAsync()
    {
        try
        {
            return await Geolocator.RequestAccessAsync() == GeolocationAccessStatus.Allowed;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
