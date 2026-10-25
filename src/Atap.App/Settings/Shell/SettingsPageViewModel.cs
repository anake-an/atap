using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Settings;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Form;

namespace Atap.Settings.Shell;

/// <summary>What the settings window needs from the app. Built when the window opens.</summary>
internal sealed class SettingsShellContext
{
    public required ILocalizer Text { get; init; }

    public required IAppSettings Settings { get; init; }

    public required ISecretStore Secrets { get; init; }

    /// <summary>Every registered app setting. Those with a section id appear on that section's page.</summary>
    public required IReadOnlyList<SettingDefinition> Definitions { get; init; }

    public required SettingsCardSet Cards { get; init; }

    /// <summary>Builds a card's view model. The lifetime ends when the window closes.</summary>
    public required Func<ISettingsCard, OwnerLifetime, object?> CreateCardViewModel { get; init; }

    /// <summary>File and folder dialogs, or null in tests.</summary>
    public IPathPicker? Picker { get; init; }

    /// <summary>Warnings, with the exception if any.</summary>
    public required Action<string, Exception?> Log { get; init; }
}

/// <summary>A hand-made card on a page, with its title and the view model its own views show.</summary>
internal sealed class SettingsCardViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _text;
    private readonly OwnerLifetime _lifetime;
    private readonly Action<string, Exception?> _log;

    public SettingsCardViewModel(ISettingsCard card, SettingsShellContext context, Action<Uri> ensureViews)
    {
        Id = card.Id;
        TitleKey = card.TitleKey;
        _text = context.Text;
        _log = context.Log;
        _lifetime = new OwnerLifetime($"settings.{card.Id}", ex => _log($"Settings card '{card.Id}' reported an error.", ex));
        _lifetime.DisposeFailed += (_, ex) => _log($"Settings card '{card.Id}' failed to release a resource.", ex);
        try
        {
            ensureViews(card.ViewsSource);
            Content = context.CreateCardViewModel(card, _lifetime);
        }
        catch (Exception ex)
        {
            _log($"Settings card '{card.Id}' could not be shown.", ex);
            Content = null;
        }
    }

    public string Id { get; }

    public string TitleKey { get; }

    public string Title => _text[TitleKey];

    public bool HasTitle => !string.IsNullOrEmpty(TitleKey);

    /// <summary>The card's own view model, shown with the template its views give for that type.</summary>
    public object? Content { get; }

    public bool Failed => Content is null;

    public string FailedText => _text["settings.card.failed"];

    /// <summary>True once the window closed and the card let go of everything.</summary>
    internal bool IsDisposed => _lifetime.IsDisposed;

    internal void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(FailedText));
    }

    public void Dispose()
    {
        try
        {
            (Content as IDisposable)?.Dispose();
        }
        catch (Exception ex)
        {
            _log($"Settings card '{Id}' failed to close.", ex);
        }
        _lifetime.Dispose();
    }
}

/// <summary>
/// One page: the generated form of every setting in its sections, then their cards in order, then
/// "More options". A page with nothing on it yet shows a short "coming soon" line.
/// </summary>
internal sealed class SettingsPageViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _text;
    private readonly List<object> _items;
    private readonly List<object> _moreItems;

    public SettingsPageViewModel(SettingsPage page, SettingsShellContext context, Action<Uri> ensureViews)
    {
        Page = page;
        _text = context.Text;
        _items = Build(page, page.Sections, context, ensureViews, splitAdvanced: true);
        _moreItems = Build(page, page.MoreSections, context, ensureViews, splitAdvanced: false);
    }

    public SettingsPage Page { get; }

    public string Title => _text[Page.TitleKey];

    public string Hint => _text[Page.HintKey];

    /// <summary>Forms and cards, in order.</summary>
    public IReadOnlyList<object> Items => _items;

    /// <summary>Forms and cards of the page's "More options" sections.</summary>
    public IReadOnlyList<object> MoreItems => _moreItems;

    public bool HasMore => _moreItems.Count > 0;

    public string MoreTitle => _text["settingsform.moreOptions"];

    public string MoreHint => _text["settingsform.moreOptionsHint"];

    public bool IsComingSoon => _items.Count == 0 && _moreItems.Count == 0;

    public string ComingSoonText => _text[Page.ComingSoonKey];

    internal IEnumerable<SettingsCardViewModel> Cards => _items.Concat(_moreItems).OfType<SettingsCardViewModel>();

    internal IEnumerable<SettingsFormViewModel> Forms => _items.Concat(_moreItems).OfType<SettingsFormViewModel>();

    internal void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(MoreTitle));
        OnPropertyChanged(nameof(MoreHint));
        OnPropertyChanged(nameof(ComingSoonText));
        foreach (var card in Cards)
            card.OnLanguageChanged();
    }

    public void Dispose()
    {
        foreach (var item in _items.Concat(_moreItems))
            (item as IDisposable)?.Dispose();
    }

    private static List<object> Build(
        SettingsPage page, IReadOnlyList<string> sections, SettingsShellContext context, Action<Uri> ensureViews, bool splitAdvanced)
    {
        var items = new List<object>();
        if (sections.Count == 0)
            return items;

        var inPage = sections.ToHashSet(StringComparer.Ordinal);
        var definitions = context.Definitions.Where(d => d.SectionId is { } id && inPage.Contains(id)).ToList();
        if (definitions.Count > 0)
        {
            var form = new SettingsFormContext(new AppSettingsTarget(context.Settings), context.Text, context.Secrets, context.Picker);
            items.Add(new SettingsFormViewModel(definitions, form, splitAdvanced, page.KeyOrder));
        }
        foreach (var card in context.Cards.For(sections))
            items.Add(new SettingsCardViewModel(card, context, ensureViews));
        return items;
    }
}
