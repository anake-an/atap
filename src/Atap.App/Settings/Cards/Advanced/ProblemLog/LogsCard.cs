using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Diagnostics;
using Atap.Settings.Cards.Advanced.ProblemLog;
using Atap.Settings.Shell;

[assembly: AtapSettingsCard(typeof(LogsCard))]

namespace Atap.Settings.Cards.Advanced.ProblemLog;

/// <summary>Opens the folder with Atap's short problem log, for sending with a bug report.</summary>
internal sealed class LogsCard : ISettingsCard
{
    public string Id => "logs";

    public string SectionId => SettingsSections.Advanced;

    public int Order => 90;

    public string TitleKey => "logs.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Advanced/ProblemLog/LogsViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) => new LogsCardViewModel(Log.Folder, ShellOpen.Folder);
}

internal sealed class LogsCardViewModel : ObservableObject
{
    private readonly string _folder;
    private readonly Func<string, bool> _open;
    private bool _noLog;

    /// <param name="folder">The log folder.</param>
    /// <param name="open">Opens a folder. False when it does not exist.</param>
    public LogsCardViewModel(string folder, Func<string, bool> open)
    {
        _folder = folder;
        _open = open;
        OpenCommand = new RelayCommand(() => NoLog = !_open(_folder));
    }

    public IRelayCommand OpenCommand { get; }

    /// <summary>True after Open found no log folder yet.</summary>
    public bool NoLog
    {
        get => _noLog;
        private set => SetProperty(ref _noLog, value);
    }
}
