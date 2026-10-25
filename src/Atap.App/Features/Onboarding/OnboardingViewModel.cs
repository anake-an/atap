using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Placement;
using Atap.Core.Presets;
using Atap.Core.Settings;
using Atap.Core.Sync;
using Atap.Core.Widgets;
using Atap.Features.Language;
using Atap.Features.Startup;
using Atap.Features.Sync;
using Atap.Settings.Cards.Malaysia.PrayerArea;

namespace Atap.Features.Onboarding;

public enum OnboardingStep
{
    Language,

    /// <summary>OneDrive sync. Only when OneDrive is on this PC.</summary>
    Sync,
    Position,
    Preset,
    Area,
    Startup,
    Done,
}

/// <summary>A choice in a segmented list, such as a language or a notch spot.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A preset as a card: its name, what it shows, and whether it goes online.</summary>
public sealed partial class PresetCard(LayoutPreset preset, string name, string description, string network, Action<PresetCard> pick) : ObservableObject
{
    private bool _isSelected;

    public LayoutPreset Preset => preset;

    public string Name => name;

    public string Description => description;

    /// <summary>"Works offline" or "Uses the internet".</summary>
    public string Network => network;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    [RelayCommand]
    private void Pick() => pick(this);
}

/// <summary>
/// The setup steps. Language and position apply at once, so the person sees the change. The preset, Start
/// with Windows and sync apply on Finish. Settings from OneDrive apply at once and take the place of the
/// preset. Skip keeps everything as it is. Either way setup never shows again.
/// </summary>
internal sealed partial class OnboardingViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly ILayoutService _layout;
    private readonly WidgetCatalog _catalog;
    private readonly ILocalizer _text;
    private readonly bool _startupSupported;
    private readonly SyncSetup? _sync;
    private readonly string? _syncFolder;
    private readonly bool _syncHasFile;
    private OnboardingStep _step;
    private bool _startWithWindows = true;
    private bool _keepInSync;
    private string? _syncBackup;
    private bool _syncFailed;

    /// <param name="settings">Language, Start with Windows, sync and done.</param>
    /// <param name="layout">Position and the preset.</param>
    /// <param name="catalog">The widgets the presets place.</param>
    /// <param name="text">UI text.</param>
    /// <param name="area">The Malaysia area step.</param>
    /// <param name="startupSupported">Start with Windows works in this build.</param>
    /// <param name="sync">Turns sync on, or null without OneDrive.</param>
    /// <param name="syncFolder">The sync folder in OneDrive, or null when OneDrive is not on this PC. The sync step shows only with one.</param>
    public OnboardingViewModel(
        IAppSettings settings,
        ILayoutService layout,
        WidgetCatalog catalog,
        ILocalizer text,
        PrayerAreaViewModel area,
        bool startupSupported,
        SyncSetup? sync,
        string? syncFolder)
    {
        _settings = settings;
        _layout = layout;
        _catalog = catalog;
        _text = text;
        _startupSupported = startupSupported;
        if (sync is not null && syncFolder is not null)
        {
            _sync = sync;
            _syncFolder = syncFolder;
            _syncHasFile = sync.NeedsChoice(syncFolder);
        }
        Area = area;
        Build();
        Presets[0].IsSelected = true;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? CloseRequested;

    public OnboardingStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
                OnPropertyChanged(string.Empty);
        }
    }

    public bool IsLanguage => _step == OnboardingStep.Language;

    public bool IsSync => _step == OnboardingStep.Sync;

    public bool IsPosition => _step == OnboardingStep.Position;

    public bool IsPreset => _step == OnboardingStep.Preset;

    public bool IsArea => _step == OnboardingStep.Area;

    public bool IsStartup => _step == OnboardingStep.Startup;

    public bool IsDone => _step == OnboardingStep.Done;

    public bool IsNotDone => !IsDone;

    /// <summary>Next shows on every step but the last, and not while the sync step asks about OneDrive's settings.</summary>
    public bool ShowNext => !IsDone && !(IsSync && SyncAsking);

    public bool CanGoBack => _step != OnboardingStep.Language && !IsDone;

    /// <summary>"Step 2 of 5". The Area step counts only with the Malaysia preset.</summary>
    public string StepText
    {
        get
        {
            var steps = Steps();
            return _text.Format("onboarding.stepOf", ("number", (steps.IndexOf(_step) + 1).ToString(_text.Culture)), ("count", steps.Count.ToString(_text.Culture)));
        }
    }

    public IReadOnlyList<Choice<string>> Languages { get; private set; } = [];

    public Choice<string>? SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.Value == _settings.Values.GetString(AppServices.LanguageChoiceKey)) ?? Languages[0];
        set
        {
            if (value is not null)
                _settings.Set(AppServices.LanguageChoiceKey, value.Value);
        }
    }

    public IReadOnlyList<Choice<NotchAnchor>> Anchors { get; private set; } = [];

    public Choice<NotchAnchor>? SelectedAnchor
    {
        get => Anchors.FirstOrDefault(a => a.Value == _layout.Document.Placement.Anchor);
        set
        {
            if (value is null || value.Value == _layout.Document.Placement.Anchor)
                return;
            _layout.Update(d =>
            {
                d.Placement.Anchor = value.Value;
                d.Placement.Offset = 0;
            });
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<PresetCard> Presets { get; private set; } = [];

    public LayoutPreset Preset => Presets.FirstOrDefault(p => p.IsSelected)?.Preset ?? LayoutPreset.Minimal;

    public PrayerAreaViewModel Area { get; }

    public bool ShowStartup => _startupSupported;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    /// <summary>OneDrive already has settings from another PC.</summary>
    public bool SyncHasFile => _syncHasFile;

    /// <summary>OneDrive has no settings yet, so the step offers to keep them in sync.</summary>
    public bool SyncOffer => !_syncHasFile;

    /// <summary>"Use my settings from OneDrive" and "Not now" show until the settings from OneDrive are in use.</summary>
    public bool SyncAsking => _syncHasFile && !SyncUsed;

    /// <summary>The settings from OneDrive are in use, and sync is on.</summary>
    public bool SyncUsed => _syncBackup is not null;

    /// <summary>Off by default. Turns sync on at Finish.</summary>
    public bool KeepInSync
    {
        get => _keepInSync;
        set => SetProperty(ref _keepInSync, value);
    }

    public string? SyncMessage => _syncBackup is { } backup
        ? _text.Format("onboarding.sync.used", ("folder", backup))
        : _syncFailed ? _text["onboarding.sync.failed"] : null;

    public bool HasSyncMessage => SyncMessage is not null;

    public void Dispose()
    {
        _text.LanguageChanged -= OnLanguageChanged;
        Area.Dispose();
    }

    [RelayCommand]
    private void Next()
    {
        var steps = Steps();
        var index = steps.IndexOf(_step);
        if (index < steps.Count - 1)
            Step = steps[index + 1];
    }

    [RelayCommand]
    private void Back()
    {
        var steps = Steps();
        var index = steps.IndexOf(_step);
        if (index > 0)
            Step = steps[index - 1];
    }

    /// <summary>Keeps the layout as it is and never shows setup again.</summary>
    [RelayCommand]
    private void Skip()
    {
        _settings.Set(OnboardingDescriptor.DoneKey, true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saves a copy of this PC's settings and layout, then takes OneDrive's and turns sync on, as the Sync card
    /// does. When the copy cannot be saved nothing changes, and the step says so.
    /// </summary>
    [RelayCommand]
    private void UseOneDriveSettings()
    {
        if (_sync is null || _syncFolder is null || SyncUsed)
            return;
        _syncBackup = _sync.Start(_syncFolder, SyncStartChoice.UseOneDrive);
        _syncFailed = _syncBackup is null;
        if (_syncBackup is not null)
            _settings.Set(SyncDescriptor.EnabledKey, true);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Leaves sync off and goes on.</summary>
    [RelayCommand]
    private void NotNow() => Next();

    [RelayCommand]
    private void Finish()
    {
        // The layout from OneDrive takes the place of the preset.
        if (!SyncUsed)
        {
            var preset = Preset;
            _layout.Update(d => LayoutPresets.Apply(d, preset, _catalog));
        }
        if (_startupSupported)
            _settings.Set(StartupDescriptor.EnabledKey, StartWithWindows);
        if (_sync is not null && SyncOffer && KeepInSync)
            _settings.Set(SyncDescriptor.EnabledKey, true);
        _settings.Set(OnboardingDescriptor.DoneKey, true);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private List<OnboardingStep> Steps()
    {
        var steps = new List<OnboardingStep> { OnboardingStep.Language };
        if (_sync is not null)
            steps.Add(OnboardingStep.Sync);
        steps.Add(OnboardingStep.Position);
        // Settings from OneDrive bring their own widgets and area.
        if (!SyncUsed)
            steps.Add(OnboardingStep.Preset);
        if (!SyncUsed && Preset == LayoutPreset.Malaysia)
            steps.Add(OnboardingStep.Area);
        if (_startupSupported)
            steps.Add(OnboardingStep.Startup);
        steps.Add(OnboardingStep.Done);
        return steps;
    }

    private void PickPreset(PresetCard card)
    {
        foreach (var preset in Presets)
            preset.IsSelected = ReferenceEquals(preset, card);
        OnPropertyChanged(nameof(StepText));
    }

    private void Build()
    {
        var selected = Presets.FirstOrDefault(p => p.IsSelected)?.Preset;
        Languages =
        [
            new(LanguageFeatureDescriptor.SameAsWindows, _text["onboarding.language.windows"]),
            new("en", "English"),
            new("ms", "Bahasa Melayu"),
        ];
        Anchors =
        [
            new(NotchAnchor.TopCenter, _text["notch.anchor.topCenter"]),
            new(NotchAnchor.TopLeft, _text["notch.anchor.topLeft"]),
            new(NotchAnchor.TopRight, _text["notch.anchor.topRight"]),
            new(NotchAnchor.LeftCenter, _text["notch.anchor.leftCenter"]),
            new(NotchAnchor.RightCenter, _text["notch.anchor.rightCenter"]),
        ];
        Presets = LayoutPresets.All.Select(p =>
        {
            var key = p.ToString().ToLowerInvariant();
            return new PresetCard(p, _text[$"onboarding.preset.{key}"], _text[$"onboarding.preset.{key}Hint"],
                _text[LayoutPresets.UsesInternet(p) ? "onboarding.online" : "onboarding.offline"], PickPreset)
            {
                IsSelected = p == selected,
            };
        }).ToList();
        OnPropertyChanged(string.Empty);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Build();
}
