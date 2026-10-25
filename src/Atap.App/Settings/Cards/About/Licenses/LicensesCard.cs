using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Settings.Cards.About.Licenses;
using Atap.Settings.Shell;

[assembly: AtapSettingsCard(typeof(LicensesCard))]

namespace Atap.Settings.Cards.About.Licenses;

/// <summary>Atap's licence and the notices of the software it uses, from the files next to Atap.exe.</summary>
internal sealed class LicensesCard : ISettingsCard
{
    public string Id => "licenses";

    public string SectionId => SettingsSections.About;

    public int Order => 90;

    public string TitleKey => "licenses.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/About/Licenses/LicensesViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new LicensesCardViewModel(AppContext.BaseDirectory, ShellOpen.File);
}

internal sealed class LicensesCardViewModel : ObservableObject
{
    public const string LicenseFile = "LICENSE.txt";
    public const string NoticesFile = "THIRD-PARTY-NOTICES.md";

    private readonly string _folder;
    private readonly Func<string, bool> _open;
    private bool _missing;

    /// <param name="folder">Where the licence files ship, next to Atap.exe.</param>
    /// <param name="open">Opens a file. False when it could not.</param>
    public LicensesCardViewModel(string folder, Func<string, bool> open)
    {
        _folder = folder;
        _open = open;
        OpenLicenseCommand = new RelayCommand(() => Open(LicenseFile));
        OpenNoticesCommand = new RelayCommand(() => Open(NoticesFile));
    }

    public IRelayCommand OpenLicenseCommand { get; }

    public IRelayCommand OpenNoticesCommand { get; }

    /// <summary>True after a file could not be opened.</summary>
    public bool IsMissing
    {
        get => _missing;
        private set => SetProperty(ref _missing, value);
    }

    private void Open(string name) => IsMissing = !_open(Path.Combine(_folder, name));
}
