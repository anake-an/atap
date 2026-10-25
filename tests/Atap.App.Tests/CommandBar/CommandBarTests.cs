using Atap.Core.Commands;
using Atap.Core.Commands.Query;
using Atap.Features.CommandBar;
using Atap.Platform.Apps;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.CommandBar;

/// <summary>Sums, units, commands, apps and web search in the command bar, and opening and closing it.</summary>
public sealed class CommandBarTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakeApps _apps = new();
    private readonly List<string> _opened = [];
    private readonly List<string> _copied = [];
    private readonly List<string> _ran = [];

    public void Dispose() => _services.Dispose();

    [Fact]
    public void ASum_IsWorkedOut_AndEnterCopiesIt()
    {
        var bar = Bar();

        bar.Query = "12*8";

        Assert.Equal(QueryResultKind.Calculation, bar.Rows[0].Kind);
        Assert.Equal("= 96", bar.Rows[0].Title);
        Assert.True(bar.RunSelected());
        Assert.Equal(["96"], _copied);
    }

    [Fact]
    public void Units_Convert()
    {
        var bar = Bar();

        bar.Query = "5 km to mi";

        var row = Assert.Single(bar.Rows, r => r.Kind == QueryResultKind.Conversion);
        Assert.StartsWith("5 km = 3.1", row.Title);
    }

    [Fact]
    public async Task TypingAnAppName_LaunchesIt()
    {
        _apps.Apps = [new InstalledApp("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"), new InstalledApp("Notepad", @"C:\Windows\notepad.exe")];
        var bar = Bar();
        await bar.LoadAppsAsync();

        bar.Query = "notep";

        Assert.Equal("Notepad", bar.Rows[0].Title);
        bar.RunSelected();
        Assert.Equal(["Notepad"], _apps.Launched);
    }

    [Fact]
    public async Task AppsThatArriveLate_AreRankedIntoWhatWasTyped()
    {
        var bar = Bar();
        bar.Query = "calcul";
        Assert.DoesNotContain(bar.Rows, r => r.Kind == QueryResultKind.App);

        _apps.Apps = [new InstalledApp("Calculator", "calc")];
        await bar.LoadAppsAsync();

        Assert.Contains(bar.Rows, r => r is { Kind: QueryResultKind.App, Title: "Calculator" });
    }

    [Fact]
    public void WebSearch_IsAlwaysLast_AndOpensOnlyOnEnter()
    {
        var bar = Bar();

        bar.Query = "cuaca kuala lumpur";

        var last = bar.Rows[^1];
        Assert.Equal(QueryResultKind.WebSearch, last.Kind);
        Assert.Empty(_opened);

        bar.SelectedIndex = bar.Rows.Count - 1;
        bar.RunSelected();
        Assert.Equal(["https://duckduckgo.com/?q=cuaca%20kuala%20lumpur"], _opened);
    }

    [Fact]
    public async Task AtapCommands_AreFound_ByTitleOrKeyword()
    {
        using var pause = Command("alerts.pause", "quick.command.pauseAlerts", "quick.pauseKeywords");
        var bar = Bar();

        bar.Query = "mute";

        var row = Assert.Single(bar.Rows, r => r.Kind == QueryResultKind.Command);
        Assert.Equal(_services.Text["quick.command.pauseAlerts"], row.Title);
        bar.Run(row);
        await Task.Yield();
        Assert.Equal(["alerts.pause"], _ran);
    }

    [Fact]
    public void TheCommandBarItself_AndCommandsThatCannotRun_AreLeftOut()
    {
        using var open = Command(CommandBarViewModel.OpenCommandId, "commandbar.open");
        using var off = _services.Commands.Register(new AtapCommand("x.off", "commandbar.open", _ => Task.CompletedTask) { IsAvailable = () => false });
        var bar = Bar();

        bar.Query = "command bar";

        Assert.DoesNotContain(bar.Rows, r => r.Kind == QueryResultKind.Command);
    }

    [Fact]
    public void ARowRuns_AfterTheWindowIsAskedToClose()
    {
        var bar = Bar();
        var order = new List<string>();
        bar.Done += (_, _) => order.Add("done");
        _copied.Clear();

        bar.Query = "2+2";
        bar.Run(new CommandBarRow(QueryResultKind.Calculation, "= 4", "", "", () => order.Add("ran")));

        Assert.Equal(["done", "ran"], order);
    }

    [Fact]
    public void UpAndDown_WrapRound()
    {
        var bar = Bar();
        bar.Query = "1+1";
        Assert.Equal(0, bar.SelectedIndex);

        bar.Move(-1);
        Assert.Equal(bar.Rows.Count - 1, bar.SelectedIndex);

        bar.Move(1);
        Assert.Equal(0, bar.SelectedIndex);
    }

    [Fact]
    public void NothingTyped_ShowsNothing()
    {
        var bar = Bar();

        bar.Query = "   ";

        Assert.Empty(bar.Rows);
        Assert.False(bar.RunSelected());
    }

    [Fact]
    public async Task TheCommand_OpensAndClosesTheBar_WithTheChosenSearch()
    {
        _services.Settings.Load();
        _services.Settings.Register(new CommandBarDescriptor().Settings);
        _services.Settings.Set(CommandBarDescriptor.SearchKey, "bing");
        var shell = new FakeShell();
        var feature = new CommandBarFeature(_services.Commands, _services.Text, _services.Settings, _apps, shell);
        await feature.StartAsync();

        Assert.True(await _services.Commands.TryRunAsync(CommandBarViewModel.OpenCommandId));
        Assert.NotNull(shell.Shown);
        shell.Shown!.Query = "x";
        Assert.Equal(QueryResultKind.WebSearch, shell.Shown.Rows[^1].Kind);

        Assert.True(await _services.Commands.TryRunAsync(CommandBarViewModel.OpenCommandId));
        Assert.False(shell.IsOpen);

        await feature.DisposeAsync();
        Assert.False(_services.Commands.Contains(CommandBarViewModel.OpenCommandId));
    }

    private CommandBarViewModel Bar() => new(_services.Commands, _services.Text, _apps, _opened.Add, v =>
    {
        _copied.Add(v);
        return true;
    }, CommandBarDescriptor.SearchPages["duckduckgo"]);

    private IDisposable Command(string id, string titleKey, string? keywordsKey = null) =>
        _services.Commands.Register(new AtapCommand(id, titleKey, _ =>
        {
            _ran.Add(id);
            return Task.CompletedTask;
        }) { KeywordsKey = keywordsKey });

    private sealed class FakeApps : IAppCatalog
    {
        public IReadOnlyList<InstalledApp> Apps { get; set; } = [];

        public List<string> Launched { get; } = [];

        public Task<IReadOnlyList<InstalledApp>> LoadAsync() => Task.FromResult(Apps);

        public void Launch(InstalledApp app) => Launched.Add(app.Name);
    }

    private sealed class FakeShell : ICommandBarShell
    {
        public CommandBarViewModel? Shown { get; private set; }

        public bool IsOpen { get; private set; }

        public void Show(CommandBarViewModel viewModel)
        {
            Shown = viewModel;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;

        public void Dispose() => IsOpen = false;
    }
}
