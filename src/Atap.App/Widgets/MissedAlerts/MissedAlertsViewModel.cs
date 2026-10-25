using System.Globalization;
using Atap.Core.Alerts;
using Atap.Core.Localization;
using Atap.Notch.Automation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atap.Widgets.MissedAlerts;

/// <param name="Title">As the source sent it, already localized.</param>
/// <param name="Subtitle">The detail line, if any.</param>
/// <param name="Time">When it arrived, in the short Windows time format.</param>
/// <param name="AccessibleName">What a screen reader says, for example "Battery low, at 14:05".</param>
public sealed record MissedAlertItem(string Title, string? Subtitle, string Time, string AccessibleName)
{
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);
}

/// <summary>The Missed list, newest first, with a Clear button. Times are fixed clock times, so nothing ticks.</summary>
public sealed class MissedAlertsViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly IMissedAlerts? _missed;
    private IReadOnlyList<MissedAlertItem> _items = [];

    /// <param name="text">For the screen reader text.</param>
    /// <param name="missed">The Missed list. Null shows an empty list.</param>
    public MissedAlertsViewModel(ILocalizer text, IMissedAlerts? missed)
    {
        _text = text;
        _missed = missed;
        ClearCommand = new RelayCommand(() => _missed?.ClearMissed());
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<MissedAlertItem> Items
    {
        get => _items;
        private set
        {
            _items = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(AccessibleSummary));
        }
    }

    public int Count => _items.Count;

    public string CountText => Count.ToString(CultureInfo.CurrentCulture);

    public bool HasItems => Count > 0;

    public bool IsEmpty => Count == 0;

    /// <summary>What a screen reader says, for example "Missed alerts, 3".</summary>
    public string AccessibleSummary => IsEmpty
        ? _text["missed-alerts.accessibleEmpty"]
        : _text.Format("missed-alerts.accessible", ("count", Count));

    public IRelayCommand ClearCommand { get; }

    /// <summary>Reads the Missed list again. Called when it changes.</summary>
    public void Refresh() => Items = _missed?.Missed.Select(ToItem).ToList() ?? [];

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private MissedAlertItem ToItem(ShownAlert shown)
    {
        var time = TimeZoneInfo.ConvertTime(shown.ReceivedAt, TimeZoneInfo.Local).ToString("t", CultureInfo.CurrentCulture);
        var name = _text.Format("missed-alerts.item", ("title", shown.Alert.Title), ("time", time));
        return new MissedAlertItem(shown.Alert.Title, shown.Alert.Subtitle, time, name);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
}
