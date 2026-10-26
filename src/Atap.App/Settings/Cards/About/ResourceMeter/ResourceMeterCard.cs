using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Diagnostics;
using Atap.Core.Localization;
using Atap.Core.Scheduling;
using Atap.Platform.Diagnostics;
using Atap.Settings.Cards.About.ResourceMeter;

[assembly: AtapSettingsCard(typeof(ResourceMeterCard))]

namespace Atap.Settings.Cards.About.ResourceMeter;

/// <summary>"Atap is using 45 MB of memory and 0.1% of the processor", with details and Copy details. On the About page.</summary>
internal sealed class ResourceMeterCard : ISettingsCard
{
    public string Id => "resourceMeter";

    public string SectionId => SettingsSections.About;

    public int Order => 10;

    public string TitleKey => "meter.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/About/ResourceMeter/ResourceMeterViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        var probe = new ProcessSampler(services.Time, () => services.Scheduler.Stats, () => services.Widgets.Widgets.Count, () => services.Shared.ActiveCount);
        return new ResourceMeterViewModel(probe, services.Scheduler, services.Text, AppInfo.Version, CopyToClipboard);
    }

    private static bool CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception)
        {
            // Another app holds the clipboard. The button says it did not copy.
            return false;
        }
    }
}

/// <summary>One line of the details: what it is and how much.</summary>
internal sealed record MeterRow(string Label, string Value);

/// <summary>
/// Samples every 2 seconds, only while the card is on screen, so a closed Settings window or another page costs
/// nothing. Nothing leaves the PC: Copy details puts the numbers on the clipboard for the user to paste.
/// </summary>
internal sealed class ResourceMeterViewModel : ObservableObject, IDisposable
{
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(2);

    private readonly IUsageProbe _probe;
    private readonly IScheduler _scheduler;
    private readonly ILocalizer _text;
    private readonly string _version;
    private readonly Func<string, bool> _copy;
    private IDisposable? _timer;
    private ResourceUsage? _usage;
    private string? _copyStatus;

    public ResourceMeterViewModel(IUsageProbe probe, IScheduler scheduler, ILocalizer text, string version, Func<string, bool> copy)
    {
        _probe = probe;
        _scheduler = scheduler;
        _text = text;
        _version = version;
        _copy = copy;
        CopyCommand = new RelayCommand(Copy, () => _usage is not null);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand CopyCommand { get; }

    /// <summary>True while samples are being taken, that is, while the card is on screen.</summary>
    public bool IsSampling => _timer is not null;

    public ResourceUsage? Usage => _usage;

    /// <summary>The one line most people want: memory and processor.</summary>
    public string Summary => _usage is { } usage
        ? _text.Format("meter.summary", ("mb", ResourceUsage.Megabytes(usage.Memory)), ("cpu", Percent(usage.CpuPercent)))
        : _text["meter.measuring"];

    public IReadOnlyList<MeterRow> Rows => _usage is not { } usage
        ? []
        :
        [
            new(_text["meter.memory"], Megabytes(usage.Memory)),
            new(_text["meter.privateBytes"], Megabytes(usage.PrivateBytes)),
            new(_text["meter.workingSet"], Megabytes(usage.WorkingSet)),
            new(_text["meter.managedHeap"], Megabytes(usage.ManagedHeap)),
            new(_text["meter.cpu"], Percent(usage.CpuPercent) + "%"),
            new(_text["meter.threads"], Count(usage.Threads)),
            new(_text["meter.handles"], Count(usage.Handles)),
            new(_text["meter.timers"], Count(usage.ArmedTimers)),
            new(_text["meter.wakes"], Count(usage.WakesLastHour)),
            new(_text["meter.widgets"], Count(usage.RunningWidgets)),
            new(_text["meter.shared"], Count(usage.SharedSources)),
            new(_text["meter.assemblies"], Count(usage.Assemblies)),
        ];

    /// <summary>"Copied" or "Could not copy" for a moment after Copy details. Null otherwise.</summary>
    public string? CopyStatus
    {
        get => _copyStatus;
        private set => SetProperty(ref _copyStatus, value);
    }

    /// <summary>The view calls this as the card comes on screen or goes off it.</summary>
    public void SetShown(bool shown)
    {
        if (shown == IsSampling)
            return;
        if (shown)
        {
            Sample();
            _timer = _scheduler.Every(SampleEvery, Sample);
        }
        else
        {
            _timer?.Dispose();
            _timer = null;
            CopyStatus = null;
        }
        OnPropertyChanged(nameof(IsSampling));
    }

    public void Dispose()
    {
        _text.LanguageChanged -= OnLanguageChanged;
        _timer?.Dispose();
        _timer = null;
    }

    private void Sample()
    {
        _usage = _probe.Sample();
        OnPropertyChanged(nameof(Usage));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Rows));
        CopyCommand.NotifyCanExecuteChanged();
    }

    private void Copy()
    {
        if (_usage is { } usage)
            CopyStatus = _text[_copy(usage.ToDiagnostics(_version)) ? "meter.copied" : "meter.copyFailed"];
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Rows));
    }

    private string Megabytes(long bytes) => _text.Format("meter.mb", ("mb", ResourceUsage.Megabytes(bytes)));

    private static string Percent(double value) => value.ToString(value < 10 ? "0.0" : "0", CultureInfo.CurrentCulture);

    private static string Count(int value) => value.ToString("N0", CultureInfo.CurrentCulture);
}

/// <summary>Tells the meter when its card comes on screen and goes off it: <c>meter:MeterShown.Track="True"</c>.</summary>
public static class MeterShown
{
    public static readonly DependencyProperty TrackProperty = DependencyProperty.RegisterAttached(
        "Track", typeof(bool), typeof(MeterShown), new PropertyMetadata(false, OnTrackChanged));

    public static bool GetTrack(DependencyObject element) => (bool)element.GetValue(TrackProperty);

    public static void SetTrack(DependencyObject element, bool value) => element.SetValue(TrackProperty, value);

    private static void OnTrackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;
        element.IsVisibleChanged += (_, _) => Update(element);
        element.Unloaded += (_, _) => (element.DataContext as ResourceMeterViewModel)?.SetShown(false);
        element.Loaded += (_, _) => Update(element);
    }

    private static void Update(FrameworkElement element) =>
        (element.DataContext as ResourceMeterViewModel)?.SetShown(element.IsVisible && element.IsLoaded);
}
