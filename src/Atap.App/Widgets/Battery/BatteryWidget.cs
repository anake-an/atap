using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Battery;

[assembly: AtapWidget(typeof(BatteryDescriptor))]

namespace Atap.Widgets.Battery;

/// <summary>The battery. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class BatteryDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "battery";
    public const string LowAtKey = "lowAt";
    public const string LowAlertKey = "lowAlert";

    public string TypeId => TypeIdValue;

    public string NameKey => "battery.name";

    public string DescriptionKey => "battery.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(LowAlertKey, "battery.lowAlert", SettingType.Bool, true),
        new(LowAtKey, "battery.lowAt", SettingType.Int, 20) { Min = 5, Max = 50, Step = 5, UnitKey = "battery.percent", VisibleWhen = (LowAlertKey, true) },
    ];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Battery/BatteryViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new BatteryWidget(context);
}

/// <summary>
/// Follows the Windows power events, so it has no timer of its own. Plugging in makes it live for 10 seconds, so
/// the compact notch says "Charging". Falling to the low mark gives one alert per discharge.
/// </summary>
public sealed class BatteryWidget(WidgetContext context) : WidgetBase(context)
{
    /// <summary>How long the compact notch shows "Charging" after the plug goes in.</summary>
    public static readonly TimeSpan PluggedInLive = TimeSpan.FromSeconds(10);

    private readonly BatteryViewModel _viewModel = new(context.Text);
    private bool _lowAlert;
    private int _lowAt;
    private bool? _wasOnBattery;
    private bool _alertedThisDischarge;
    private IDisposable? _liveEnds;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _lowAlert = settings.GetBool(BatteryDescriptor.LowAlertKey);
        _lowAt = Math.Clamp(settings.GetInt(BatteryDescriptor.LowAtKey), 5, 50);
        _wasOnBattery = null;
        Context.Power.Changed += OnPowerChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Power.Changed -= OnPowerChanged;
        EndLive();
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnPowerChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var power = Context.Power;
        var percent = power.BatteryPercent;
        var onBattery = power.OnBattery;

        if (percent is not null && _wasOnBattery == true && !onBattery)
            StartLive();
        if (!onBattery)
            _alertedThisDischarge = false;
        else if (percent is { } level && _lowAlert && level <= _lowAt && !_alertedThisDischarge)
        {
            _alertedThisDischarge = true;
            Context.Alerts.Show(_viewModel.LowAlert(level));
        }
        _wasOnBattery = onBattery;

        _viewModel.Show(percent, onBattery, power.EnergySaverOn, _lowAt, _liveEnds is not null);
    }

    private void StartLive()
    {
        _liveEnds?.Dispose();
        _liveEnds = Context.Scheduler.At(Context.Time.GetUtcNow() + PluggedInLive, () =>
        {
            _liveEnds = null;
            SetIsLive(false);
            Refresh();
        }, new ScheduleOptions(TimeSpan.Zero, false, MissedRun.Skip));
        SetIsLive(true);
    }

    private void EndLive()
    {
        _liveEnds?.Dispose();
        _liveEnds = null;
        SetIsLive(false);
    }
}

public sealed class BatteryViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private int? _percent;
    private bool _onBattery;
    private bool _saver;
    private int _lowAt = 20;
    private bool _justPlugged;

    public BatteryViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public bool HasBattery => _percent is not null;

    public bool HasNoBattery => _percent is null;

    public double Percent => _percent ?? 0;

    /// <summary>"64%".</summary>
    public string PercentText => _percent is { } p ? p.ToString(CultureInfo.CurrentCulture) + "%" : string.Empty;

    public bool IsCharging => HasBattery && !_onBattery;

    /// <summary>"Charging", "Plugged in, full", "On battery" or "No battery".</summary>
    public string StatusText => _text[
        _percent is null ? "battery.none"
        : !_onBattery && _percent >= 100 ? "battery.full"
        : !_onBattery ? "battery.charging"
        : "battery.onBattery"];

    /// <summary>For the compact notch: "Charging" while just plugged in.</summary>
    public string CompactText => _justPlugged ? _text["battery.charging"] : StatusText;

    public bool SaverOn => _saver;

    public string SaverText => _text["battery.saver"];

    /// <summary>Amber at the low mark, rose at half of it.</summary>
    public ValueLevel Level =>
        _percent is not { } p || !_onBattery ? ValueLevel.Normal
        : p <= _lowAt / 2 ? ValueLevel.Critical
        : p <= _lowAt ? ValueLevel.Warning
        : ValueLevel.Normal;

    public string AccessibleSummary => HasBattery
        ? _text.Format("battery.accessible", ("percent", PercentText), ("status", StatusText))
        : _text["battery.accessibleNone"];

    public void Show(int? percent, bool onBattery, bool saver, int lowAt, bool justPlugged)
    {
        _percent = percent;
        _onBattery = onBattery;
        _saver = saver;
        _lowAt = lowAt;
        _justPlugged = justPlugged;
        OnPropertyChanged(string.Empty);
    }

    public Alert LowAlert(int percent) =>
        new("battery.low", AlertPriority.High, _text.Format("battery.lowTitle", ("percent", percent.ToString(CultureInfo.CurrentCulture) + "%")))
        {
            Subtitle = _text["battery.lowSubtitle"],
            IconKey = "battery",
            Tone = AlertTone.Warning,
            Level = percent / 100.0,
        };

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
