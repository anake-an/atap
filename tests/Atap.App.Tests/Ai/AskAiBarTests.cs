using System.Xml.Linq;
using Atap.Core.Ai;
using Atap.Core.Commands;
using Atap.Core.Commands.Query;
using Atap.Core.Widgets;
using Atap.Features.Ai;
using Atap.Features.CommandBar;
using Atap.Features.Hotkeys;
using Atap.Platform.Apps;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Ai;

/// <summary>The Ask AI row and answer pane in the command bar: what sends, what stops it, and what the pane may hold.</summary>
public sealed class AskAiBarTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakeAskAi _ai = new();
    private readonly List<string> _copied = [];
    private string? _clipboard;
    private int _clipboardReads;

    public void Dispose() => _services.Dispose();

    [Fact]
    public void Typing_SendsNothing_AndOffersAskAiAboveWebSearch()
    {
        using var bar = Bar();

        bar.Query = "w";
        bar.Query = "what is the time in Tokyo";

        Assert.Empty(_ai.Asked);
        Assert.Equal(QueryResultKind.WebSearch, bar.Rows[^1].Kind);
        var row = bar.Rows[^2];
        Assert.Equal(QueryResultKind.AskAi, row.Kind);
        Assert.Equal("Ask AI: \"what is the time in Tokyo\"", row.Title);
        Assert.Equal(0, _clipboardReads);
    }

    [Fact]
    public void Enter_OnTheRow_Sends_AndKeepsTheBarOpen()
    {
        using var bar = Bar();
        var closed = 0;
        bar.Done += (_, _) => closed++;
        bar.Query = "what is the capital of Malaysia";
        bar.SelectedIndex = bar.Rows.Count - 2;

        Assert.True(bar.RunSelected());

        var turns = Assert.Single(_ai.Asked).Turns;
        Assert.Equal([new AiTurn(AiRole.User, "what is the capital of Malaysia")], turns);
        Assert.Equal(0, closed);
        Assert.Equal(string.Empty, bar.Query);
        Assert.True(bar.Ai!.HasConversation);
        Assert.True(bar.Ai.IsAsking);
        Assert.Equal("Asking Claude", bar.Ai.StatusText);
        Assert.Equal("Ask anything, then press Enter", bar.Placeholder);
    }

    [Fact]
    public void TheAnswer_Streams_AndAFollowUpCarriesTheEarlierTurns()
    {
        using var bar = Bar();
        Ask(bar, "capital of Malaysia?");

        _ai.Last.Delta("Kuala ");
        _ai.Last.Delta("Lumpur");
        Assert.Equal(string.Empty, bar.Ai!.AnswerText);
        _services.Time.Advance(AskAiViewModel.RefreshEvery);
        Assert.Equal("Kuala Lumpur", bar.Ai!.AnswerText);
        _ai.Last.Finish(AiEvent.Done);

        Assert.False(bar.Ai.IsAsking);
        Assert.Equal("Answer from Claude. Type a follow-up and press Enter.", bar.Ai.StatusText);
        Ask(bar, "and of Japan?");
        Assert.Equal(
            [new AiTurn(AiRole.User, "capital of Malaysia?"), new AiTurn(AiRole.Assistant, "Kuala Lumpur"), new AiTurn(AiRole.User, "and of Japan?")],
            _ai.Last.Turns);
    }

    [Fact]
    public void ClosingTheBar_StopsTheAnswer_AndForgetsTheConversation()
    {
        var bar = Bar();
        Ask(bar, "a long question");

        bar.Dispose();

        Assert.True(_ai.Last.Token.IsCancellationRequested);
        Assert.Equal(0, _ai.Subscribers);
    }

    [Fact]
    public void ANewQuestion_StopsTheLastOne_WhichIsNotKept()
    {
        using var bar = Bar();
        Ask(bar, "first");
        var first = _ai.Last;

        Ask(bar, "second");

        Assert.True(first.Token.IsCancellationRequested);
        first.Finish(AiEvent.Failed(AiError.Stopped));
        Assert.True(bar.Ai!.IsAsking);
        Assert.Equal([new AiTurn(AiRole.User, "second")], _ai.Last.Turns);
    }

    [Fact]
    public void Stop_AndChangingTheKey_StopTheAnswer()
    {
        using var bar = Bar();
        Ask(bar, "first");
        bar.Ai!.StopCommand.Execute(null);
        Assert.True(_ai.Last.Token.IsCancellationRequested);
        _ai.Last.Finish(AiEvent.Failed(AiError.Stopped));
        Assert.Equal("Stopped.", bar.Ai.StatusText);
        Assert.False(bar.Ai.HasFailed);

        Ask(bar, "second");
        _ai.RaiseSetupChanged();
        Assert.True(_ai.Last.Token.IsCancellationRequested);
    }

    [Fact]
    public void AnError_ShowsPlainWords_AndAskAgainSendsTheSameQuestion()
    {
        using var bar = Bar();
        Ask(bar, "hello");

        _ai.Last.Finish(AiEvent.Failed(AiError.OutOfCredit));

        Assert.True(bar.Ai!.HasFailed);
        Assert.Equal("Your Claude account is out of credit. Top it up on their site.", bar.Ai.StatusText);
        Assert.True(bar.Ai.AgainCommand.CanExecute(null));
        bar.Ai.AgainCommand.Execute(null);
        Assert.Equal(2, _ai.Asked.Count);
        Assert.Equal([new AiTurn(AiRole.User, "hello")], _ai.Last.Turns);
    }

    [Fact]
    public void CopiedText_IsReadOnlyWhenTicked_ShownFirst_AndSentOnce()
    {
        _clipboard = "Meeting notes: budget is RM 5,000";
        using var bar = Bar();
        bar.Query = "summarise this";
        bar.SelectedIndex = bar.Rows.Count - 2;
        Assert.True(bar.ShowCopiedOption);
        Assert.Equal(0, _clipboardReads);

        bar.Ai!.IncludeCopied = true;

        Assert.Equal(1, _clipboardReads);
        Assert.Equal("Meeting notes: budget is RM 5,000", bar.Ai.CopiedPreview);
        bar.RunSelected();
        Assert.Equal(AiPrompt.Build("summarise this", _clipboard), Assert.Single(_ai.Last.Turns).Text);
        Assert.Contains("<copied>", _ai.Last.Turns[0].Text);
        Assert.False(bar.Ai.IncludeCopied);
        Assert.Equal("summarise this", bar.Ai.QuestionText.Replace("You asked: ", string.Empty));
    }

    [Fact]
    public void LongCopiedText_IsCut_AndSaysSo()
    {
        _clipboard = new string('a', 9000);
        using var bar = Bar();

        bar.Ai!.IncludeCopied = true;

        Assert.Equal("Only the first 8,000 characters go.", bar.Ai.CopiedNote);
        _clipboard = null;
        bar.Ai.IncludeCopied = false;
        bar.Ai.IncludeCopied = true;
        Assert.Equal("There is no copied text right now.", bar.Ai.CopiedNote);
    }

    [Fact]
    public void WithoutAKey_TheRowOpensSettings_AndSendsNothing()
    {
        _ai.Problem = AiSetupProblem.NoKey;
        var opened = new List<string?>();
        using var settings = _services.Commands.Register(new AtapCommand(CommandBarViewModel.SettingsCommandId, "commandbar.open", arg =>
        {
            opened.Add(arg);
            return Task.CompletedTask;
        }));
        using var bar = Bar();
        var closed = 0;
        bar.Done += (_, _) => closed++;
        bar.Query = "hello";
        var row = bar.Rows.Single(r => r.Kind == QueryResultKind.AskAi);

        bar.Run(row);

        Assert.Equal("Set up Ask AI in Settings", row.Title);
        Assert.Equal(["behavior"], opened);
        Assert.Equal(1, closed);
        Assert.Empty(_ai.Asked);
        Assert.False(bar.ShowCopiedOption);
    }

    [Fact]
    public void ReadyToAsk_ShowsOnlyTheAskAiRow()
    {
        using var bar = Bar(askMode: true);

        bar.Query = "2+2";

        var row = Assert.Single(bar.Rows);
        Assert.Equal(QueryResultKind.AskAi, row.Kind);
        Assert.Equal("Ask anything, then press Enter", bar.Placeholder);
    }

    [Fact]
    public void TheAnswer_IsPlainText_WithLineBreaksKept_AndControlsTakenOut()
    {
        using var bar = Bar();
        Ask(bar, "q");

        _ai.Last.Delta("First line‮ reversed\u0007\r\n\r\n\r\n\r\nSecond   line\n");
        _services.Time.Advance(AskAiViewModel.RefreshEvery);

        Assert.Equal("First line reversed\n\nSecond line", bar.Ai!.AnswerText);
    }

    [Fact]
    public void ManyTinyPieces_AreGathered_AndShownFast()
    {
        using var bar = Bar();
        var shown = 0;
        Ask(bar, "q");
        bar.Ai!.PropertyChanged += (_, e) => shown += e.PropertyName == nameof(AskAiViewModel.AnswerText) ? 1 : 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 65_000; i++)
        {
            _ai.Last.Delta(i % 100 == 99 ? "\n" : "a");
            // A refresh every 1,000 pieces, as a fast stream past the 75 ms timer would get.
            if (i % 1_000 == 999)
                _services.Time.Advance(AskAiViewModel.RefreshEvery);
        }
        _ai.Last.Finish(AiEvent.Done);

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}.");
        Assert.True(shown < 100, $"Shown {shown} times.");
        Assert.Equal(650, bar.Ai.AnswerText.Split('\n').Length);
        Assert.Equal(64_350, bar.Ai.AnswerText.Count(c => c == 'a'));
    }

    [Fact]
    public void AskAgain_LeavesCopiedTextOut_UnlessTickedAgain()
    {
        _clipboard = "private notes";
        using var bar = Bar();
        bar.Ai!.IncludeCopied = true;
        Ask(bar, "summarise");
        Assert.Contains("private notes", _ai.Last.Turns[^1].Text);
        _ai.Last.Finish(AiEvent.Failed(AiError.ServiceBusy));

        bar.Ai.AgainCommand.Execute(null);
        Assert.Equal("summarise", _ai.Last.Turns[^1].Text);
        _ai.Last.Finish(AiEvent.Failed(AiError.ServiceBusy));

        bar.Ai.IncludeCopied = true;
        bar.Ai.AgainCommand.Execute(null);
        Assert.Contains("private notes", _ai.Last.Turns[^1].Text);
        Assert.False(bar.Ai.IncludeCopied);
    }

    [Fact]
    public void ChangingTheService_UnticksCopiedText()
    {
        _clipboard = "private notes";
        using var bar = Bar();
        bar.Ai!.IncludeCopied = true;
        Ask(bar, "first");
        _ai.Last.Finish(AiEvent.Done);
        bar.Ai.IncludeCopied = true;

        _ai.RaiseSetupChanged();

        Assert.False(bar.Ai.IncludeCopied);
        bar.Ai.AgainCommand.Execute(null);
        Assert.DoesNotContain("private notes", _ai.Last.Turns[^1].Text);
    }

    [Fact]
    public void Copy_PutsTheAnswerOnTheClipboard()
    {
        using var bar = Bar();
        Ask(bar, "q");
        _ai.Last.Delta("The answer");
        _services.Time.Advance(AskAiViewModel.RefreshEvery);

        bar.Ai!.CopyCommand.Execute(null);

        Assert.Equal(["The answer"], _copied);
        Assert.Equal("Copied", bar.Ai.CopyText);
    }

    [Fact]
    public void WithAskAiOff_ThereIsNoRow()
    {
        using var bar = new CommandBarViewModel(_services.Commands, _services.Text, new NoApps(), _ => { }, _ => true, CommandBarDescriptor.SearchPages["google"]);

        bar.Query = "hello";

        Assert.DoesNotContain(bar.Rows, r => r.Kind == QueryResultKind.AskAi);
        Assert.False(bar.ShowCopiedOption);
    }

    [Fact]
    public async Task TheShortcut_OpensTheBarReadyToAsk()
    {
        var setting = new HotkeysDescriptor().Settings.Single(s => s.Key == HotkeysDescriptor.AskAiKey);
        Assert.Equal("hotkeys.ai.ask", setting.Key);
        Assert.Equal(string.Empty, setting.DefaultValue);
        Assert.Contains(HotkeysDescriptor.AskAiKey, HotkeysDescriptor.Keys);

        _services.Settings.Load();
        _services.Settings.Register(new CommandBarDescriptor().Settings);
        _services.Settings.Register(new AiDescriptor().Settings);
        _services.Settings.Register(new Atap.Features.NetworkPrivacy.NetworkPrivacyDescriptor().Settings);
        var shell = new FakeShell();
        await using var bar = new CommandBarFeature(_services.Commands, _services.Text, _services.Settings, new NoApps(), shell, () => _ai, () => _clipboard);
        using var transport = new AiTransport("test", _services.Network);
        var secrets = new Atap.Platform.Secrets.DpapiSecretStore(_services.Paths.SecretsFile);
        await using var feature = new AiFeature(_services.Commands, _services.Settings, secrets, _services.Text, transport, (_, _) => { });
        await bar.StartAsync();
        await feature.StartAsync();

        Assert.Same(feature, AskAi.Current);
        Assert.True(await _services.Commands.TryRunAsync(AiDescriptor.AskCommand));

        Assert.True(shell.Shown!.IsAskMode);
        Assert.NotNull(shell.Shown.Ai);
        await feature.DisposeAsync();
        Assert.Null(AskAi.Current);
        Assert.False(_services.Commands.Contains(AiDescriptor.AskCommand));
    }

    /// <summary>
    /// The answer is shown only as plain text. This fails if the command bar gains anything that can parse,
    /// link or load what a service sends: links, rich text, web views, frames, images or XAML parsing.
    /// </summary>
    [Fact]
    public void ThePane_HoldsNoLinksRichTextOrWebViews()
    {
        var file = Path.Combine(RepoPaths.App, "Features", "CommandBar", "CommandBarWindow.xaml");
        var document = XDocument.Load(file);
        string[] refused = ["Hyperlink", "RichTextBox", "WebBrowser", "WebView2", "Frame", "FlowDocument", "FlowDocumentScrollViewer", "DocumentViewer", "Image", "MediaElement"];
        var names = document.Descendants().Select(e => e.Name.LocalName).ToHashSet(StringComparer.Ordinal);
        foreach (var name in refused)
            Assert.DoesNotContain(name, names);

        var answer = document.Descendants().Single(e => e.Name.LocalName == "TextBox" && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "Answer");
        Assert.Equal("True", (string?)answer.Attribute("IsReadOnly"));
        Assert.Contains("Mode=OneWay", (string?)answer.Attribute("Text"));

        foreach (var code in new[] { "CommandBarWindow.xaml.cs", "CommandBarViewModel.cs" }.Select(f => Path.Combine(RepoPaths.App, "Features", "CommandBar", f))
            .Append(Path.Combine(RepoPaths.App, "Features", "Ai", "AskAiViewModel.cs")))
        {
            var text = File.ReadAllText(code);
            foreach (var parser in new[] { "XamlReader", "Hyperlink", "RichTextBox", "WebBrowser", "FlowDocument", "BitmapImage" })
                Assert.DoesNotContain(parser, text, StringComparison.Ordinal);
        }
    }

    private CommandBarViewModel Bar(bool askMode = false)
    {
        var pane = new AskAiViewModel(_ai, _services.Text, ui: null, () =>
        {
            _clipboardReads++;
            return _clipboard;
        }, v =>
        {
            _copied.Add(v);
            return true;
        }, _services.Time);
        return new CommandBarViewModel(_services.Commands, _services.Text, new NoApps(), _ => { }, _ => true, CommandBarDescriptor.SearchPages["google"], pane, askMode);
    }

    private static void Ask(CommandBarViewModel bar, string question)
    {
        bar.Query = question;
        var row = bar.Rows.Single(r => r.Kind == QueryResultKind.AskAi);
        bar.SelectedIndex = bar.Rows.ToList().IndexOf(row);
        Assert.True(bar.RunSelected());
    }

    private sealed class FakeAskAi : IAskAi
    {
        private EventHandler? _setupChanged;

        public AiSetupProblem Problem { get; set; } = AiSetupProblem.None;

        public AiProvider Provider => AiProvider.Anthropic;

        public string ServiceName => "Claude";

        public List<Question> Asked { get; } = [];

        public Question Last => Asked[^1];

        public int Subscribers { get; private set; }

        public event EventHandler? SetupChanged
        {
            add
            {
                _setupChanged += value;
                Subscribers++;
            }
            remove
            {
                _setupChanged -= value;
                Subscribers--;
            }
        }

        public void RaiseSetupChanged() => _setupChanged?.Invoke(this, EventArgs.Empty);

        public Task<AiEvent> AskAsync(IReadOnlyList<AiTurn> turns, Action<AiEvent> onDelta, CancellationToken cancellationToken)
        {
            var question = new Question(turns.ToList(), onDelta, cancellationToken);
            Asked.Add(question);
            return question.Result.Task;
        }
    }

    private sealed record Question(IReadOnlyList<AiTurn> Turns, Action<AiEvent> OnDelta, CancellationToken Token)
    {
        public TaskCompletionSource<AiEvent> Result { get; } = new();

        public void Delta(string text) => OnDelta(AiEvent.Delta(text));

        /// <summary>From a thread with no UI, as the transport does, so the pane finishes before this returns.</summary>
        public void Finish(AiEvent result)
        {
            var thread = new Thread(() => Result.SetResult(result));
            thread.Start();
            thread.Join();
        }
    }

    private sealed class NoApps : IAppCatalog
    {
        public Task<IReadOnlyList<InstalledApp>> LoadAsync() => Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public void Launch(InstalledApp app)
        {
        }
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
