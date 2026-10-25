using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Localization;
using Atap.Notch.Automation;

namespace Atap.Widgets.Clock;

public sealed class ClockViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private string _time = string.Empty;

    public ClockViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public string Time
    {
        get => _time;
        set
        {
            if (!SetProperty(ref _time, value))
                return;
            OnPropertyChanged(nameof(AccessibleSummary));
            OnPropertyChanged(nameof(Hours));
            OnPropertyChanged(nameof(Minutes));
        }
    }

    /// <summary>"19" from "19:05", for the stacked clock on a side edge. Splits on the culture's time separator.</summary>
    public string Hours => Parts[0];

    /// <summary>"05" from "19:05". Empty when the time has no separator.</summary>
    public string Minutes => Parts is [_, var minutes] ? minutes : string.Empty;

    private string[] Parts => Time.Split(CultureInfo.CurrentCulture.DateTimeFormat.TimeSeparator, 2);

    /// <summary>What a screen reader says, for example "Clock, 14:05".</summary>
    public string AccessibleSummary => _text.Format("clock.accessible", ("time", Time));

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(AccessibleSummary));
}
