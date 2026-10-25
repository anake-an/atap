using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Notch.Automation;
using Atap.Platform.Audio;
using Atap.Widgets.Audio;

[assembly: AtapWidget(typeof(AudioOutputDescriptor))]
[assembly: AtapWidget(typeof(AudioMixerDescriptor))]

namespace Atap.Widgets.Audio;

/// <summary>Pick the speakers or headphones. Constants only.</summary>
public sealed class AudioOutputDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "audio-output";

    public string TypeId => TypeIdValue;

    public string NameKey => "audio.output.name";

    public string DescriptionKey => "audio.output.description";

    public WidgetGroup Group => WidgetGroup.Media;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Small, WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Audio/AudioOutputViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new AudioOutputWidget(context, new WindowsAudioDevices());
}

/// <summary>Volume for each app making sound. Constants only.</summary>
public sealed class AudioMixerDescriptor : IWidgetDescriptor
{
    public const string TypeIdValue = "audio-mixer";

    public string TypeId => TypeIdValue;

    public string NameKey => "audio.mixer.name";

    public string DescriptionKey => "audio.mixer.description";

    public WidgetGroup Group => WidgetGroup.Media;

    public IReadOnlyList<WidgetSize> SupportedSizes { get; } = [WidgetSize.Medium];

    public IReadOnlyList<NotchState> SupportedStates { get; } = [NotchState.Expanded, NotchState.Panel];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public WidgetCapabilities Capabilities => WidgetCapabilities.None;

    public bool AllowsMultipleInstances => false;

    public bool WorksWithoutPlacement => false;

    public Uri ViewsSource { get; } = new("/Atap;component/Widgets/Audio/AudioMixerViews.xaml", UriKind.Relative);

    public IWidget Create(WidgetContext context) => new AudioMixerWidget(context, new WindowsAudioDevices());
}

/// <summary>Lists the outputs each time it is shown. Nothing runs while hidden.</summary>
public sealed class AudioOutputWidget : WidgetBase
{
    private readonly IAudioDevices _devices;
    private readonly AudioOutputViewModel _viewModel;

    internal AudioOutputWidget(WidgetContext context, IAudioDevices devices)
        : base(context)
    {
        _devices = devices;
        _viewModel = new AudioOutputViewModel(context.Text, Pick, devices.OpenSoundSettings);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Context.Visibility.Changed += OnVisibilityChanged;
        Refresh();
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        if (Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel)
            Refresh();
    }

    /// <summary>Switches, or opens Sound settings when Windows refuses.</summary>
    private void Pick(string id)
    {
        var switched = _devices.TrySetDefault(id);
        if (!switched)
            _devices.OpenSoundSettings();
        _viewModel.Show(_devices.Outputs(), failed: !switched);
    }

    private void Refresh() => _viewModel.Show(_devices.Outputs(), failed: false);
}

/// <summary>Reads the apps only while shown, again every 2 seconds, and stops when hidden.</summary>
public sealed class AudioMixerWidget : WidgetBase
{
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(2);
    public const int Step = 10;

    private readonly IAudioDevices _devices;
    private readonly AudioMixerViewModel _viewModel;
    private IDisposable? _tick;

    internal AudioMixerWidget(WidgetContext context, IAudioDevices devices)
        : base(context)
    {
        _devices = devices;
        _viewModel = new AudioMixerViewModel(context.Text, Change, ToggleMute);
    }

    public override object ViewModel => _viewModel;

