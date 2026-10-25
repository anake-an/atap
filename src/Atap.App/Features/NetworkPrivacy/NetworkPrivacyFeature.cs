using System.IO;
using Atap.Composition;
using Atap.Core.Net;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Diagnostics;
using Atap.Features.NetworkPrivacy;

[assembly: AtapFeature(typeof(NetworkPrivacyDescriptor))]

namespace Atap.Features.NetworkPrivacy;

/// <summary>
/// The Privacy page's network settings: the offline switch, and the Wi-Fi name option for rules per network.
/// Always on, in safe mode too, so the offline switch always holds.
/// </summary>
internal sealed class NetworkPrivacyDescriptor : IFeatureDescriptor
{
    public const string FeatureId = "network";
    public const string OfflineKey = "network.offline";
    public const string WifiNameKey = "network.wifiName";

    /// <summary>The HTTP cache's folder inside <see cref="Core.Storage.AtapPaths.Cache"/>.</summary>
    public const string HttpCacheFolder = "http";

    public string Id => FeatureId;

    /// <summary>
    /// The Wi-Fi name is under More options and stays on this PC: turning it on may make Windows ask for
    /// Location, which only makes sense per PC.
    /// </summary>
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(OfflineKey, "network.offline", SettingType.Bool, false)
        {
            SectionId = SettingsSections.Privacy,
            DescriptionKey = "network.offlineHint",
        },
        new(WifiNameKey, "network.wifiName", SettingType.Bool, false)
        {
            SectionId = SettingsSections.Privacy,
            DescriptionKey = "network.wifiNameHint",
            Advanced = true,
            Scope = SettingScope.Machine,
        },
    ];

    public IReadOnlyCollection<string> EnabledBy => [];

    public bool IsEnabled(SettingValues settings, AppMode mode) => true;

    public IAppFeature Create(FeatureContext context) => new NetworkPrivacyFeature(
        context.Http,
        context.Settings,
        context.Network,
        Path.Combine(context.Paths.Cache, HttpCacheFolder),
        Log.Warn);
}

/// <summary>
/// Makes the HTTP service follow the settings: the offline switch, the disk cache folder, and a Wi-Fi name
/// lease while that option is on. Logs failed requests by site and status only. Holds no timer.
/// </summary>
internal sealed class NetworkPrivacyFeature : IAppFeature
{
    private readonly HttpDataService _http;
    private readonly IAppSettings _settings;
    private readonly INetworkState _network;
    private readonly string _cacheFolder;
    private readonly Action<string, Exception?> _log;
    private IDisposable? _wifiName;

    /// <param name="http">The shared HTTP service.</param>
    /// <param name="settings">Where the switches live.</param>
    /// <param name="network">Gives the Wi-Fi name while it is wanted.</param>
    /// <param name="cacheFolder">The HTTP disk cache folder.</param>
    /// <param name="log">Writes a warning line.</param>
    public NetworkPrivacyFeature(HttpDataService http, IAppSettings settings, INetworkState network, string cacheFolder, Action<string, Exception?> log)
    {
        _http = http;
        _settings = settings;
        _network = network;
        _cacheFolder = cacheFolder;
        _log = log;
    }

    /// <summary>True while the Wi-Fi name is wanted.</summary>
    internal bool WantsWifiName => _wifiName is not null;

    public Task StartAsync()
    {
        _http.UseDiskCache(_cacheFolder);
        Apply();
        _settings.Changed += OnSettingsChanged;
        _http.Attempted += OnAttempted;
        return Task.CompletedTask;
    }

    /// <summary>Leaves the offline switch as it is: this runs at shutdown, and nothing should go out then either.</summary>
    public ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        _http.Attempted -= OnAttempted;
        _wifiName?.Dispose();
        _wifiName = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>The log line for a failed request: the site and what went wrong, never the path or query.</summary>
    internal static string? Describe(FetchAttempt attempt)
    {
        // Successes are not logged, and neither are requests the user stopped with the offline switch
        // or that never went out (offline, waiting after failures).
        if (attempt.Status is FetchStatus.Fresh || attempt.Error is FetchError.None or FetchError.OfflineMode or FetchError.Offline or FetchError.BackingOff)
            return null;
        var status = attempt.StatusCode is { } code ? $", HTTP {code}" : string.Empty;
        return $"A web request to {attempt.Site} did not work: {attempt.Error}{status}.";
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(NetworkPrivacyDescriptor.OfflineKey) || e.Keys.Contains(NetworkPrivacyDescriptor.WifiNameKey))
            Apply();
    }

    private void Apply()
    {
        var values = _settings.Values;
        _http.OfflineMode = values.GetBool(NetworkPrivacyDescriptor.OfflineKey);

        var wantWifiName = values.GetBool(NetworkPrivacyDescriptor.WifiNameKey);
        if (wantWifiName && _wifiName is null)
        {
            _wifiName = _network.WantSsid();
        }
        else if (!wantWifiName && _wifiName is not null)
        {
            _wifiName.Dispose();
            _wifiName = null;
        }
    }

    private void OnAttempted(object? sender, FetchAttempt attempt)
    {
        if (Describe(attempt) is { } line)
            _log(line, null);
    }
}
