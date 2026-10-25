using CommunityToolkit.Mvvm.ComponentModel;
using Atap.Core.Localization;

namespace Atap.Settings.Shell;

/// <summary>A page in the left list.</summary>
internal sealed class SettingsNavItem : ObservableObject
{
    private readonly ILocalizer _text;

    public SettingsNavItem(SettingsPage page, ILocalizer text)
    {
        Page = page;
        _text = text;
    }

    public SettingsPage Page { get; }

    public string Title => _text[Page.TitleKey];

    public string Glyph => Page.Glyph;

    /// <summary>What a screen reader says for the list item.</summary>
    public override string ToString() => Title;

    internal void OnLanguageChanged() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// The settings window: the page list and the page shown. A page is built the first time it is shown and
/// kept until the window closes. Everything here is created when the window opens and disposed when it
/// closes, so nothing of Settings stays in memory afterwards.
/// </summary>
internal sealed class SettingsWindowViewModel : ObservableObject, IDisposable
{
    private readonly SettingsShellContext _context;
    private readonly Action<Uri> _ensureViews;
    private readonly Dictionary<string, SettingsPageViewModel> _pages = new(StringComparer.Ordinal);
    private SettingsNavItem? _selected;
    private SettingsPageViewModel? _current;
    private bool _disposed;

    /// <param name="context">The app services the pages use.</param>
    /// <param name="ensureViews">Merges a card's views into the window before its page shows.</param>
    public SettingsWindowViewModel(SettingsShellContext context, Action<Uri> ensureViews)
    {
        _context = context;
        _ensureViews = ensureViews;
        Pages = SettingsPages.All.Select(p => new SettingsNavItem(p, context.Text)).ToList();
        _context.Text.LanguageChanged += OnLanguageChanged;
    }

    public IReadOnlyList<SettingsNavItem> Pages { get; }

    public string Title => _context.Text["settings.window.title"];

    public SettingsNavItem? SelectedPage
    {
        get => _selected;
        set
        {
            if (value is null || _disposed || !SetProperty(ref _selected, value))
                return;
            Current = PageFor(value.Page);
        }
    }

    public SettingsPageViewModel? Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    /// <summary>Pages built so far, for tests.</summary>
    internal IReadOnlyCollection<SettingsPageViewModel> BuiltPages => _pages.Values;

    /// <summary>Shows a page by its id or by a section on it. Anything else keeps the current page, or Home.</summary>
    public void Show(string? pageOrSection)
    {
        var page = SettingsPages.Find(pageOrSection);
        if (page is null && _selected is not null)
            return;
        page ??= SettingsPages.All[0];
        SelectedPage = Pages.First(p => p.Page.Id == page.Id);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _context.Text.LanguageChanged -= OnLanguageChanged;
        Current = null;
        foreach (var page in _pages.Values)
            page.Dispose();
        _pages.Clear();
    }

    private SettingsPageViewModel PageFor(SettingsPage page)
    {
        if (!_pages.TryGetValue(page.Id, out var model))
        {
            model = new SettingsPageViewModel(page, _context, _ensureViews);
            _pages[page.Id] = model;
        }
        return model;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Title));
        foreach (var item in Pages)
            item.OnLanguageChanged();
        foreach (var page in _pages.Values)
            page.OnLanguageChanged();
    }
}
