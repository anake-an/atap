using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Malaysia;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Widgets.WaktuSolat;

[assembly: AtapWidget(typeof(WaktuSolatDescriptor))]

namespace Atap.Widgets.WaktuSolat;

/// <summary>
/// Prayer times for the zone picked on the Prayer times page. Created at startup whether it is enabled or not,
/// so it holds constants only.
/// </summary>
public sealed class WaktuSolatDescriptor : IWidgetDescriptor
{
    public const string LiveBeforeKey = "liveBefore";
    public const string ReminderKey = "reminder";
    public const string RamadanKey = "ramadan";
    public const string RamadanAuto = "auto";
    public const string RamadanOn = "on";
    public const string RamadanOff = "off";

    public string TypeId => MalaysiaKeys.WaktuSolatTypeId;

    public string NameKey => "solat.name";

    public string DescriptionKey => "solat.description";

    public WidgetGroup Group => WidgetGroup.Malaysia;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(LiveBeforeKey, "solat.liveBefore", SettingType.Int, 10)
        {
            DescriptionKey = "solat.liveBeforeHint",
            Min = 0,
            Max = 30,
            Step = 5,
            UnitKey = "solat.minutes",
        },
        new(ReminderKey, "solat.reminder", SettingType.Bool, true) { DescriptionKey = "solat.reminderHint" },
        new(RamadanKey, "solat.ramadan", SettingType.Choice, RamadanAuto, [RamadanAuto, RamadanOn, RamadanOff])
        {
            DescriptionKey = "solat.ramadanHint",
        },
    ];

    public WidgetCapabilities Capabilities { get; } =
        new(WidgetPermissions.Network, [MalaysiaKeys.EsolatHost, MalaysiaKeys.MirrorHost], "solat.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/WaktuSolat/WaktuSolatViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new WaktuSolatWidget(context);
}

/// <summary>
/// Wakes once for each change <see cref="SolatClock.NextChange"/> names: a live window opening, a prayer, the end
/// of its first five minutes, midnight. The minute tick runs only while the widget is on screen, for the countdown.
/// Reminders fire at the prayer itself, never from out-of-date data, and once each.
/// </summary>
public sealed class WaktuSolatWidget(WidgetContext context) : WidgetBase(context)
{
    /// <summary>A reminder still shows this long after the time, for a wake that runs a little late.</summary>
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(2);

    /// <summary>After a failed load, try again this much later. The HTTP client's backoff applies too.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    // Prayer times are wall-clock appointments: no slack, never stretched by battery saver, and run on resume.
    private static readonly ScheduleOptions Exact = new(TimeSpan.Zero, false, MissedRun.RunOnce);

