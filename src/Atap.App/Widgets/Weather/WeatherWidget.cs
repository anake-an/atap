using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Weather;

[assembly: AtapWidget(typeof(WeatherDescriptor))]

namespace Atap.Widgets.Weather;

/// <summary>MET Malaysia's forecast for the state picked on the Prayer times page. Constants only.</summary>
public sealed class WeatherDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "weather";

    public string TypeId => TypeIdValue;

    public string NameKey => "weather.name";

    public string DescriptionKey => "weather.description";

    public WidgetGroup Group => WidgetGroup.Malaysia;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Network, [MalaysiaKeys.DataGovHost], "weather.privacy");

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Weather/WeatherViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new WeatherWidget(context);
}

/// <summary>
/// Fetches the state's 7-day forecast every 3 hours (stretched by battery saver) and keeps it on disk, so a
/// restart shows it without a request. Shown days follow Malaysian dates when the card opens.
/// </summary>
public sealed class WeatherWidget(WidgetContext context) : WidgetBase(context)
{
    private readonly WeatherViewModel _viewModel = new(context.Text);
    private CancellationTokenSource? _stopping;
    private MalaysiaState? _state;
    private WeatherForecast? _forecast;
    private bool _failed;
    private IDisposable? _refresh;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        _state = MalaysiaStates.Find(Context.App.Values.GetString(MalaysiaKeys.State));
        Context.App.Changed += OnAppSettingsChanged;
        Context.Visibility.Changed += OnVisibilityChanged;
        _refresh = Context.Scheduler.Every(MalaysiaKeys.ForecastMaxAge, () => _ = LoadAsync(),
            new ScheduleOptions(TimeSpan.FromMinutes(10), Deferrable: true));
        _ = LoadAsync();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.App.Changed -= OnAppSettingsChanged;
        Context.Visibility.Changed -= OnVisibilityChanged;
        _refresh?.Dispose();
        _refresh = null;
        _stopping?.Cancel();
        _stopping?.Dispose();
        _stopping = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnAppSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (!e.Keys.Contains(MalaysiaKeys.State))
            return;
        _state = MalaysiaStates.Find(Context.App.Values.GetString(MalaysiaKeys.State));
        _forecast = null;
        _ = LoadAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Show();

