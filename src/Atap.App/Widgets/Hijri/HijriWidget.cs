using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Hijri;

[assembly: AtapWidget(typeof(HijriDescriptor))]

namespace Atap.Widgets.Hijri;

/// <summary>Today's Hijri date. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class HijriDescriptor : IWidgetDescriptor
{
    public string TypeId => MalaysiaKeys.HijriTypeId;

    public string NameKey => "hijri.name";

    public string DescriptionKey => "hijri.description";

    public WidgetGroup Group => WidgetGroup.Malaysia;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities { get; } =
        new(WidgetPermissions.Network, [MalaysiaKeys.EsolatHost, MalaysiaKeys.MirrorHost], "hijri.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Hijri/HijriViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new HijriWidget(context);
}

/// <summary>
/// The official date from e-Solat when an area is picked, the same monthly data as the waktu solat widget and
/// so from the same cache. Otherwise the calculated date with the offset from Settings. Wakes once a day, at
/// midnight in Malaysia.
/// </summary>
public sealed class HijriWidget(WidgetContext context) : WidgetBase(context)
{
    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce);

    private readonly HijriViewModel _viewModel = new(context.Text);
    private readonly PrayerTimesLoader _loader = new(context.Http);
    private CancellationTokenSource? _stopping;
    private PrayerSchedule? _schedule;
    private string _zone = string.Empty;
    private int _offset;
    private DateOnly? _loadedFor;
    private bool _loading;
    private IDisposable? _wake;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        ReadAppSettings();
        Context.App.Changed += OnAppSettingsChanged;
        Update();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.App.Changed -= OnAppSettingsChanged;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        _wake?.Dispose();
        _wake = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void ReadAppSettings()
    {
        var values = Context.App.Values;
        var zone = values.GetString(MalaysiaKeys.Zone);
        _zone = PrayerZones.IsValidCode(zone) ? zone : string.Empty;
        _offset = Math.Clamp(values.GetInt(MalaysiaKeys.HijriOffset), -HijriDate.MaxOffsetDays, HijriDate.MaxOffsetDays);
    }

    private void OnAppSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.Keys.Contains(MalaysiaKeys.Zone) && !e.Keys.Contains(MalaysiaKeys.HijriOffset))
            return;
        var zone = _zone;
        ReadAppSettings();
        if (zone != _zone)
        {
            _schedule = null;
            _loadedFor = null;
        }
        Update();
    }

    private void Update()
    {
        if (_stopping is null)
            return;
        var now = Context.Time.GetUtcNow();
        var today = MalaysiaTime.DateOf(now);
        if (_zone.Length > 0 && _loadedFor != today && !_loading)
            _ = LoadAsync(today, _stopping.Token);

        var official = _schedule?.DayOf(today)?.Hijri;
        _viewModel.Show(official ?? HijriDate.FromGregorian(today, _offset), today, official is not null);

        _wake?.Dispose();
        _wake = Context.Scheduler.At(MalaysiaTime.At(today.AddDays(1), TimeOnly.MinValue), OnWake, Exact);
    }

    private async Task LoadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        _loading = true;
        var zone = _zone;
        try
        {
            var schedule = await _loader.LoadAsync(zone, Context.Time.GetUtcNow(), cancellationToken);
            if (cancellationToken.IsCancellationRequested || zone != _zone)
                return;
            _schedule = schedule ?? _schedule;
            // Tried once today. A failure shows the calculated date until tomorrow's load.
            _loadedFor = today;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _loading = false;
        }
        Update();
    }

    private void OnWake()
    {
        _wake = null;
        Update();
    }
}

public sealed class HijriViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private HijriDate _date;
    private DateOnly _gregorian;
    private bool _isOfficial;

    public HijriViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>"14 Jamadilawal 1448".</summary>
    public string DateText => _date.IsValid ? _date.Format(_text.Language) : string.Empty;

    /// <summary>"Sunday, 25 October" or "Ahad, 25 Oktober".</summary>
    public string GregorianText => _gregorian.ToString("dddd, d MMMM", _text.Culture);

    /// <summary>Said when the date is calculated, since it can be a day off from the official one.</summary>
    public bool IsEstimate => !_isOfficial;

    public string AccessibleSummary => _text.Format("hijri.accessible", ("date", DateText), ("gregorian", GregorianText));

    public void Show(HijriDate date, DateOnly gregorian, bool isOfficial)
    {
        _date = date;
        _gregorian = gregorian;
        _isOfficial = isOfficial;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
