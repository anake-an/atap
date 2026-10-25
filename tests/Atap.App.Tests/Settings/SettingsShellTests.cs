using System.Runtime.CompilerServices;
using Atap.Composition;
using Atap.Core.Widgets;
using Atap.Features.SettingsWindow;
using Atap.Features.Startup;
using Atap.Settings.Form;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class SettingsShellTests : IDisposable
{
    private static readonly SettingDefinition Language = new(AppServices.LanguageChoiceKey, "language.choice", SettingType.Choice, "system", ["system", "en", "ms"])
    {
        SectionId = SettingsSections.General,
    };

    private readonly TestServices _services = new();
    private readonly List<string> _log = [];
    private readonly List<Uri> _merged = [];

    public SettingsShellTests() => _services.Settings.Load();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void EverySection_IsOnExactlyOnePage()
    {
        foreach (var section in SettingsSections.All)
        {
            var pages = SettingsPages.All.Where(p => p.Sections.Concat(p.MoreSections).Contains(section)).ToList();
            Assert.True(pages.Count == 1, $"Section '{section}' is on {pages.Count} pages.");
        }
    }

    [Fact]
    public void ThePagesAreTheApprovedFive()
    {
        Assert.Equal(
            new[] { SettingsPages.Home, SettingsPages.Shows, SettingsPages.Prayer, SettingsPages.Privacy, SettingsPages.About },
            SettingsPages.All.Select(p => p.Id));
        Assert.Equal(SettingsPages.About, SettingsPages.Find("about")?.Id);
        Assert.Equal(SettingsPages.About, SettingsPages.Find(SettingsSections.Updates)?.Id);
        Assert.Equal(SettingsPages.Home, SettingsPages.Find(SettingsSections.Advanced)?.Id);
        Assert.Null(SettingsPages.Find("nowhere"));
        Assert.Null(SettingsPages.Find(null));
    }

    [Fact]
    public void EveryPage_HasTextInBothLanguages()
    {
        foreach (var page in SettingsPages.All)
        {
            foreach (var key in new[] { page.TitleKey, page.HintKey, page.ComingSoonKey })
                Assert.NotEqual(key, _services.Text[key]);
        }
    }

    [Fact]
    public void Home_ShowsShowAtap_StartWithWindows_ThenLanguage()
    {
        var other = new SettingDefinition("other.thing", "other.thing", SettingType.Bool, false) { SectionId = SettingsSections.Appearance };
        var definitions = new List<SettingDefinition>();
        definitions.AddRange(new[] { other, Language });
        definitions.AddRange(new StartupDescriptor().Settings);
        definitions.AddRange(new ShowAtapDescriptor().Settings);
        using var window = CreateWindow(definitions, []);

        window.Show(SettingsPages.Home);

        var form = Assert.Single(window.Current!.Forms);
        Assert.Equal(
            new[] { ShowAtapDescriptor.ShowKey, StartupDescriptor.EnabledKey, AppServices.LanguageChoiceKey, "other.thing" },
            form.Fields.Select(f => f.Key));
        Assert.IsType<ChoiceField>(form.Find(AppServices.LanguageChoiceKey));
        Assert.False(window.Current.IsComingSoon);
    }

    [Fact]
    public void PagesWithNothingOnThem_SayComingSoon()
    {
        using var window = CreateWindow([Language], []);

        foreach (var id in new[] { SettingsPages.Prayer })
        {
            window.Show(id);
            Assert.True(window.Current!.IsComingSoon);
            Assert.Empty(window.Current.Items);
            Assert.NotEqual(window.Current.Page.ComingSoonKey, window.Current.ComingSoonText);
        }
    }

    [Fact]
    public void Cards_AppearOnTheirPage_InOrder_WithTheirViewsMerged()
    {
        using var window = CreateWindow([], [typeof(LateAboutCard), typeof(EarlyAboutCard), typeof(AdvancedCard)]);

        window.Show(SettingsPages.About);
        Assert.Equal(new[] { "early", "late" }, window.Current!.Items.Cast<SettingsCardViewModel>().Select(c => c.Id));
        Assert.Equal(new[] { EarlyAboutCard.Views, LateAboutCard.Views }, _merged);

        window.Show(SettingsPages.Home);
        Assert.Equal("advanced", Assert.Single(window.Current!.MoreItems.Cast<SettingsCardViewModel>()).Id);
        Assert.True(window.Current.HasMore);
    }

    [Fact]
    public void PagesAreBuilt_OnlyWhenFirstShown()
    {
        using var window = CreateWindow([], [typeof(EarlyAboutCard)]);

        Assert.Empty(window.BuiltPages);
        window.Show(null);
        Assert.Equal(SettingsPages.Home, window.Current!.Page.Id);
        Assert.Single(window.BuiltPages);
        Assert.Empty(_merged);
    }

    [Fact]
    public void ACardThatFails_ShowsANote_AndTheRestStillShow()
    {
        using var window = CreateWindow([], [typeof(BrokenCard), typeof(EarlyAboutCard)]);

        window.Show(SettingsPages.About);

        var cards = window.Current!.Items.Cast<SettingsCardViewModel>().ToList();
        Assert.True(cards.Single(c => c.Id == "broken").Failed);
        Assert.False(cards.Single(c => c.Id == "early").Failed);
        Assert.Contains(_log, l => l.Contains("'broken'", StringComparison.Ordinal));
    }

    [Fact]
    public void CardSet_LeavesOutDuplicateAndBrokenTypes()
    {
        var set = SettingsCardSet.Create([typeof(EarlyAboutCard), typeof(EarlyAboutCardAgain), typeof(ThrowingConstructorCard)], (m, e) => _log.Add(m));

        Assert.Equal("early", Assert.Single(set.Cards).Id);
        Assert.Equal(2, _log.Count);
    }

    [Fact]
    public void Closing_DisposesEveryCardAndForm()
    {
        var window = CreateWindow([Language], [typeof(EarlyAboutCard), typeof(AdvancedCard)]);
        foreach (var page in SettingsPages.All)
            window.Show(page.Id);
        var cards = window.BuiltPages.SelectMany(p => p.Cards).ToList();
        var models = cards.Select(c => c.Content).OfType<CardModel>().ToList();
        Assert.Equal(2, models.Count);

        window.Dispose();

        Assert.All(cards, c => Assert.True(c.IsDisposed));
        Assert.All(models, m => Assert.True(m.Disposed));
        Assert.All(models, m => Assert.True(m.Resource.Disposed));
        Assert.Null(window.Current);
    }

    [Fact]
    public void AClosedWindow_LeavesNothingBehind()
    {
        var watched = OpenAndClose();
        // The window, plus at least one form and its fields: the settings would keep a leaked form alive.
        Assert.True(watched.Count > 2);

        for (var i = 0; i < 5 && watched.Any(w => w.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(watched.Any(w => w.IsAlive), "The settings view models are still referenced after closing.");
    }

    [Fact]
    public void Labels_FollowTheLanguage()
    {
        var text = new Atap.Core.Localization.Localizer(new Atap.Core.Localization.AssemblyStringResources(typeof(App).Assembly), "en");
        using var window = new SettingsWindowViewModel(Context([], [], text), _merged.Add);
        window.Show(SettingsPages.Home);
        Assert.Equal("Home", window.Pages[0].Title);
        Assert.Equal("Atap Settings", window.Title);

        text.SetLanguage("ms");

        Assert.Equal("Utama", window.Pages[0].Title);
        Assert.Equal("Utama", window.Current!.Title);
        Assert.Equal("Tetapan Atap", window.Title);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<WeakReference> OpenAndClose()
    {
        var window = CreateWindow([Language, .. new ShowAtapDescriptor().Settings], [typeof(EarlyAboutCard), typeof(AdvancedCard)]);
        List<WeakReference> watched = [new(window)];
        foreach (var page in SettingsPages.All)
        {
            window.Show(page.Id);
            foreach (var form in window.Current!.Items.Concat(window.Current.MoreItems).OfType<SettingsFormViewModel>())
            {
                watched.Add(new WeakReference(form));
                watched.AddRange(form.Fields.Select(f => new WeakReference(f)));
            }
        }
        window.Dispose();
        return watched;
    }

    private SettingsWindowViewModel CreateWindow(IReadOnlyList<SettingDefinition> definitions, IReadOnlyList<Type> cards)
    {
        _services.Settings.Register(definitions);
        return new SettingsWindowViewModel(Context(definitions, cards, _services.Text), _merged.Add);
    }

    private SettingsShellContext Context(IReadOnlyList<SettingDefinition> definitions, IReadOnlyList<Type> cards, Atap.Core.Localization.ILocalizer text) => new()
    {
        Text = text,
        Settings = _services.Settings,
        Secrets = new FakeSecretStore(),
        Definitions = definitions,
        Cards = SettingsCardSet.Create(cards, (m, e) => _log.Add(m)),
        CreateCardViewModel = (card, lifetime) => card.CreateViewModel(new SettingsCardContext(null!, lifetime)),
        Log = (m, e) => _log.Add(m),
    };

    internal sealed class Resource : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    internal sealed class CardModel(IWidgetLifetime lifetime) : IDisposable
    {
        public Resource Resource { get; } = Add(lifetime);

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        private static Resource Add(IWidgetLifetime lifetime)
        {
            var resource = new Resource();
            lifetime.Add(resource);
            return resource;
        }
    }

    internal abstract class TestCard(string id, string section, int order) : ISettingsCard
    {
        public string Id => id;

        public string SectionId => section;

        public int Order => order;

        public string TitleKey => "version.title";

        public Uri ViewsSource => new($"/Atap.Tests;component/{id}.xaml", UriKind.Relative);

        public virtual object CreateViewModel(SettingsCardContext context) => new CardModel(context.Lifetime);
    }

    internal sealed class EarlyAboutCard() : TestCard("early", SettingsSections.About, 0)
    {
        public static readonly Uri Views = new("/Atap.Tests;component/early.xaml", UriKind.Relative);
    }

    internal sealed class EarlyAboutCardAgain() : TestCard("early", SettingsSections.Updates, 5);

    internal sealed class LateAboutCard() : TestCard("late", SettingsSections.Updates, 90)
    {
        public static readonly Uri Views = new("/Atap.Tests;component/late.xaml", UriKind.Relative);
    }

    internal sealed class AdvancedCard() : TestCard("advanced", SettingsSections.Advanced, 0);

    internal sealed class BrokenCard() : TestCard("broken", SettingsSections.About, 50)
    {
        public override object CreateViewModel(SettingsCardContext context) => throw new InvalidOperationException("Broken on purpose.");
    }

    internal sealed class ThrowingConstructorCard : TestCard
    {
        public ThrowingConstructorCard()
            : base("throws", SettingsSections.About, 0) => throw new InvalidOperationException("Broken on purpose.");
    }
}
