using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Sync;
using Atap.Features.Sync;
using Atap.Platform.OneDrive;
using Atap.Settings.Cards.General.Sync;
using Atap.Settings.Form;

[assembly: AtapSettingsCard(typeof(SyncCard))]

namespace Atap.Settings.Cards.General.Sync;

/// <summary>OneDrive sync on the General page: on or off, the folder, and the choice on the first turn on.</summary>
internal sealed class SyncCard : ISettingsCard
{
    public string Id => "sync";

    public string SectionId => SettingsSections.General;

    public int Order => 50;

    public string TitleKey => "synccard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/General/Sync/SyncViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context)
    {
        var services = context.Services;
        var catalog = services.Catalog;
        var stores = SyncParts.Stores(services.Settings, services.Layout, services.Features, catalog);
        var setup = new SyncSetup(stores, SyncParts.Scope(services.Features, catalog), new SyncLocalFiles(services.Paths), services.Time);
        var environment = new WindowsOneDriveEnvironment();
        var picker = new DialogPathPicker(() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive));
        return new SyncCardViewModel(
            services.Settings,
            setup,
            () => OneDriveLocator.SyncFolder(services.Settings.Values.GetString(SyncDescriptor.FolderKey), environment),
            services.Text,
            picker);
    }
}

internal sealed class SyncCardViewModel : ObservableObject, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly SyncSetup _setup;
    private readonly Func<string?> _findFolder;
    private readonly ILocalizer _text;
    private readonly IPathPicker? _picker;
    private string? _folder;
    private bool _isAsking;
    private string? _messageKey;
    private string? _messageFolder;

    /// <param name="settings">Holds sync.enabled and sync.folder.</param>
    /// <param name="setup">The first turn on.</param>
    /// <param name="findFolder">The sync folder, or null when OneDrive is not found.</param>
    /// <param name="text">UI text.</param>
    /// <param name="picker">The folder dialog, or null in tests.</param>
    public SyncCardViewModel(IAppSettings settings, SyncSetup setup, Func<string?> findFolder, ILocalizer text, IPathPicker? picker)
    {
        _settings = settings;
        _setup = setup;
        _findFolder = findFolder;
        _text = text;
        _picker = picker;
        _folder = findFolder();
        ChooseFolderCommand = new RelayCommand(ChooseFolder, () => _picker is not null);
        UseOneDriveCommand = new RelayCommand(() => Begin(SyncStartChoice.UseOneDrive));
        KeepThisPcCommand = new RelayCommand(() => Begin(SyncStartChoice.KeepThisPc));
        CancelCommand = new RelayCommand(() => IsAsking = false);
        _settings.Changed += OnSettingsChanged;
        _text.LanguageChanged += OnLanguageChanged;
        SyncStatus.Changed += OnStatusChanged;
    }

    public IRelayCommand ChooseFolderCommand { get; }

    public IRelayCommand UseOneDriveCommand { get; }

    public IRelayCommand KeepThisPcCommand { get; }

    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Turning it on asks first when this PC has never synced and OneDrive already has a sync file. It stays
    /// off until the user picks a side.
    /// </summary>
    public bool IsOn
    {
        get => _settings.Values.GetBool(SyncDescriptor.EnabledKey);
        set
        {
            if (value == IsOn)
                return;
            if (value)
                TurnOn();
            else
                _settings.Set(SyncDescriptor.EnabledKey, false);
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasFolder => _folder is not null;

    public bool NoFolder => _folder is null;

    public string FolderText => _folder ?? _text["synccard.noFolder"];

    public bool CanChooseFolder => _picker is not null;

    public bool IsAsking
    {
        get => _isAsking;
        private set => SetProperty(ref _isAsking, value);
    }

    public bool HasStatus => IsOn;

    public string StatusText => _text[SyncStatus.Health switch
    {
        SyncHealth.NoFolder => "synccard.status.noFolder",
        SyncHealth.Waiting => "synccard.status.waiting",
        SyncHealth.NewerFile => "synccard.status.newerFile",
        SyncHealth.DamagedFile => "synccard.status.damagedFile",
        SyncHealth.CannotWrite => "synccard.status.cannotWrite",
        _ => "synccard.status.working",
    }];

    public string? Message => _messageKey is null ? null : _text.Format(_messageKey, ("folder", _messageFolder));

    public bool HasMessage => _messageKey is not null;

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
        SyncStatus.Changed -= OnStatusChanged;
    }

    private void TurnOn()
    {
        Show(null);
        if (_folder is not { } folder)
            return;
        if (_setup.NeedsChoice(folder))
        {
            IsAsking = true;
            return;
        }
        _settings.Set(SyncDescriptor.EnabledKey, true);
    }

    /// <summary>Backs up the losing side, then turns sync on. When the backup fails nothing changes.</summary>
    private void Begin(SyncStartChoice choice)
    {
        IsAsking = false;
        if (_folder is not { } folder)
            return;
        if (_setup.Start(folder, choice) is not { } backup)
        {
            Show("synccard.backupFailed");
            return;
        }
        Show("synccard.backupSaved", backup);
        _settings.Set(SyncDescriptor.EnabledKey, true);
    }

    private void ChooseFolder()
    {
        if (_picker?.PickFolder(_folder) is not { } chosen)
            return;
        _settings.Set(SyncDescriptor.FolderKey, chosen);
    }

    private void Show(string? key, string? folder = null)
    {
        _messageKey = key;
        _messageFolder = folder;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        if (e.Keys.Contains(SyncDescriptor.FolderKey))
        {
            _folder = _findFolder();
            OnPropertyChanged(nameof(HasFolder));
            OnPropertyChanged(nameof(NoFolder));
            OnPropertyChanged(nameof(FolderText));
        }
        if (e.Keys.Contains(SyncDescriptor.EnabledKey))
        {
            OnPropertyChanged(nameof(IsOn));
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    private void OnStatusChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(StatusText));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(FolderText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Message));
    }
}
