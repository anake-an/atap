using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core;
using Atap.Core.Custom;
using Atap.Core.Localization;
using Atap.Core.Privacy;
using Atap.Core.Scheduling;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Privacy;
using Atap.Widgets.PrivacyDot;

[assembly: AtapWidget(typeof(PrivacyDotDescriptor))]

namespace Atap.Widgets.PrivacyDot;

/// <summary>A dot while an app uses the camera or the microphone. Constants only.</summary>
public sealed class PrivacyDotDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "privacy-dot";

    public string TypeId => TypeIdValue;

    public string NameKey => "privacy-dot.name";

    public string DescriptionKey => "privacy-dot.description";

    public WidgetGroup Group => WidgetGroup.System;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Compact, NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/PrivacyDot/PrivacyDotViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new PrivacyDotWidget(context, () => new RegistrySensorWatcher(context.Ui));
}

/// <summary>
/// Follows the Windows camera and microphone records, so nothing polls while no app uses them. Live while one does,
/// so the compact notch shows the dot. While in use it reads again every 5 seconds to drop apps that crashed.
/// </summary>
public sealed class PrivacyDotWidget : WidgetBase
{
    public static readonly TimeSpan RecheckEvery = TimeSpan.FromSeconds(5);

    private readonly Func<ISensorWatcher> _createWatcher;
    private readonly PrivacyDotViewModel _viewModel;
    private ISensorWatcher? _watcher;
    private IDisposable? _recheck;

    internal PrivacyDotWidget(WidgetContext context, Func<ISensorWatcher> createWatcher)
        : base(context)
    {
        _createWatcher = createWatcher;
        _viewModel = new PrivacyDotViewModel(context.Text);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        _watcher = _createWatcher();
        _watcher.Changed += OnChanged;
        OnChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        _recheck?.Dispose();
        _recheck = null;
        if (_watcher is not null)
        {
            _watcher.Changed -= OnChanged;
            _watcher.Dispose();
            _watcher = null;
        }
        SetIsLive(false);
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_watcher is not { } watcher)
            return;
        _viewModel.Show(watcher.Current);
        var inUse = watcher.Current.Count > 0;
        SetIsLive(inUse);
        if (inUse && _recheck is null)
        {
            _recheck = Context.Scheduler.Every(RecheckEvery, () => _watcher?.Refresh(), new ScheduleOptions(TimeSpan.FromSeconds(1), true));
        }
        else if (!inUse && _recheck is not null)
        {
            _recheck.Dispose();
            _recheck = null;
        }
    }
}

public sealed class PrivacyDotViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private IReadOnlyList<SensorUse> _uses = [];

    public PrivacyDotViewModel(ILocalizer text)
    {
        _text = text;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public bool CameraOn => _uses.Any(u => u.Sensor == Sensor.Camera);

    public bool MicOn => _uses.Any(u => u.Sensor == Sensor.Microphone);

    public bool NoneOn => _uses.Count == 0;

    /// <summary>"Zoom, Teams".</summary>
    public string CameraApps => Apps(Sensor.Camera);

    public string MicApps => Apps(Sensor.Microphone);

    /// <summary>"Camera", "Mic" or "Camera and mic", for the compact notch.</summary>
    public string CompactText => (CameraOn, MicOn) switch
    {
        (true, true) => _text["privacy-dot.both"],
        (true, false) => _text["privacy-dot.cameraShort"],
        (false, true) => _text["privacy-dot.micShort"],
        _ => string.Empty,
    };

    public string AccessibleSummary
    {
        get
        {
            if (NoneOn)
                return _text["privacy-dot.none"];
            var parts = new List<string>();
            if (CameraOn)
                parts.Add(_text.Format("privacy-dot.cameraUsedBy", ("apps", CameraApps)));
            if (MicOn)
                parts.Add(_text.Format("privacy-dot.micUsedBy", ("apps", MicApps)));
            return string.Join(". ", parts);
        }
    }

    internal void Show(IReadOnlyList<SensorUse> uses)
    {
        _uses = uses;
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private string Apps(Sensor sensor) => string.Join(", ", _uses.Where(u => u.Sensor == sensor).Select(u => u.AppName));

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
