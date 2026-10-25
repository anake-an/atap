using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Placement;
using Atap.Core.Settings;
using Atap.Features.Language;
using Atap.Features.NotchSize;
using Atap.Notch;

namespace Atap.Features.Quick;

/// <summary>One choice in the quick panel, such as "Slim" for the notch size.</summary>
internal sealed record QuickOption(string Value, string Label);

/// <summary>
/// What the quick panel shows and changes: hiding the notch and pausing alerts for an hour, the notch size,
/// its position and the language. Each change takes effect at once, through the same settings and commands
/// as the rest of Atap. Made fresh each time the panel opens.
/// </summary>
internal sealed class QuickPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly ICommandRegistry _commands;
    private readonly ILayoutService _layout;
    private readonly ILocalizer _text;
    private readonly Func<bool> _alertsPaused;
    private readonly Action<bool> _pauseAlerts;

    public QuickPanelViewModel(
        IAppSettings settings, ICommandRegistry commands, ILayoutService layout, ILocalizer text, Func<bool> alertsPaused, Action<bool> pauseAlerts)
    {
        _settings = settings;
        _commands = commands;
        _layout = layout;
        _text = text;
        _alertsPaused = alertsPaused;
        _pauseAlerts = pauseAlerts;
        OpenSettingsCommand = Run(AppCommands.OpenSettings);
        QuitCommand = Run(AppCommands.Quit);
        StopSoundCommand = Run(Alerts.AlertsFeature.StopSoundCommand);
        _settings.Changed += OnSettingsChanged;
        _layout.Changed += OnLayoutChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>Raised after a command that leaves the panel, such as All settings, so the panel closes.</summary>
    public event EventHandler? Done;

    public ICommand OpenSettingsCommand { get; }

    public ICommand QuitCommand { get; }

    public ICommand StopSoundCommand { get; }

    /// <summary>On while Hide for 1 hour holds the notch hidden. Turning it off shows the notch again.</summary>
    public bool IsNotchHidden
    {
        get => IsChecked(NotchSizeFeature.HideForAnHourCommand);
        set
        {
            if (value == IsNotchHidden)
                return;
            _ = _commands.TryRunAsync(value ? NotchSizeFeature.HideForAnHourCommand : NotchSizeFeature.ShowNowCommand);
            OnPropertyChanged();
        }
    }

    /// <summary>On while alerts are paused for an hour. Critical alerts still show.</summary>
    public bool AreAlertsPaused
    {
        get => _alertsPaused();
        set
        {
            if (value == AreAlertsPaused)
                return;
            _pauseAlerts(value);
            OnPropertyChanged();
        }
    }

    /// <summary>True while an alert sound is playing, so Stop sound shows.</summary>
    public bool CanStopSound =>
        _commands.All.FirstOrDefault(c => c.Id == Alerts.AlertsFeature.StopSoundCommand)?.IsAvailable?.Invoke() ?? false;

    public IReadOnlyList<QuickOption> Sizes => Options(
        [NotchSizeDescriptor.Normal, NotchSizeDescriptor.Slim, NotchSizeDescriptor.Tucked], v => $"notchsize.size.{v}");

    public string Size
    {
        get => _settings.Values.GetString(NotchSizeDescriptor.SizeKey);
        set => Set(NotchSizeDescriptor.SizeKey, value);
    }

    public IReadOnlyList<QuickOption> Positions =>
        NotchMenuViewModel.Anchors.Select(a => new QuickOption(a.Anchor.ToString(), _text[a.Key])).ToList();

    public string Position
    {
        get => _layout.Document.Placement.Anchor.ToString();
        set
        {
            if (!Enum.TryParse<NotchAnchor>(value, out var anchor) || anchor == _layout.Document.Placement.Anchor)
                return;
            _layout.Update(d => d.Placement.Anchor = anchor);
        }
    }

    public IReadOnlyList<QuickOption> Languages => Options(
        [LanguageFeatureDescriptor.SameAsWindows, Localizer.English, Localizer.Malay], v => $"{LanguageFeatureDescriptor.ChoiceKey}.{v}");

    public string Language
    {
        get => _settings.Values.GetString(LanguageFeatureDescriptor.ChoiceKey);
        set => Set(LanguageFeatureDescriptor.ChoiceKey, value);
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _layout.Changed -= OnLayoutChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    private bool IsChecked(string id) => _commands.All.FirstOrDefault(c => c.Id == id)?.IsChecked?.Invoke() ?? false;

    private IReadOnlyList<QuickOption> Options(string[] values, Func<string, string> key) =>
        values.Select(v => new QuickOption(v, _text[key(v)])).ToList();

    private void Set(string key, string? value)
    {
        if (value is null || value == _settings.Values.GetString(key))
            return;
        _settings.Set(key, value);
    }

    private IRelayCommand Run(string id) => new RelayCommand(() =>
    {
        _ = _commands.TryRunAsync(id);
        if (id != Alerts.AlertsFeature.StopSoundCommand)
            Done?.Invoke(this, EventArgs.Empty);
        else
            OnPropertyChanged(nameof(CanStopSound));
    });

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(NotchSizeDescriptor.SizeKey))
            OnPropertyChanged(nameof(Size));
        if (e.Keys.Contains(LanguageFeatureDescriptor.ChoiceKey))
            OnPropertyChanged(nameof(Language));
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Position));

    // The panel stays open while the language changes, so every word on it follows.
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Sizes));
        OnPropertyChanged(nameof(Positions));
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(Language));
    }
}
