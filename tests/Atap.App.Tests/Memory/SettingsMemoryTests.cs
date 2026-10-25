using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Storage;
using Atap.Core.Widgets;
using Atap.Settings.Shell;
using Atap.Tests.Composition;
using Atap.Tests.Infrastructure;
using Atap.Tests.Settings;
using Xunit.Abstractions;

namespace Atap.Tests.Memory;

/// <summary>
/// The real settings window, with every registered card on the real app services, opened on every page and
/// closed again, many times. A screen reader, or another app that reads windows, can keep a closed window's
/// automation peers, and with them the window and the views of the page it showed last. Here the test holds
/// them, as such an app would. Everything else Settings showed must still go.
/// </summary>
[Collection(MemoryProbe.Collection)]
public sealed class SettingsMemoryTests(ITestOutputHelper output) : IDisposable
{
    private const int Rounds = 5;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    // What a screen reader would hold: every automation peer of the last window, until the next one opens.
    private readonly List<AutomationPeer> _reader = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [ByHandFact]
    public async Task AClosedSettingsWindow_LetsGoOfItsPages()
    {
        var paths = new AtapPaths(Path.Combine(_folder, "roaming"), Path.Combine(_folder, "local"));
        Directory.CreateDirectory(paths.Roaming);
        File.WriteAllText(paths.SettingsFile, """{ "schemaVersion": 1, "values": { "language.choice": "en" } }""");

        var text = Localizer.Instance;
        var uiCulture = CultureInfo.CurrentUICulture;
        var defaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var services = Sta.Run(() => AppServices.Create(Sta.Dispatcher, AppMode.Normal, typeof(App).Assembly, paths, new NoViews(), null));
        try
        {
            await MemoryProbe.SettleAsync();
            output.WriteLine($"before: {MemoryProbe.Numbers()}");
            var earlier = new List<WeakReference>();
            Watched last = new([], []);
            for (var round = 0; round < Rounds; round++)
            {
                earlier.AddRange(last.Released.Concat(last.LastPage));
                last = Sta.Run(() => OpenAndClose(services));
                await MemoryProbe.SettleAsync();
                output.WriteLine($"round {round}: {MemoryProbe.Numbers()}, alive {earlier.Concat(last.Released).Count(w => w.IsAlive)}");
            }

            // Every page but the last one shown, and everything of the windows before.
            await MemoryProbe.AssertCollectedAsync(last.Released.Concat(earlier));
        }
        finally
        {
            Sta.Run(() => _reader.Clear());
            Sta.Run(services.Dispose);
            Sta.Run(() => Localizer.Instance = text);
            CultureInfo.CurrentUICulture = uiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = defaultUiCulture;
        }
    }

    /// <summary>What SettingsSession.Open builds and what it does on close, in a window off screen that never takes focus.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Watched OpenAndClose(AppServices services)
    {
        var log = new List<string>();
        var window = new SettingsWindow
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
        };
        var context = new SettingsShellContext
        {
            Text = services.Text,
            Settings = services.Settings,
            Secrets = new FakeSecretStore(),
            Definitions = services.Features.SelectMany(f => f.Settings).ToList(),
            Cards = SettingsCardSet.Create(services.SettingsCards, (m, e) => log.Add(m)),
            CreateCardViewModel = (card, lifetime) => card.CreateViewModel(new SettingsCardContext(services, lifetime)),
            Log = (m, e) => log.Add(m),
        };
        var model = new SettingsWindowViewModel(context, window.MergeViews);
        model.Show(null);
        window.DataContext = model;
        window.Show();

        var released = new List<WeakReference> { new(model) };
        var lastPage = new List<WeakReference>();
        foreach (var page in model.Pages)
        {
            model.SelectedPage = page;
            window.UpdateLayout();
            foreach (var expander in SettingsUi.Find<Expander>(window))
                expander.IsExpanded = true;
            window.UpdateLayout();

            released.AddRange(lastPage);
            lastPage = [new(model.Current)];
            lastPage.AddRange(model.Current!.Cards.Select(c => c.Content).OfType<object>().Select(c => new WeakReference(c)));
            lastPage.AddRange(model.Current.Forms.Select(f => new WeakReference(f)));
        }
        Assert.Empty(log);
        Assert.True(released.Count > 10, "Settings showed fewer cards than expected.");

        _reader.Clear();
        _reader.AddRange(AllPeers(UIElementAutomationPeer.CreatePeerForElement(window)));

        window.Close();
        window.DataContext = null;
        model.Dispose();
        return new Watched(released, lastPage);
    }

    private static IEnumerable<AutomationPeer> AllPeers(AutomationPeer peer) =>
        new[] { peer }.Concat((peer.GetChildren() ?? []).SelectMany(AllPeers));

    /// <param name="Released">Must be collected once the window is closed.</param>
    /// <param name="LastPage">The page on screen at close, which the screen reader may still keep.</param>
    private sealed record Watched(List<WeakReference> Released, List<WeakReference> LastPage);

    private sealed class NoViews : IWidgetViews
    {
        public IDisposable Acquire(IWidgetDescriptor descriptor) => new TrackedResource();
    }
}
