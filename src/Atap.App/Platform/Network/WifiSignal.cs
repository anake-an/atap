using System.Runtime.CompilerServices;
using Windows.Networking.Connectivity;

namespace Atap.Platform.Network;

/// <param name="OnWifi">Connected over Wi-Fi.</param>
/// <param name="Bars">Signal from 0 to 5 bars, or null when Windows does not say.</param>
internal readonly record struct WifiSignalReading(bool OnWifi, int? Bars);

/// <summary>Wi-Fi signal bars. Windows raises no event when the signal changes, so it is read when shown.</summary>
internal interface IWifiSignal
{
    WifiSignalReading Read();
}

/// <summary>The bars Windows shows in the taskbar, from the Wi-Fi connection profile. No Location needed.</summary>
internal sealed class WindowsWifiSignal : IWifiSignal
{
    public const int MaxBars = 5;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public WifiSignalReading Read()
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
                return new WifiSignalReading(false, null);
            return new WifiSignalReading(true, profile.GetSignalBars() is { } bars ? Math.Clamp((int)bars, 0, MaxBars) : null);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new WifiSignalReading(false, null);
        }
    }
}
