namespace Atap.Core.Net;

/// <summary>Whether the PC is online, from Windows network events. Never polled.</summary>
public interface INetworkState
{
    bool IsOnline { get; }

    /// <summary>A metered connection, such as a phone hotspot. Refreshes slow down on it.</summary>
    bool IsMetered { get; }

    /// <summary>The Wi-Fi name, only while <see cref="WantSsid"/> is held and Windows allows it.</summary>
    string? WifiSsid { get; }

    SsidAvailability Ssid { get; }

    /// <summary>Starts reading the Wi-Fi name. Windows may ask for Location. Dispose to stop.</summary>
    IDisposable WantSsid();

    /// <summary>Raised on the UI thread.</summary>
    event EventHandler? Changed;
}

public enum SsidAvailability
{
    /// <summary>Nobody asked for it.</summary>
    NotRequested,
    Available,

    /// <summary>Connected, but not over Wi-Fi.</summary>
    NotOnWifi,

    /// <summary>Windows Location is off or denied, so the name cannot be read.</summary>
    LocationDenied,
    Unavailable,
}