    public override Task StartAsync(SettingValues settings, CancellationToken cancellationToken = default)
    {
        Context.Visibility.Changed += OnVisibilityChanged;
        OnVisibilityChanged(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public override Task StopAsync()
    {
        Context.Visibility.Changed -= OnVisibilityChanged;
        _tick?.Dispose();
        _tick = null;
        return Task.CompletedTask;
    }

    public override ValueTask DisposeAsync()
    {
        _viewModel.Dispose();
        return base.DisposeAsync();
    }

    private void OnVisibilityChanged(object? sender, EventArgs e)
    {
        var shown = Context.Visibility.Value is WidgetVisibility.Expanded or WidgetVisibility.Panel;
        if (shown && _tick is null)
        {
            Refresh();
            _tick = Context.Scheduler.Every(RefreshEvery, Refresh);
        }
        else if (!shown && _tick is not null)
        {
            _tick.Dispose();
            _tick = null;
        }
    }

    private void Change(AppAudio app, int delta)
    {
        _devices.SetAppVolume(app.Id, Math.Clamp(app.VolumePercent + delta, 0, 100));
        Refresh();
    }

    private void ToggleMute(AppAudio app)
    {
        _devices.SetAppMuted(app.Id, !app.Muted);
        Refresh();
    }

    private void Refresh() => _viewModel.Show(_devices.Apps());
}

/// <summary>An output in the list. Picking it makes it the default.</summary>
public sealed record OutputRow(string Name, bool IsDefault, IRelayCommand Pick);

public sealed class AudioOutputViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly Action<string> _pick;
    private IReadOnlyList<AudioOutput> _outputs = [];
    private bool _failed;

    internal AudioOutputViewModel(ILocalizer text, Action<string> pick, Action openSettings)
    {
        _text = text;
        _pick = pick;
        OpenSettingsCommand = new RelayCommand(openSettings);
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IRelayCommand OpenSettingsCommand { get; }

    public IReadOnlyList<OutputRow> Rows { get; private set; } = [];

    public bool HasNone => Rows.Count == 0;

    /// <summary>Windows would not switch, so Sound settings opened.</summary>
    public bool Failed => _failed;

    public string AccessibleSummary => Rows.FirstOrDefault(r => r.IsDefault) is { } current
        ? _text.Format("audio.output.accessible", ("name", current.Name))
        : _text["audio.output.none"];

    internal void Show(IReadOnlyList<AudioOutput> outputs, bool failed)
    {
        _outputs = outputs;
        _failed = failed;
        Rows = outputs.Select(o => new OutputRow(o.Name, o.IsDefault, new RelayCommand(() => _pick(o.Id), () => !o.IsDefault))).ToList();
        OnPropertyChanged(string.Empty);
    }

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_outputs, _failed);
}

/// <summary>An app in the mixer: name, level, and buttons.</summary>
public sealed record MixerRow(string Name, int Percent, string Value, bool Muted, IRelayCommand Down, IRelayCommand Up, IRelayCommand Mute, string MuteLabel);

public sealed class AudioMixerViewModel : ObservableObject, IAccessibleSummary, IDisposable
{
    private readonly ILocalizer _text;
    private readonly Action<AppAudio, int> _change;
    private readonly Action<AppAudio> _mute;
    private IReadOnlyList<AppAudio> _apps = [];

    internal AudioMixerViewModel(ILocalizer text, Action<AppAudio, int> change, Action<AppAudio> mute)
    {
        _text = text;
        _change = change;
        _mute = mute;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<MixerRow> Rows { get; private set; } = [];

    public bool HasNone => Rows.Count == 0;

    public string AccessibleSummary => _text.Format("audio.mixer.accessible", ("count", Rows.Count.ToString(CultureInfo.CurrentCulture)));

    internal void Show(IReadOnlyList<AppAudio> apps)
    {
        _apps = apps;
        Rows = apps.Select(a =>
        {
            var name = NameOf(a);
            return new MixerRow(
                name,
                a.Muted ? 0 : a.VolumePercent,
                a.Muted ? _text["audio.mixer.muted"] : a.VolumePercent.ToString(CultureInfo.CurrentCulture) + "%",
                a.Muted,
                new RelayCommand(() => _change(a, -AudioMixerWidget.Step)),
                new RelayCommand(() => _change(a, AudioMixerWidget.Step)),
                new RelayCommand(() => _mute(a)),
                _text.Format(a.Muted ? "audio.mixer.unmute" : "audio.mixer.mute", ("app", name)));
        }).ToList();
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"System sounds" for Windows' own sounds, "Another app" when Windows says nothing usable.</summary>
    internal string NameOf(AppAudio app) =>
        app.IsSystemSounds ? _text["audio.mixer.systemSounds"]
        : string.IsNullOrWhiteSpace(app.Name) ? _text["audio.mixer.otherApp"]
        : app.Name;

    public void Dispose() => _text.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => Show(_apps);
}
