using System.Globalization;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Core.Scheduling;
using Atap.Core.Settings;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Widgets.Holidays;

[assembly: AtapWidget(typeof(HolidaysDescriptor))]

namespace Atap.Widgets.Holidays;

/// <summary>Public holidays from the bundled federal and state lists. Constants only.</summary>
public sealed class HolidaysDescriptor : IWidgetDescriptor
{
    public string TypeId => MalaysiaKeys.HolidaysTypeId;

    public string NameKey => "holidays.name";

    public string DescriptionKey => "holidays.description";

    public WidgetGroup Group => WidgetGroup.Malaysia;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    // Bundled data: nothing leaves the PC.
    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => true;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Holidays/HolidaysViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new HolidaysWidget(context, BundledHolidays.Calendar);
}

/// <summary>
/// The holiday years shipped in Widgets/Holidays/Data, embedded as "Atap.Data.holidays-&lt;year&gt;.json". Taken from
/// the Cabinet Division's gazetted tables. A file that does not parse is left out, and a test keeps every file valid.
/// </summary>
public static class BundledHolidays
{
    public const string ResourcePrefix = "Atap.Data.holidays-";

    private static readonly Lazy<HolidayCalendar> Loaded = new(() => new HolidayCalendar(Years(typeof(BundledHolidays).Assembly)));

    public static HolidayCalendar Calendar => Loaded.Value;

    /// <summary>Each embedded year file and how it parsed.</summary>
    public static IEnumerable<(string Name, ParseResult<HolidayYear> Result)> Files(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal)).Order())
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return (name, HolidayParser.Parse(reader.ReadToEnd()));
        }
    }

    private static IEnumerable<HolidayYear> Years(Assembly assembly) =>
        Files(assembly).Where(f => f.Result.Succeeded).Select(f => f.Result.Value!);
}

/// <summary>Reads the state from the Your area card and wakes once a day, at midnight in Malaysia.</summary>
public sealed class HolidaysWidget(WidgetContext context, HolidayCalendar calendar) : WidgetBase(context)
{
    private static readonly ScheduleOptions AtMidnight = new(TimeSpan.FromMinutes(1), false, MissedRun.RunOnce);

    private readonly HolidaysViewModel _viewModel = new(context.Text);
    private IDisposable? _wake;

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Context.App.Changed += OnAppSettingsChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.App.Changed -= OnAppSettingsChanged;
        _wake?.Dispose();
        _wake = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnAppSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(MalaysiaKeys.State))
            Refresh();
    }

    private void Refresh()
    {
        var today = MalaysiaTime.DateOf(Context.Time.GetUtcNow());
        var state = MalaysiaStates.Find(Context.App.Values.GetString(MalaysiaKeys.State));
        var next = calendar.Next(today, state?.Code);
        var upcoming = calendar.Upcoming(today, state?.Code, HolidaysViewModel.Shown);
        _viewModel.Show(today, state, next, upcoming);

        _wake?.Dispose();
        _wake = Context.Scheduler.At(MalaysiaTime.At(today.AddDays(1), TimeOnly.MinValue), Refresh, AtMidnight);
    }
}

/// <summary>"Hari Malaysia", "Wed, 16 Sep", "in 12 days", and whether the date may change.</summary>
public sealed record HolidayRow(string Name, string Date, string Until, bool Tentative);

public sealed class HolidaysViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    /// <summary>Holidays in the medium card.</summary>
    public const int Shown = 4;

    private readonly ILocalizer _text;
    private DateOnly _today;
    private MalaysiaState? _state;
    private HolidayLookup _next = new(HolidayLookupStatus.NoData, null);
    private IReadOnlyList<Holiday> _upcoming = [];

    public HolidaysViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public HolidayRow? Next { get; private set; }

    public IReadOnlyList<HolidayRow> Upcoming { get; private set; } = [];

    public bool HasNext => Next is not null;

    public bool HasMessage => Next is null;

    /// <summary>"Update Atap to see next year's holidays", when the bundled lists run out.</summary>
    public string Message => _text[_next.Status == HolidayLookupStatus.DataEnds ? "holidays.dataEnds" : "holidays.noData"];

    /// <summary>"For Selangor", or that only nationwide holidays show until a state is picked.</summary>
    public string Scope => _state is { } state
        ? _text.Format("holidays.forState", ("state", state.Name(_text.Language)))
        : _text["holidays.nationwideOnly"];

    public string AccessibleSummary => Next is { } next
        ? _text.Format("holidays.accessible", ("name", next.Name), ("date", next.Date), ("until", next.Until))
        : _text.Format("holidays.accessibleMessage", ("message", Message));

    public void Show(DateOnly today, MalaysiaState? state, HolidayLookup next, IReadOnlyList<Holiday> upcoming)
    {
        _today = today;
        _state = state;
        _next = next;
        _upcoming = upcoming;
        Build();
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void Build()
    {
        Next = _next.Holiday is { } holiday ? RowFor(holiday) : null;
        Upcoming = _upcoming.Select(RowFor).ToList();
        OnPropertyChanged(string.Empty);
    }

    private HolidayRow RowFor(Holiday holiday) => new(
        holiday.Name(_text.Language),
        holiday.Date.ToString("ddd, d MMM", _text.Culture),
        holiday.Date == _today ? _text["holidays.today"] : DurationText.DaysLeft(holiday.Date.DayNumber - _today.DayNumber, _text.Language),
        holiday.Tentative);

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
