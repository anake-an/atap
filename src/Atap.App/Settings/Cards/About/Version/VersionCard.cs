using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Localization;
using Atap.Settings.Cards.About.Version;
using Atap.Updates;

[assembly: AtapSettingsCard(typeof(VersionCard))]

namespace Atap.Settings.Cards.About.Version;

/// <summary>The version, and Check for updates through the "updates.check" command. Store builds say the Store updates Atap.</summary>
internal sealed class VersionCard : ISettingsCard
{
    public string Id => "version";

    public string SectionId => SettingsSections.About;

    public int Order => 0;

    public string TitleKey => "version.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/About/Version/VersionViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        return new VersionCardViewModel(AppInfo.Version, services.Updates, services.Commands, services.Text);
    }
}

internal sealed class VersionCardViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateService? _updates;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;

    /// <param name="version">Shown as "Atap 0.1.0".</param>
    /// <param name="updates">Null in Store builds.</param>
    /// <param name="commands">Runs "updates.check" when the app registered it.</param>
    /// <param name="text">UI text.</param>
    public VersionCardViewModel(string version, IUpdateService? updates, ICommandRegistry commands, ILocalizer text)
    {
        Version = version;
        _updates = updates;
        _commands = commands;
        _text = text;
        CheckCommand = new AsyncRelayCommand(() => _commands.TryRunAsync(AppCommands.CheckForUpdates), () => CanCheck);
        _commands.Changed += OnChanged;
        _text.LanguageChanged += OnChanged;
        if (_updates is not null)
            _updates.StatusChanged += OnChanged;
    }

    public string Version { get; }

    public string VersionText => _text.Format("version.name", ("version", Version));

    /// <summary>False in Store builds.</summary>
    public bool ShowCheck => _updates is not null;

    /// <summary>True while "updates.check" is registered and can run right now.</summary>
    public bool CanCheck =>
        _commands.All.FirstOrDefault(c => c.Id == AppCommands.CheckForUpdates) is { } command
        && (command.IsAvailable?.Invoke() ?? true);

    public IAsyncRelayCommand CheckCommand { get; }

    public string StatusText => _text[_updates switch
    {
        null => "version.status.store",
        { IsSupported: false } when AppInfo.InstallSource == Core.Updates.InstallSource.Scoop => "version.status.scoop",
        { IsSupported: false } => "version.status.unsupported",
        { Status: UpdateStatus.Checking } => "version.status.checking",
        { Status: UpdateStatus.Downloading } => "version.status.downloading",
        { Status: UpdateStatus.ReadyToInstall } => "version.status.ready",
        { Status: UpdateStatus.Installing } => "version.status.installing",
        { Status: UpdateStatus.UpToDate } => "version.status.upToDate",
        _ => "version.status.idle",
    }];

    public void Dispose()
    {
        _commands.Changed -= OnChanged;
        _text.LanguageChanged -= OnChanged;
        if (_updates is not null)
            _updates.StatusChanged -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanCheck));
        CheckCommand.NotifyCanExecuteChanged();
    }
}
