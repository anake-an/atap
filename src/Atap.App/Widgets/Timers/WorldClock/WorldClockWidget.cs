using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Timers.WorldClock;
using Clocks = Atap.Core.Productivity.Timers.WorldClock;

[assembly: AtapWidget(typeof(WorldClockDescriptor))]

namespace Atap.Widgets.Timers.WorldClock;

/// <summary>The time in up to three places. Created at startup whether it is enabled or not, so it holds constants only.</summary>
public sealed class WorldClockDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "world-clock";
    public static readonly string[] CityKeys = ["city1", "city2", "city3"];

    public string TypeId => TypeIdValue;

    public string NameKey => "worldclock.name";

    public string DescriptionKey => "worldclock.description";

    public WidgetGroup Group => WidgetGroup.Productivity;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    // Windows zone ids, as the time zone picker stores them. Riyadh is also Makkah's time.
    public IReadOnlyList<SettingDefinition> Settings { get; } =
    [
        new(CityKeys[0], "worldclock.city1", SettingType.TimeZone, "GMT Standard Time"),
        new(CityKeys[1], "worldclock.city2", SettingType.TimeZone, "Tokyo Standard Time"),
        new(CityKeys[2], "worldclock.city3", SettingType.TimeZone, "Arab Standard Time"),
    ];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Timers/WorldClock/WorldClockViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new WorldClockWidget(context);
}

/// <summary>Reads the clocks on the shared minute tick, and only while the widget is open.</summary>
public sealed class WorldClockWidget(WidgetContext context) : WidgetBase(context)
{
    private readonly WorldClockViewModel _viewModel = new(context.Text);
    private IReadOnlyList<(string Id, TimeZoneInfo Zone)> _zones = [];
    private IDisposable? _tick;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _zones = WorldClockDescriptor.CityKeys
            .Select(settings.GetString)
            .Select(id => Clocks.TryFindZone(id, out var zone) ? (id, zone) : default)
            .Where(z => z.zone is not null)
            .ToList()!;
        Context.Visibility.Changed += OnVisibilityChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _tick?.Dispose();
        _tick = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var now = Context.Time.GetUtcNow();
        var home = Context.Time.LocalTimeZone;
        _viewModel.Show(_zones.Select(z => (Clocks.CityOf(z.Id), Clocks.Read(z.Zone, home, now))).ToList());
        var shown = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (shown && _tick is null)
            _tick = Context.Scheduler.EveryMinute(Refresh);
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }
}

/// <summary>"Tokyo", "6:05 PM", "Tomorrow", "+1 h".</summary>
public sealed record CityRow(string City, string Time, string Day, string Offset)
{
    public bool HasDay => Day.Length > 0;
}

public sealed class WorldClockViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private IReadOnlyList<(string City, Core.Productivity.Timers.WorldClockReading Reading)> _readings = [];

    public WorldClockViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<CityRow> Rows { get; private set; } = [];

    public CityRow? First => Rows.Count > 0 ? Rows[0] : null;

    public bool HasCities => Rows.Count > 0;

    public bool HasNoCities => Rows.Count == 0;

    public string AccessibleSummary => HasCities
        ? _text.Format("worldclock.accessible", ("clocks", string.Join(". ", Rows.Select(r => $"{r.City} {r.Time} {r.Day}".TrimEnd()))))
        : _text["worldclock.none"];

    public void Show(IReadOnlyList<(string City, Core.Productivity.Timers.WorldClockReading Reading)> readings)
    {
        _readings = readings;
        Rows = readings.Select(r => new CityRow(
            r.City,
            r.Reading.LocalTime.ToString("t", CultureInfo.CurrentCulture),
            r.Reading.DayOffset switch
            {
                > 0 => _text["worldclock.tomorrow"],
                < 0 => _text["worldclock.yesterday"],
                _ => string.Empty,
            },
            OffsetText(r.Reading.OffsetFromHome))).ToList();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    /// <summary>"+1 h", "-7 h 30 min", or "Same time".</summary>
    private string OffsetText(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
            return _text["worldclock.sameTime"];
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        var hours = (int)abs.TotalHours;
        return abs.Minutes == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{hours} {_text["worldclock.hours"]}")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{hours} {_text["worldclock.hours"]} {abs.Minutes} min");
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_readings);
}
