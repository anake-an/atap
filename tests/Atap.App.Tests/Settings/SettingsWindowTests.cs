using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Atap.Composition;
using Atap.Core.Placement;
using Atap.Core.Widgets;
using Atap.Features.SettingsWindow;
using Atap.Features.Startup;
using Atap.Settings.Cards.About.Licenses;
using Atap.Settings.Cards.About.Version;
using Atap.Settings.Cards.Advanced.ProblemLog;
using Atap.Settings.Cards.Position;
using Atap.Settings.Cards.Privacy.NetworkFeatures;
using Atap.Settings.Cards.Widgets;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Settings;

public sealed class SettingsWindowTests : IDisposable
{
    private static readonly Type[] BaseCards =
    [
        typeof(PositionCard), typeof(WidgetsCard), typeof(NetworkFeaturesCard), typeof(VersionCard), typeof(LicensesCard), typeof(LogsCard),
    ];

    private readonly TestServices _services = new();
    private readonly List<string> _log = [];

    public SettingsWindowTests()
    {
        _services.Settings.Load();
        _services.Layout.Load();
    }

    public void Dispose() => _services.Dispose();

    [Fact]
    public void TheBaseCards_AreRegistered_AndEachHasAPageAndATitle()
    {
        var types = Discovery.SettingsCards(typeof(App).Assembly);
        Assert.All(BaseCards, t => Assert.Contains(t, types));

        var set = SettingsCardSet.Create(types, (m, e) => _log.Add(m));
        Assert.Empty(_log);
        foreach (var card in set.Cards)
        {
            Assert.NotNull(SettingsPages.Find(card.SectionId));
            Assert.NotEqual(card.TitleKey, _services.Text[card.TitleKey]);
        }
    }

    [Fact]
    public void EveryCardsViews_Load() => Sta.Run(() =>
    {
        foreach (var card in SettingsCardSet.Create(BaseCards, (m, e) => _log.Add(m)).Cards)
        {
            var views = (ResourceDictionary)Application.LoadComponent(card.ViewsSource);
            Assert.Contains(views.Values.Cast<object>(), v => v is DataTemplate);
        }
    });

    [Fact]
    public void TheFeatures_AreRegistered_WithoutProblems()
    {
        var problems = new List<string>();
        var features = Discovery.Features(typeof(App).Assembly, problems);

        Assert.Empty(problems);
        Assert.Contains(features, f => f.Id == SettingsWindowDescriptor.FeatureId);
        Assert.Contains(features, f => f.Id == ShowAtapDescriptor.FeatureId);
        Assert.Contains(features, f => f.Id == StartupDescriptor.FeatureId);
    }

    [Fact]
    public void EveryPage_Shows_WithEveryControlNamed() => Sta.Run(() =>
    {
        var catalog = WidgetCatalog.FromDescriptors(
        [
            new SettingsTestDescriptor("clock", [new SettingDefinition("use24Hour", "clock.use24Hour", SettingType.Bool, true)]),
            new SettingsTestDescriptor("weather", capabilities: new WidgetCapabilities(WidgetPermissions.Network, ["api.data.gov.my"])),
        ]);
        var host = new FakeHostView(catalog) { Ids = { "clock", "clock#second" } };
        host.Statuses["clock"] = WidgetStatus.Running;
        host.Statuses["clock#second"] = WidgetStatus.Failed;
        var definitions = new List<SettingDefinition>(new ShowAtapDescriptor().Settings);
        definitions.AddRange(new StartupDescriptor().Settings);
        _services.Settings.Register(definitions);

        var context = new SettingsShellContext
        {
            Text = _services.Text,
            Settings = _services.Settings,
            Secrets = new FakeSecretStore(),
            Definitions = definitions,
            Cards = SettingsCardSet.Create(BaseCards, (m, e) => _log.Add(m)),
            CreateCardViewModel = (card, lifetime) => card.Id switch
            {
                "position" => new PositionCardViewModel(_services.Layout, new FixedMonitors(), _services.Commands, _services.Text),
                "widgets" => new WidgetsCardViewModel(host, _services.Layout, _services.Text, new FakeSecretStore(), null),
                "networkFeatures" => new NetworkFeaturesViewModel(catalog, _services.Layout, true, _services.Text),
                "version" => new VersionCardViewModel("1.2.3", null, _services.Commands, _services.Text),
                "licenses" => new LicensesCardViewModel(_services.Paths.Roaming, _ => false),
                "logs" => new LogsCardViewModel(_services.Paths.Roaming, _ => false),
                _ => null,
            },
            Log = (m, e) => _log.Add(m),
        };

        var window = new SettingsWindow
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        var model = new SettingsWindowViewModel(context, window.MergeViews);
        model.Show(null);
        window.DataContext = model;
        window.Show();
        try
        {
            AssertNamed(window.Nav);
            foreach (var page in model.Pages)
            {
                model.SelectedPage = page;
                foreach (var row in model.Current!.Cards.Select(c => c.Content).OfType<WidgetsCardViewModel>().SelectMany(c => c.Rows))
                    row.IsExpanded = row.HasSettings;
                window.UpdateLayout();
                foreach (var expander in SettingsUi.Find<Expander>(window))
                    expander.IsExpanded = true;
                window.UpdateLayout();

                Assert.DoesNotContain(model.Current.Cards, c => c.Failed);
                foreach (var control in SettingsUi.Find<Control>(window).Where(IsTabStop))
                    AssertNamed(control);
            }

            // Failed shows Restart, and only there.
            model.Show(SettingsPages.Shows);
            window.UpdateLayout();
            var restart = SettingsUi.Find<Button>(window).Where(b => b.IsVisible && b.Command is not null)
                .Select(b => UIElementAutomationPeer.CreatePeerForElement(b).GetName())
                .Where(n => n.StartsWith("Restart", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(new[] { "Restart Clock (second)" }, restart);
        }
        finally
        {
            window.Close();
            model.Dispose();
        }
        Assert.Empty(_log);
    });

    private static bool IsTabStop(Control control) =>
        control.IsVisible && control.Focusable && control.IsTabStop && control is not ListBoxItem and not ComboBoxItem
        && control.TemplatedParent is not Control;

    private static void AssertNamed(Control control)
    {
        var peer = UIElementAutomationPeer.CreatePeerForElement(control);
        Assert.False(string.IsNullOrWhiteSpace(peer.GetName()), $"A {control.GetType().Name} has no accessible name.");
        if (control is ListBox)
        {
            foreach (var item in peer.GetChildren() ?? [])
                Assert.False(string.IsNullOrWhiteSpace(item.GetName()), $"An item of {peer.GetName()} has no accessible name.");
        }
    }

    private sealed class FixedMonitors : IMonitorList
    {
        public IReadOnlyList<DisplayMonitor> Monitors { get; } =
        [
            new(@"\\?\DISPLAY#MAIN", "Main", new PixelRect(0, 0, 1920, 1080), IsPrimary: true),
            new(@"\\?\DISPLAY#SIDE", "Side", new PixelRect(1920, 0, 2560, 1440), IsPrimary: false),
        ];

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }
}