    private async Task LoadAsync()
    {
        if (_stopping is not { } stopping)
            return;
        if (_state is not { } state)
        {
            Show();
            return;
        }
        try
        {
            var result = await Context.Http.GetAsync(new FetchRequest(MalaysiaUrls.Forecast(state.ForecastLocationId), MalaysiaKeys.ForecastMaxAge), stopping.Token);
            if (stopping.IsCancellationRequested || state != _state)
                return;
            var parsed = result.Body.IsEmpty ? null : ForecastParser.Parse(Encoding.UTF8.GetString(result.Body.Span));
            if (parsed is { Succeeded: true, Value: { } forecast })
            {
                _forecast = forecast;
                _failed = false;
            }
            else
            {
                _failed = _forecast is null;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Show();
    }

    private void Show()
    {
        var today = MalaysiaTime.DateOf(Context.Time.GetUtcNow());
        var days = _state is { } state && _forecast is { } forecast
            ? forecast.ForLocation(state.ForecastLocationId).Where(d => d.Date >= today).ToList()
            : [];
        _viewModel.Show(_state, days, today, _failed);
    }
}

/// <summary>One day: "Thu", the condition, its icon and "24 to 33°".</summary>
public sealed record ForecastRow(string Day, string Condition, string IconKey, string Temperatures);

public sealed class WeatherViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    /// <summary>Days after today in the medium card.</summary>
    public const int MoreDays = 3;

    private readonly ILocalizer _text;
    private MalaysiaState? _state;
    private IReadOnlyList<ForecastDay> _days = [];
    private DateOnly _today;
    private bool _failed;

    public WeatherViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public bool HasForecast => _days.Count > 0;

    public bool HasMessage => !HasForecast;

    public string Message => _text[_state is null ? "weather.needsArea" : _failed ? "weather.failed" : "weather.loading"];

    /// <summary>"Selangor".</summary>
    public string Place => _state?.Name(_text.Language) ?? string.Empty;

    public ForecastRow? Today { get; private set; }

    /// <summary>"Thunderstorms in the afternoon", from the day's summary.</summary>
    public string TodayDetail { get; private set; } = string.Empty;

    public IReadOnlyList<ForecastRow> Next { get; private set; } = [];

    public string AccessibleSummary => Today is { } today
        ? _text.Format("weather.accessible", ("place", Place), ("condition", TodayDetail), ("temperatures", today.Temperatures))
        : _text.Format("weather.accessibleMessage", ("message", Message));

    public void Show(MalaysiaState? state, IReadOnlyList<ForecastDay> days, DateOnly today, bool failed)
    {
        _state = state;
        _days = days;
        _today = today;
        _failed = failed;
        Build();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        Today = _days.Count > 0 ? RowFor(_days[0]) : null;
        TodayDetail = _days.Count > 0 ? DetailOf(_days[0]) : string.Empty;
        Next = _days.Skip(1).Take(MoreDays).Select(RowFor).ToList();
        OnPropertyChanged(string.Empty);
    }

    private ForecastRow RowFor(ForecastDay day) => new(
        day.Date == _today ? _text["weather.today"] : day.Date.ToString("ddd", _text.Culture),
        ConditionOf(day.Summary),
        IconOf(day.Summary.Condition),
        day.MinTemperature is { } min && day.MaxTemperature is { } max
            ? _text.Format("weather.range", ("min", min.ToString(CultureInfo.CurrentCulture)), ("max", max.ToString(CultureInfo.CurrentCulture)))
            : string.Empty);

    /// <summary>"Thunderstorms", or MET Malaysia's own words for a phrase this build does not know.</summary>
    private string ConditionOf(ForecastPhrase phrase) => phrase.Condition == WeatherCondition.Unknown
        ? phrase.Text
        : _text[$"weather.condition.{phrase.Condition.ToString().ToLowerInvariant()}"];

    /// <summary>"Thunderstorms in some places, in the afternoon".</summary>
    private string DetailOf(ForecastDay day)
    {
        var parts = new List<string> { ConditionOf(day.Summary) };
        if (day.Summary.Condition != WeatherCondition.Unknown && day.Summary.Extent != WeatherExtent.None)
            parts[0] = _text.Format($"weather.extent.{day.Summary.Extent.ToString().ToLowerInvariant()}", ("condition", parts[0]));
        if (day.Summary.Area != WeatherArea.Everywhere)
            parts.Add(_text[$"weather.area.{day.Summary.Area.ToString().ToLowerInvariant()}"]);
        if (day.SummaryWhen is not (ForecastPeriods.None or ForecastPeriods.AllDay))
            parts.Add(_text[day.SummaryWhen switch
            {
                ForecastPeriods.Morning => "weather.when.morning",
                ForecastPeriods.Afternoon => "weather.when.afternoon",
                ForecastPeriods.Night => "weather.when.night",
                ForecastPeriods.Afternoon | ForecastPeriods.Night => "weather.when.afternoonNight",
                _ => "weather.when.morningAfternoon",
            }]);
        return string.Join(", ", parts);
    }

    private static string IconKey(string name) => "icon." + name;

    private static string IconOf(WeatherCondition condition) => IconKey(condition switch
    {
        WeatherCondition.Thunderstorm => "bolt",
        WeatherCondition.Rain => "droplet",
        WeatherCondition.Cloudy or WeatherCondition.Hazy or WeatherCondition.Windy => "cloud",
        WeatherCondition.NoRain or WeatherCondition.Fair => "sun",
        _ => "cloud",
    });

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}

/// <summary>Turns an "icon.&lt;name&gt;" key into the icon geometry from the app's resources.</summary>
public sealed class IconKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
