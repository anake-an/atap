using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Network;
using Atap.Widgets.Wifi;

[assembly: AtapWidget(typeof(WifiDescriptor))]

namespace Atap.Widgets.Wifi;

/// <summary>Wi-Fi. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class WifiDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "wifi";
    public const string ShowNameKey = "showName";

    public string TypeId => TypeIdValue;

    public string NameKey => "wifi.name";

    public string DescriptionKey => "wifi.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    // The name can need Windows Location, so it is off until asked for, and kept on this PC.
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(ShowNameKey, "wifi.showName", SettingType.Bool, false) { DescriptionKey = "wifi.showNameHint", Scope = SettingScope.Machine },
    ];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Location, []);

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Wifi/WifiViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new WifiWidget(context, new WindowsWifiSignal());
}

/// <summary>
/// Follows the network events. The signal has no event, so it is read when the card opens and every 30 seconds
/// while it stays open, never while hidden. The name is read only when the setting asks for it.
/// </summary>
public sealed class WifiWidget : WidgetBase
{
    public static readonly TimeSpan SignalRefresh = TimeSpan.FromSeconds(30);

    private readonly WifiViewModel _viewModel;
    private readonly IWifiSignal _signal;
    private IDisposable? _nameLease;
    private IDisposable? _tick;
    private bool _showName;

    internal WifiWidget(WidgetContext context, IWifiSignal signal)
        : base(context)
    {
        _signal = signal;
        _viewModel = new WifiViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _showName = settings.GetBool(WifiDescriptor.ShowNameKey);
        if (_showName)
            _nameLease = Context.Network.WantSsid();
        Context.Network.Changed += OnChanged;
        Context.Visibility.Changed += OnChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Network.Changed -= OnChanged;
        Context.Visibility.Changed -= OnChanged;
        _nameLease?.Dispose();
        _nameLease = null;
        _tick?.Dispose();
        _tick = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var network = Context.Network;
        var shown = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        var reading = shown ? _signal.Read() : _viewModel.LastSignal;
        _viewModel.Show(network.IsOnline, network.IsMetered, reading, _showName ? network.Ssid : SsidAvailability.NotRequested, network.WifiSsid);

        if (shown && _tick is null)
            _tick = Context.Scheduler.Every(SignalRefresh, Refresh, new ScheduleOptions(TimeSpan.FromSeconds(3), true));
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}

public sealed class WifiViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private bool _online;
    private bool _metered;
    private SsidAvailability _ssid;
    private string? _name;

    public WifiViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    internal WifiSignalReading LastSignal { get; private set; }

    public bool OnWifi => _online && LastSignal.OnWifi;

    /// <summary>The network's name, "Connected", "Connected, not over Wi-Fi" or "Not connected".</summary>
    public string Title =>
        !_online ? _text["wifi.offline"]
        : !LastSignal.OnWifi ? _text["wifi.notWifi"]
        : _ssid == SsidAvailability.Available && !string.IsNullOrEmpty(_name) ? _name
        : _text["wifi.connected"];

    /// <summary>"Strong signal" and so on, or empty when not on Wi-Fi.</summary>
    public string SignalText => OnWifi && LastSignal.Bars is { } bars
        ? _text[bars switch { >= 4 => "wifi.signal.strong", >= 2 => "wifi.signal.good", _ => "wifi.signal.weak" }]
        : string.Empty;

    public double Bars => OnWifi ? LastSignal.Bars ?? 0 : 0;

    public bool HasBars => OnWifi && LastSignal.Bars is not null;

    public bool IsMetered => _online && _metered;

    /// <summary>Why the name is not shown although it was asked for.</summary>
    public string? NameNote => OnWifi && _ssid == SsidAvailability.LocationDenied ? _text["wifi.locationOff"] : null;

    public bool HasNameNote => NameNote is not null;

    public string AccessibleSummary => _text.Format("wifi.accessible", ("title", Title), ("signal", SignalText));

    internal void Show(bool online, bool metered, WifiSignalReading signal, SsidAvailability ssid, string? name)
    {
        _online = online;
        _metered = metered;
        LastSignal = signal;
        _ssid = ssid;
        _name = name;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
