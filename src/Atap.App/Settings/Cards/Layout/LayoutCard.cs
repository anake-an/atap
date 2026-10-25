using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Features.LayoutEditor;
using Atap.Notch;
using Atap.Settings.Cards.Layout;

[assembly: AtapSettingsCard(typeof(LayoutCard))]

namespace Atap.Settings.Cards.Layout;

/// <summary>Edit layout, and a way back to the layout from before the last time the editor saved.</summary>
internal sealed class LayoutCard : ISettingsCard
{
    public string Id => "layout";

    public string SectionId => SettingsSections.Layout;

    public int Order => 10;

    public string TitleKey => "layoutcard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Layout/LayoutViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new LayoutCardViewModel(context.Services.Layout, context.Services.Commands, context.Services.Text, new LayoutBackup(context.Services.Paths.LayoutFile));
}

internal sealed class LayoutCardViewModel : ObservableObject, IDisposable
{
    private readonly ILayoutService _layout;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private readonly LayoutBackup _backup;
    private string? _messageKey;

    public LayoutCardViewModel(ILayoutService layout, ICommandRegistry commands, ILocalizer text, LayoutBackup backup)
    {
        _layout = layout;
        _commands = commands;
        _text = text;
        _backup = backup;
        EditCommand = new AsyncRelayCommand(() => _commands.TryRunAsync(NotchMenuViewModel.EditLayoutCommandId));
        RestoreCommand = new RelayCommand(Restore);
        _commands.Changed += OnCommandsChanged;
        _text.LanguageChanged += OnLanguageChanged;
    }

    public IAsyncRelayCommand EditCommand { get; }

    public IRelayCommand RestoreCommand { get; }

    public bool CanEdit => _commands.Contains(NotchMenuViewModel.EditLayoutCommandId);

    public bool CanRestore => _backup.Exists;

    public string? Message => _messageKey is null ? null : _text[_messageKey];

    public bool HasMessage => _messageKey is not null;

    public void Dispose()
    {
        _commands.Changed -= OnCommandsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>
    /// Swaps the arrangement with the saved copy, so pressing it again swaps back. Where the notch sits and each
    /// widget's settings stay as they are.
    /// </summary>
    private void Restore()
    {
        if (_backup.TryLoad() is not { } previous)
        {
            Show("layoutcard.restoreFailed");
            return;
        }
        _backup.TrySave(_layout.Document);
        var result = _layout.Update(document =>
        {
            document.EnabledWidgets = previous.EnabledWidgets;
            document.States = previous.States;
            document.CompactPriority = previous.CompactPriority;
            document.Profiles = previous.Profiles;
        });
        Show(result == LayoutWriteResult.Applied ? "layoutcard.restored" : "layoutcard.restoreFailed");
    }

    private void Show(string key)
    {
        _messageKey = key;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(CanRestore));
    }

    private void OnCommandsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CanEdit));

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(Message));
}
