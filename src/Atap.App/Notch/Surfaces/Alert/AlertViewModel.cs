using System.Windows;
using System.Windows.Automation;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atap.Notch.Surfaces.Alerts;

/// <summary>
/// One alert on the notch: the Alert surface template binds to it through NotchViewModel.SurfaceContent.
/// A new one is made for each alert. The source already localized the title and subtitle.
/// </summary>
public sealed class AlertViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _text;
    private bool _canStop;

    /// <param name="shown">The alert and who sent it.</param>
    /// <param name="text">For the screen reader text and the level.</param>
    /// <param name="stop">Stops the sound and closes the alert.</param>
    /// <param name="close">Closes the alert.</param>
    internal AlertViewModel(ShownAlert shown, ILocalizer text, Action stop, Action close)
    {
        Shown = shown;
        _text = text;
        StopCommand = new RelayCommand(stop);
        CloseCommand = new RelayCommand(close);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public ShownAlert Shown { get; }

    public string Title => Shown.Alert.Title;

    public string? Subtitle => Shown.Alert.Subtitle;

    /// <summary>The subtitle line. A level bar takes its place.</summary>
    public bool HasSubtitle => !HasLevel && !string.IsNullOrWhiteSpace(Subtitle);

    public bool HasLevel => Shown.Alert.Level is not null;

    /// <summary>The level from 0 to 1.</summary>
    public double Level => Math.Clamp(Shown.Alert.Level ?? 0, 0, 1);

    public int LevelPercent => (int)Math.Round(Level * 100, MidpointRounding.AwayFromZero);

    /// <summary>The filled part of the level bar, as a star column width.</summary>
    public GridLength LevelFilled => new(Level, GridUnitType.Star);

    /// <summary>The empty part of the level bar.</summary>
    public GridLength LevelEmpty => new(1 - Level, GridUnitType.Star);

    /// <summary>For example "40%".</summary>
    public string LevelText => _text.Format("alerts.level", ("level", LevelPercent));

    public AlertTone Tone => Shown.Alert.Tone;

    /// <summary>A Segoe Fluent Icons glyph for the icon key, or for the tone.</summary>
    public string Glyph => AlertIcons.GlyphFor(Shown.Alert.IconKey, Tone);

    public bool IsCritical => Shown.Alert.Priority == AlertPriority.Critical;

    /// <summary>Critical alerts interrupt the screen reader. The others wait for a pause.</summary>
    public AutomationLiveSetting LiveSetting => IsCritical ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite;

    /// <summary>True while a sound plays. The Stop button shows.</summary>
    public bool CanStop
    {
        get => _canStop;
        set
        {
            if (SetProperty(ref _canStop, value))
                OnPropertyChanged(nameof(ShowClose));
        }
    }

    /// <summary>A Critical alert stays until it is closed, so it has a Close button while no sound plays.</summary>
    public bool ShowClose => IsCritical && !CanStop;

    public IRelayCommand StopCommand { get; }

    public IRelayCommand CloseCommand { get; }

    /// <summary>What a screen reader says, for example "Alert: Battery low. 15% left".</summary>
    public string AccessibleSummary
    {
        get
        {
            if (HasLevel)
                return _text.Format("alerts.accessibleLevel", ("title", Title), ("level", LevelText));
            if (!string.IsNullOrWhiteSpace(Subtitle))
                return _text.Format("alerts.accessibleDetail", ("title", Title), ("detail", Subtitle));
            return _text.Format("alerts.accessible", ("title", Title));
        }
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(AccessibleSummary));
    }
}
