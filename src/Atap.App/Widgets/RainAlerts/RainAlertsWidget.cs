using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Core.Net;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.RainAlerts;

[assembly: AtapWidget(typeof(RainAlertsDescriptor))]

namespace Atap.Widgets.RainAlerts;

/// <summary>MET Malaysia thunderstorm and rain warnings for the picked state. Constants only.</summary>
public sealed class RainAlertsDescriptor : IWidgetDescriptor
{
    public string TypeId => MalaysiaKeys.RainAlertsTypeId;

    public string NameKey => "rain.name";

    public string DescriptionKey => "rain.description";

    public WidgetGroup Group => WidgetGroup.Malaysia;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities { get; } = new(WidgetPermissions.Network, [MalaysiaKeys.DataGovHost], "rain.privacy");

    public bool AllowsMultipleInstances => false;

    // Warnings arrive even when the card is not on any page.
    public bool WorksWithoutPlacement => true;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/RainAlerts/RainAlertsViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new RainAlertsWidget(context);
}

/// <summary>Ids of warnings that already gave an alert, so a restart never repeats one.</summary>
[JsonSerializable(typeof(List<string>))]
internal sealed partial class RainAlertsJson : JsonSerializerContext;

/// <summary>
/// Fetches every current warning each 20 minutes (stretched by battery saver) and filters by state on the PC,
/// so the state never leaves it. Each new land warning for the state gives one alert, ever.
/// </summary>
public sealed class RainAlertsWidget(WidgetContext context) : WidgetBase(context)
{
    public const string AlertedFile = "alerted.json";

    /// <summary>Ids kept, newest last. Old ones fall off.</summary>
    public const int MaxRemembered = 100;

    private readonly RainAlertsViewModel _viewModel = new(context.Text);
    private CancellationTokenSource? _stopping;
    private List<string> _alerted = [];
    private WarningList? _warnings;
    private bool _failed;
    private IDisposable? _refresh;

    public override object ViewModel => _viewModel;

    private string State => Context.App.Values.GetString(MalaysiaKeys.State);

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _stopping = new CancellationTokenSource();
        _alerted = Context.Storage.Load(AlertedFile, RainAlertsJson.Default.ListString, [], DataScope.Machine);
        Context.App.Changed += OnAppSettingsChanged;
        Context.Visibility.Changed += OnVisibilityChanged;
        _refresh = Context.Scheduler.Every(MalaysiaKeys.WarningsMaxAge, () => _ = LoadAsync(),
            new ScheduleOptions(TimeSpan.FromMinutes(2), Deferrable: true));
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
        // A state picked after start has no warnings yet, so they are fetched now. The HTTP cache answers when they are.
        if (e.Keys.Contains(MalaysiaKeys.State))
            _ = LoadAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e) => Update();

    private async Task LoadAsync()
    {
        if (_stopping is not { } stopping || MalaysiaStates.Find(State) is null)
        {
            Update();
            return;
        }
        try
        {
            var result = await Context.Http.GetAsync(new FetchRequest(MalaysiaUrls.Warnings, MalaysiaKeys.WarningsMaxAge), stopping.Token);
            if (stopping.IsCancellationRequested)
                return;
            var parsed = result.Body.IsEmpty ? null : WarningParser.Parse(Encoding.UTF8.GetString(result.Body.Span));
            if (parsed is { Succeeded: true, Value: { } warnings })
            {
                _warnings = warnings;
                _failed = false;
            }
            else
            {
                _failed = _warnings is null;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Update();
    }

    private void Update()
    {
        var state = MalaysiaStates.Find(State);
        var now = Context.Time.GetUtcNow();
        var active = state is null || _warnings is null
            ? []
            : _warnings.Where(w => w.IsLandWarning && w.IsActive(now) && w.AffectsState(state.Code)).OrderByDescending(w => w.Issued).ToList();

        var fresh = active.Where(w => !_alerted.Contains(w.Id)).ToList();
        foreach (var warning in fresh)
        {
            _alerted.Add(warning.Id);
            Context.Alerts.Show(_viewModel.AlertFor(warning));
        }
        if (fresh.Count > 0)
        {
            if (_alerted.Count > MaxRemembered)
                _alerted.RemoveRange(0, _alerted.Count - MaxRemembered);
            Context.Storage.Save(AlertedFile, _alerted, RainAlertsJson.Default.ListString, DataScope.Machine);
        }
        _viewModel.Show(state, active, _warnings is not null, _failed);
    }
}

/// <summary>One warning in the card: its heading, what it says, and until when.</summary>
public sealed record WarningRow(string Title, string Text, string Until);

public sealed class RainAlertsViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private MalaysiaState? _state;
    private IReadOnlyList<WeatherWarning> _active = [];
    private bool _loaded;
    private bool _failed;

    public RainAlertsViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<WarningRow> Warnings { get; private set; } = [];

    public bool HasWarnings => Warnings.Count > 0;

    public bool HasMessage => !HasWarnings;

    /// <summary>"No warnings for Selangor", or why there is nothing to show.</summary>
    public string Message =>
        _state is null ? _text["rain.needsArea"]
        : _failed ? _text["rain.failed"]
        : !_loaded ? _text["rain.loading"]
        : _text.Format("rain.none", ("state", _state.Name(_text.Language)));

    public string AccessibleSummary => HasWarnings
        ? _text.Format("rain.accessible", ("count", Warnings.Count.ToString(CultureInfo.CurrentCulture)), ("first", Warnings[0].Title))
        : _text.Format("rain.accessibleMessage", ("message", Message));

    public void Show(MalaysiaState? state, IReadOnlyList<WeatherWarning> active, bool loaded, bool failed)
    {
        _state = state;
        _active = active;
        _loaded = loaded;
        _failed = failed;
        Build();
    }

    /// <summary>The warning's own title and heading, in the UI language, as MET Malaysia wrote them.</summary>
    public Alert AlertFor(WeatherWarning warning) =>
        new("rain." + warning.Id, AlertPriority.High, warning.Title(_text.Language))
        {
            Subtitle = warning.Heading(_text.Language),
            IconKey = "warning",
            Tone = AlertTone.Warning,
        };

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        Warnings = _active.Select(w => new WarningRow(
            w.Title(_text.Language),
            w.Text(_text.Language),
            w.ValidTo is { } to ? _text.Format("rain.until", ("time", to.ToOffset(MalaysiaTime.Offset).ToString("g", CultureInfo.CurrentCulture))) : string.Empty))
            .ToList();
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