    private readonly WaktuSolatViewModel _viewModel = new(context.Text);
    private readonly PrayerTimesLoader _loader = new(context.Http);
    private CancellationTokenSource? _stopping;
    private PrayerSchedule? _schedule;
    private string _zone = string.Empty;
    private int _hijriOffset;
    private TimeSpan _liveBefore;
    private bool _remind;
    private RamadanMode _ramadan;
    private DateOnly? _loadedFor;
    private bool _loading;
    private IDisposable? _wake;
    private DateTimeOffset _wakeAt;
    private IDisposable? _retry;
    private IDisposable? _tick;
    private (Prayer Prayer, DateOnly Date)? _reminded;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _liveBefore = TimeSpan.FromMinutes(Math.Clamp(settings.GetInt(WaktuSolatDescriptor.LiveBeforeKey), 0, 30));
        _remind = settings.GetBool(WaktuSolatDescriptor.ReminderKey);
        _ramadan = settings.GetString(WaktuSolatDescriptor.RamadanKey) switch
        {
            WaktuSolatDescriptor.RamadanOn => RamadanMode.On,
            WaktuSolatDescriptor.RamadanOff => RamadanMode.Off,
            _ => RamadanMode.Auto,
        };
        _stopping = new CancellationTokenSource();
        ReadAppSettings();
        Context.App.Changed += OnAppSettingsChanged;
        Context.Visibility.Changed += OnVisibilityChanged;
        Update();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.App.Changed -= OnAppSettingsChanged;
        Context.Visibility.Changed -= OnVisibilityChanged;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        Disarm();
        _retry?.Dispose();
        _retry = null;
        _tick?.Dispose();
        _tick = null;
        SetIsLive(false);
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
        _hijriOffset = Math.Clamp(values.GetInt(MalaysiaKeys.HijriOffset), -HijriDate.MaxOffsetDays, HijriDate.MaxOffsetDays);
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
            _retry?.Dispose();
            _retry = null;
        }
        Update();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        if (_stopping is null)
            return;
        var now = Context.Time.GetUtcNow();
        var today = MalaysiaTime.DateOf(now);
        if (_zone.Length > 0 && _loadedFor != today && !_loading && _retry is null)
            _ = LoadAsync(today, _stopping.Token);

        var state = SolatClock.At(_schedule, now, _liveBefore, _ramadan, _hijriOffset);
        Remind(state, now);
        _viewModel.Show(state, now, PrayerZones.Find(_zone), StatusOf(state));
        SetIsLive(state.IsLive);
        Arm(SolatClock.NextChange(_schedule, now, _liveBefore));
        Tick(Context.Visibility.Value is WidgetVisibility.Compact or WidgetVisibility.Expanded or WidgetVisibility.Panel);
    }

    private SolatStatus StatusOf(SolatNow state) =>
        _zone.Length == 0 ? SolatStatus.NeedsArea
        : !state.IsStale ? SolatStatus.Ready
        : _loading || _loadedFor is null && _retry is null ? SolatStatus.Loading
        : SolatStatus.Stale;

    private async Task LoadAsync(DateOnly today, CancellationToken cancellationToken)
    {
        _loading = true;
        var zone = _zone;
        try
        {
            var schedule = await _loader.LoadAsync(zone, Context.Time.GetUtcNow(), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;
            // Times for an area changed meanwhile are dropped. The Update below then loads the new area.
            if (zone == _zone)
            {
                if (schedule is not null)
                    _schedule = _schedule is { } old && old.Zone == schedule.Zone ? old.Merge(schedule).DropBefore(today.AddDays(-1)) : schedule;
                if (schedule?.DayOf(today) is not null)
                    _loadedFor = today;
                else
                    _retry = Context.Scheduler.At(Context.Time.GetUtcNow() + RetryAfter, Retry, Exact);
            }
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

    private void Retry()
    {
        _retry?.Dispose();
        _retry = null;
        Update();
    }

    private void Remind(SolatNow state, DateTimeOffset now)
    {
        if (!_remind || state.IsStale || state.Today is not { } today)
            return;
        PrayerTime? due = state.Current is { } current && now - current.Time < ReminderWindow ? current : null;
        if (state.IsRamadan && today.Imsak <= now && now - today.Imsak < ReminderWindow)
            due = new PrayerTime(Prayer.Imsak, today.Imsak, today.Date);
        if (due is not { } prayer || _reminded == (prayer.Prayer, prayer.Date))
            return;
        _reminded = (prayer.Prayer, prayer.Date);
        Context.Alerts.Show(_viewModel.ReminderFor(prayer, state.IsRamadan, PrayerZones.Find(_zone)));
    }

    private void Arm(DateTimeOffset due)
    {
        if (_wake is not null && due == _wakeAt)
            return;
        Disarm();
        _wakeAt = due;
        _wake = Context.Scheduler.At(due, OnWake, Exact);
    }

    private void Disarm()
    {
        _wake?.Dispose();
        _wake = null;
    }

    private void OnWake()
    {
        _wake = null;
        Update();
    }

    private void Tick(bool shown)
    {
        if (shown && _tick is null)
            _tick = Context.Scheduler.EveryMinute(Update);
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}
