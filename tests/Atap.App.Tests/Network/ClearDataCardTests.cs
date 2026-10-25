using System.Net.Http;
using System.Windows.Automation;
using System.Windows.Controls;
using Atap.Composition;
using Atap.Core.Localization;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Settings.Cards.Privacy.ClearData;
using Atap.Settings.Shell;
using Atap.Tests.Infrastructure;
using Atap.Tests.Settings;

namespace Atap.Tests.Network;

public sealed class ClearDataCardTests : IDisposable
{
    private readonly TestServices _services = new();

    public ClearDataCardTests()
    {
        _services.Settings.Load();
    }

    private string Cache => _services.Paths.Cache;

    public void Dispose() => _services.Dispose();

    [Fact]
    public void TheCard_IsOnThePrivacyPage_WithATitle()
    {
        var card = new ClearDataCard();

        Assert.Equal(SettingsPages.Privacy, SettingsPages.Find(card.SectionId)?.Id);
        Assert.NotEqual(card.TitleKey, _services.Text[card.TitleKey]);
        Assert.Contains(typeof(ClearDataCard), Discovery.SettingsCards(typeof(App).Assembly));
    }

    [Fact]
    public async Task Clear_RemovesTheCache_AndSaysHowMuchItFreed()
    {
        Write(Path.Combine("http", "a.cache"), 300 * 1024);
        Write("other.bin", 200 * 1024);
        var model = Create();
        Assert.Equal("Using 500 KB on this PC.", model.SizeText);
        Assert.False(model.HasResult);

        await model.ClearCommand.ExecuteAsync(null);

        Assert.Empty(Directory.EnumerateFileSystemEntries(Cache));
        Assert.True(model.HasResult);
        Assert.Equal("Done. 500 KB freed.", model.ResultText);
        Assert.Equal("Nothing is saved right now.", model.SizeText);

        await model.ClearCommand.ExecuteAsync(null);
        Assert.Equal("There was nothing to clear.", model.ResultText);
    }

    [Fact]
    public async Task Clear_AlsoForgetsTheCopiesTheHttpServiceKeepsInMemory()
    {
        var sent = 0;
        using var http = new HttpDataService("test", new Online(), () => new Answer(() => sent++), _services.Time);
        var network = new WidgetCapabilities(WidgetPermissions.Network | WidgetPermissions.UserUrls, []);
        var request = new FetchRequest(new Uri("http://127.0.0.1:8123/api/states"), TimeSpan.FromHours(1));
        await http.For(network).GetAsync(request);
        var model = new ClearDataViewModel(new SavedData(http, Cache), _services.Text, run => Task.FromResult(run()));

        await model.ClearCommand.ExecuteAsync(null);
        _services.Time.Advance(TimeSpan.FromSeconds(1));
        var again = await http.For(network).GetAsync(request);

        Assert.Equal(FetchStatus.Fresh, again.Status);
        Assert.Equal(2, sent);
    }

    [Fact]
    public async Task FilesInUse_AreLeft_AndTheCardSaysSo()
    {
        Write("locked.cache", 100);
        Write("free.cache", 2 * 1024 * 1024);
        var model = Create();
        Assert.Equal("Using 2 MB on this PC.", model.SizeText);

        using (new FileStream(Path.Combine(Cache, "locked.cache"), FileMode.Open, FileAccess.Read, FileShare.None))
            await model.ClearCommand.ExecuteAsync(null);

        Assert.Equal("2 MB freed. A few files are in use, so they were left. Try again later.", model.ResultText);
        Assert.Equal("Using 1 KB on this PC.", model.SizeText);
    }

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(1L, "1 KB")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1025L, "2 KB")]
    [InlineData(1048575L, "1,024 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(5347737L, "5.1 MB")]
    public void Sizes_ArePlain(long bytes, string expected)
    {
        Assert.Equal(expected, ClearDataViewModel.FormatSize(bytes, _services.Text));
    }

    [Fact]
    public void Malay_FollowsALanguageSwitch()
    {
        Write("a.cache", 2048);
        var text = new Localizer(new AssemblyStringResources(typeof(App).Assembly), Localizer.English);
        var model = new ClearDataViewModel(new SavedData(_services.Http, Cache), text, run => Task.FromResult(run()));
        var changed = new List<string?>();
        model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        text.SetLanguage(Localizer.Malay);

        Assert.Contains(nameof(ClearDataViewModel.SizeText), changed);
        Assert.Equal("Menggunakan 2 KB pada PC ini.", model.SizeText);
        model.Dispose();
    }

    [Fact]
    public void TheViews_ShowTheSize_AndANamedButton() => Sta.Run(() =>
    {
        Write("a.cache", 4096);
        var model = Create();
        var host = SettingsUi.Render(model, new ClearDataCard().ViewsSource);

        var button = Assert.Single(SettingsUi.Find<Button>(host));
        Assert.Equal("Clear saved data", AutomationProperties.GetName(button) is { Length: > 0 } name ? name : button.Content);
        Assert.True(button.IsTabStop && button.Focusable);
        Assert.Contains(SettingsUi.Visible<TextBlock>(host), t => t.Text == "Using 4 KB on this PC.");
        Assert.DoesNotContain(SettingsUi.Visible<TextBlock>(host), t => t.Text == model.ResultText);
    });

    private ClearDataViewModel Create() =>
        new(new SavedData(_services.Http, Cache), _services.Text, run => Task.FromResult(run()));

    private void Write(string relative, int bytes)
    {
        var path = Path.Combine(Cache, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private sealed class Online : INetworkState
    {
        public bool IsOnline => true;

        public bool IsMetered => false;

        public string? WifiSsid => null;

        public SsidAvailability Ssid => SsidAvailability.NotRequested;

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public IDisposable WantSsid() => throw new NotSupportedException();
    }

    private sealed class Answer(Action sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
