using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Malaysia;
using Atap.Core.Settings;
using Atap.Platform.Location;
using Atap.Settings.Cards.Malaysia.PrayerArea;

[assembly: AtapSettingsCard(typeof(PrayerAreaCard))]

namespace Atap.Settings.Cards.Malaysia.PrayerArea;

/// <summary>Which JAKIM zone the prayer times are for: a state, then an area, or Find my area.</summary>
internal sealed class PrayerAreaCard : ISettingsCard
{
    public string Id => "prayerArea";

    public string SectionId => SettingsSections.Malaysia;

    public int Order => 10;

    public string TitleKey => "malaysia.area.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Malaysia/PrayerArea/PrayerAreaViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new PrayerAreaViewModel(context.Services.Settings, context.Services.Text, new WindowsLocation());
}

internal sealed record StateOption(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <param name="Label">The districts, for example "Gombak, Petaling, Sepang".</param>
internal sealed record ZoneOption(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Writes <see cref="MalaysiaKeys.State"/> and <see cref="MalaysiaKeys.Zone"/>. Picking a state picks its first
/// area, so times show at once. Find my area only suggests: the person presses Use this area to switch.
/// </summary>
internal sealed class PrayerAreaViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly ILocalizer _text;
    private readonly ILocationSource _location;
    private IReadOnlyList<StateOption> _states = [];
    private IReadOnlyList<ZoneOption> _zones = [];
    private ZoneMatch? _suggestion;
    private string? _findKey;
    private bool _isFinding;

    public PrayerAreaViewModel(IAppSettings settings, ILocalizer text, ILocationSource location)
    {
        _settings = settings;
        _text = text;
        _location = location;
        FindCommand = new AsyncRelayCommand(FindAsync, () => !IsFinding);
        UseSuggestionCommand = new RelayCommand(UseSuggestion);
        OpenLocationSettingsCommand = new RelayCommand(_location.OpenWindowsSettings);
        Rebuild();
        _settings.Changed += OnSettingsChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<StateOption> States
    {
        get => _states;
        private set => SetProperty(ref _states, value);
    }

    public StateOption? SelectedState
    {
        get => _states.FirstOrDefault(s => s.Code == StateCode);
        set
        {
            if (value is null || value.Code == StateCode)
                return;
            var zone = PrayerZones.ForState(value.Code).FirstOrDefault(z => !z.IsSpecial);
            Save(value.Code, zone?.Code ?? string.Empty);
        }
    }

    public IReadOnlyList<ZoneOption> Zones
    {
        get => _zones;
        private set => SetProperty(ref _zones, value);
    }

    public ZoneOption? SelectedZone
    {
        get => _zones.FirstOrDefault(z => z.Code == ZoneCode);
        set
        {
            if (value is null || value.Code == ZoneCode)
                return;
            Save(StateCode, value.Code);
        }
    }

    public bool HasState => StateCode.Length > 0;

    /// <summary>"Times for Gombak, Petaling, Sepang (SGR01)." or how to start.</summary>
    public string Summary => PrayerZones.Find(ZoneCode) is { } zone
        ? _text.Format("malaysia.area.summary", ("areas", zone.AreasText), ("zone", zone.Code))
        : _text["malaysia.area.none"];

    public IAsyncRelayCommand FindCommand { get; }

    public IRelayCommand UseSuggestionCommand { get; }

    public IRelayCommand OpenLocationSettingsCommand { get; }

    public bool IsFinding
    {
        get => _isFinding;
        private set
        {
            if (SetProperty(ref _isFinding, value))
                FindCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>What Find my area found, or why it found nothing.</summary>
    public string? FindStatus => _suggestion is { Zone: var zone }
        ? _text.Format("malaysia.area.found", ("areas", zone.AreasText), ("zone", zone.Code))
        : _findKey is null ? null : _text[_findKey];

    public bool HasFindStatus => FindStatus is not null;

    public bool HasSuggestion => _suggestion is not null;

    public bool LocationIsOff => _findKey == "malaysia.area.locationOff";

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    private string StateCode => _settings.Values.GetString(MalaysiaKeys.State);

    private string ZoneCode => _settings.Values.GetString(MalaysiaKeys.Zone);

    private async Task FindAsync()
    {
        IsFinding = true;
        ShowFind(null, "malaysia.area.finding");
        try
        {
            var fix = await _location.FindAsync();
            if (fix.Outcome == LocationOutcome.Off)
                ShowFind(null, "malaysia.area.locationOff");
            else if (fix.Point is not { } point || ZoneLocator.FindNearest(point) is not { } match)
                ShowFind(null, "malaysia.area.notFound");
            else if (match.IsAbroad)
                ShowFind(null, "malaysia.area.abroad");
            else if (match.Zone.Code == ZoneCode)
                ShowFind(null, "malaysia.area.alreadySet");
            else
                ShowFind(match, null);
        }
        finally
        {
            IsFinding = false;
        }
    }

    private void UseSuggestion()
    {
        if (_suggestion is not { Zone: var zone })
            return;
        var state = zone.StateCodes.Contains(StateCode) ? StateCode : zone.StateCodes[0];
        Save(state, zone.Code);
        ShowFind(null, null);
    }

    private void ShowFind(ZoneMatch? suggestion, string? key)
    {
        _suggestion = suggestion;
        _findKey = key;
        OnPropertyChanged(nameof(FindStatus));
        OnPropertyChanged(nameof(HasFindStatus));
        OnPropertyChanged(nameof(HasSuggestion));
        OnPropertyChanged(nameof(LocationIsOff));
    }

    private void Save(string state, string zone)
    {
        _settings.Set(MalaysiaKeys.State, state);
        _settings.Set(MalaysiaKeys.Zone, zone);
        Refresh();
    }

    private void Rebuild()
    {
        States = MalaysiaStates.All.Select(s => new StateOption(s.Code, s.Name(_text.Language))).ToList();
        Refresh();
    }

    private void Refresh()
    {
        Zones = PrayerZones.ForState(StateCode).Select(z => new ZoneOption(z.Code, z.AreasText)).ToList();
        OnPropertyChanged(nameof(SelectedState));
        OnPropertyChanged(nameof(SelectedZone));
        OnPropertyChanged(nameof(HasState));
        OnPropertyChanged(nameof(Summary));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(MalaysiaKeys.State) || e.Keys.Contains(MalaysiaKeys.Zone))
            Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Rebuild();
        OnPropertyChanged(nameof(FindStatus));
    }
}
