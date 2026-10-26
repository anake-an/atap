using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Settings.Cards.Privacy.DeleteAll;

[assembly: AtapSettingsCard(typeof(DeleteAllCard))]

namespace Atap.Settings.Cards.Privacy.DeleteAll;

/// <summary>
/// "Delete all my Atap data", under Saved data. One button opens a confirm step in the card that says
/// what goes and that Atap closes. Only the second button deletes, through the app as it quits.
/// </summary>
internal sealed class DeleteAllCard : ISettingsCard
{
    public string Id => "deleteAll";

    public string SectionId => SettingsSections.Privacy;

    public int Order => 95;

    public string TitleKey => "deleteall.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Privacy/DeleteAll/DeleteAllViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) => new DeleteAllViewModel(context.Services.DeleteAllDataAndQuit);
}

internal sealed class DeleteAllViewModel : ObservableObject
{
    private readonly Action? _deleteAndQuit;
    private bool _isConfirming;
    private bool _isDeleting;

    /// <param name="deleteAndQuit">Quits Atap and deletes its data. Null when there is no app to quit, as in tests.</param>
    public DeleteAllViewModel(Action? deleteAndQuit)
    {
        _deleteAndQuit = deleteAndQuit;
        AskCommand = new RelayCommand(() => IsConfirming = true, () => !_isDeleting);
        CancelCommand = new RelayCommand(() => IsConfirming = false, () => !_isDeleting);
        DeleteCommand = new RelayCommand(Delete, () => _isConfirming && !_isDeleting && _deleteAndQuit is not null);
    }

    /// <summary>Opens the confirm step.</summary>
    public IRelayCommand AskCommand { get; }

    /// <summary>Closes the confirm step. Nothing is deleted.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>Deletes everything and quits. Only from the confirm step, and only once.</summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>True while the confirm step shows.</summary>
    public bool IsConfirming
    {
        get => _isConfirming;
        private set
        {
            if (!SetProperty(ref _isConfirming, value))
                return;
            OnPropertyChanged(nameof(IsAsking));
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>True while the first button shows.</summary>
    public bool IsAsking => !_isConfirming;

    /// <summary>True once the delete has started. Atap is closing.</summary>
    public bool IsDeleting
    {
        get => _isDeleting;
        private set => SetProperty(ref _isDeleting, value);
    }

    private void Delete()
    {
        if (!_isConfirming || _isDeleting || _deleteAndQuit is null)
            return;
        IsDeleting = true;
        AskCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        _deleteAndQuit();
    }
}
