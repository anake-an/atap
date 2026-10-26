using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Composition;
using Atap.Core;
using Atap.Core.Commands;
using Atap.Core.Layout;
using Atap.Core.Localization;
using Atap.Core.Widgets;
using Atap.Features.LayoutEditor;
using Atap.Notch;
using Atap.Settings.Cards.Layout;
using Atap.Ui.Preview;

[assembly: AtapSettingsCard(typeof(LayoutCard))]

namespace Atap.Settings.Cards.Layout;

/// <summary>
/// A picture of each notch state as it is set up now, Edit layout, and a way back to the layout from before the
/// last time the editor saved.
/// </summary>
internal sealed class LayoutCard : ISettingsCard
{
    public string Id => "layout";

    public string SectionId => SettingsSections.Layout;

    public int Order => 10;

    public string TitleKey => "layoutcard.title";

    public Uri ViewsSource { get; } = new("/Atap;component/Settings/Cards/Layout/LayoutViews.xaml", UriKind.Relative);

    public object CreateViewModel(SettingsCardContext context) =>
        new LayoutCardViewModel(
            context.Services.Layout,
            context.Services.Catalog,
            context.Services.Commands,
            context.Services.Text,
            new LayoutBackup(context.Services.Paths.LayoutFile));
}

internal sealed class LayoutCardViewModel : ObservableObject, IDisposable
{
    private readonly ILayoutService _layout;
    private readonly WidgetCatalog _catalog;
    private readonly ICommandRegistry _commands;
    private readonly ILocalizer _text;
    private readonly LayoutBackup _backup;
    private string? _messageKey;
    private IReadOnlyList<NotchPicture>? _pictures;

    public LayoutCardViewModel(ILayoutService layout, WidgetCatalog catalog, ICommandRegistry commands, ILocalizer text, LayoutBackup backup)
    {
        _layout = layout;
        _catalog = catalog;
        _commands = commands;
        _text = text;
        _backup = backup;
        EditCommand = new AsyncRelayCommand(() => _commands.TryRunAsync(NotchMenuViewModel.EditLayoutCommandId));
        RestoreCommand = new RelayCommand(Restore);
        _commands.Changed += OnCommandsChanged;
        _text.LanguageChanged += OnLanguageChanged;
        _layout.Changed += OnLayoutChanged;
    }

    public IAsyncRelayCommand EditCommand { get; }

    public IRelayCommand RestoreCommand { get; }

    public bool CanEdit => _commands.Contains(NotchMenuViewModel.EditLayoutCommandId);

    public bool CanRestore => _backup.Exists;

    public string? Message => _messageKey is null ? null : _text[_messageKey];

    public bool HasMessage => _messageKey is not null;

    /// <summary>
    /// The small notch, the notch when you point at it, and the first panel page, with sample content. Built when
    /// first shown and again after the layout changes.
    /// </summary>
    public IReadOnlyList<NotchPicture> Pictures => _pictures ??= BuildPictures();

    public void Dispose()
    {
        _commands.Changed -= OnCommandsChanged;
        _text.LanguageChanged -= OnLanguageChanged;
        _layout.Changed -= OnLayoutChanged;
    }

    private IReadOnlyList<NotchPicture> BuildPictures()
    {
        var active = _layout.Active;
        var pages = active.PanelPages;
        // The same first page a click opens: an unfilled panel shows the hover widgets, a size up.
        // There the missed alerts only show once something is missed, so the picture leaves them out.
        var followsHover = PanelContent.FollowsHover(pages);
        var shown = PanelContent.Pages(
            active,
            id => _catalog.TryResolve(id, out var descriptor) ? descriptor : null,
            id => !followsHover || id != DefaultLayout.MissedAlertsWidgetId);
        var panel = NotchPreviews.Picture(_catalog, NotchState.Panel, shown.Count > 0 ? shown[0] : [], _text);
        if (shown.Count > 1)
            panel = panel with { Note = _text.Format("preview.pageOf", ("count", shown.Count.ToString(CultureInfo.CurrentCulture))) };
        return
        [
            NotchPreviews.Picture(_catalog, NotchState.Idle, active.Idle, _text),
            NotchPreviews.Picture(_catalog, NotchState.Expanded, active.Expanded, _text),
            panel,
        ];
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

    private void RefreshPictures()
    {
        _pictures = null;
        OnPropertyChanged(nameof(Pictures));
    }

    private void OnCommandsChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(CanEdit));

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        RefreshPictures();
        OnPropertyChanged(nameof(CanRestore));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Message));
        RefreshPictures();
    }
}
