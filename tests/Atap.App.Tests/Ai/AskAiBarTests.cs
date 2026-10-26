using System.Xml.Linq;
using Atap.Core.Ai;
using Atap.Core.Commands;
using Atap.Core.Commands.Query;
using Atap.Core.Notch;
using Atap.Features.Ai;
using Atap.Features.CommandBar;
using Atap.Features.Hotkeys;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Ai;

/// <summary>The Ask AI row in the command bar: what sends, what goes with it, and what the bar may hold.</summary>
public sealed class AskAiBarTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakeAiService _ai = new();
    private readonly NotchControl _notch = new();
    private readonly FakeChatShell _shell = new();
    private readonly AiAnswers _answers;
    private string? _clipboard;
    private int _clipboardReads;

    public AskAiBarTests()
    {
        _answers = new AiAnswers(_ai, _services.Text, _notch, _shell, ui: null, _ => true, _services.Time);
    }

    public void Dispose()
    {
        _answers.Dispose();
        _services.Dispose();
    }

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
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
    }

    [Fact]
    public void Enter_OnTheRow_Sends_ClosesTheBar_AndPutsTheAnswerInTheNotch()
    {
        using var bar = Bar();
        var closed = 0;
        var asked = 0;
        bar.Done += (_, _) => closed++;
        bar.Asked += (_, _) => asked++;
        bar.Query = "what is the capital of Malaysia";
        bar.SelectedIndex = bar.Rows.Count - 2;

        Assert.True(bar.RunSelected());

        var turns = Assert.Single(_ai.Asked).Turns;
        Assert.Equal([new AiTurn(AiRole.User, "what is the capital of Malaysia")], turns);
        Assert.Equal(0, closed);
        Assert.Equal(1, asked);
        var shown = Assert.IsType<AiNotchAnswer>(_notch.GetSurfaceContent(NotchSurface.Answer));
        Assert.Equal("what is the capital of Malaysia", shown.Last!.QuestionLine);
        Assert.True(shown.Last.IsAsking);
        Assert.Equal("Asking Claude", shown.Last.StatusText);
    }

    [Fact]
    public void ClosingTheBar_LeavesTheAnswerInTheNotch()
    {
        var bar = Bar();
        Ask(bar, "a long question");

        bar.Dispose();

        Assert.False(_ai.Last.Token.IsCancellationRequested);
        Assert.NotNull(_notch.GetSurfaceContent(NotchSurface.Answer));
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
        Assert.Equal("summarise this", _answers.Shown!.Last!.QuestionText);
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
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
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
    public void ChangingTheService_UnticksCopiedText()
    {
        _clipboard = "private notes";
        using var bar = Bar();
        bar.Ai!.IncludeCopied = true;

        _ai.RaiseSetupChanged();

        Assert.False(bar.Ai.IncludeCopied);
        Ask(bar, "first");
        Assert.DoesNotContain("private notes", _ai.Last.Turns[^1].Text);
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
    public async Task TheShortcut_OpensTheBarReadyToAsk_AndTheChatRowOpensTheChat()
    {
        var setting = new HotkeysDescriptor().Settings.Single(s => s.Key == HotkeysDescriptor.AskAiKey);
        Assert.Equal("hotkeys.ai.ask", setting.Key);
        Assert.Equal(string.Empty, setting.DefaultValue);
        Assert.Contains(HotkeysDescriptor.AskAiKey, HotkeysDescriptor.Keys);

        _services.Settings.Load();
        _services.Settings.Register(new CommandBarDescriptor().Settings);
        _services.Settings.Register(new AiDescriptor().Settings);
        _services.Settings.Register(new Atap.Features.NetworkPrivacy.NetworkPrivacyDescriptor().Settings);
        var barShell = new FakeBarShell();
        await using var bar = new CommandBarFeature(_services.Commands, _services.Text, _services.Settings, new NoApps(), barShell, () => AskAi.Current, () => _clipboard);
        using var transport = new AiTransport("test", _services.Network);
        var secrets = new Atap.Platform.Secrets.DpapiSecretStore(_services.Paths.SecretsFile);
        var chatShell = new FakeChatShell();
        await using var feature = new AiFeature(_services.Commands, _services.Settings, secrets, _services.Text, transport, (_, _) => { }, _notch, chatShell, null, _ => true);
        await bar.StartAsync();
        await feature.StartAsync();

        Assert.Same(feature.Answers, AskAi.Current);
        Assert.Equal(1, _notch.InputHandlerCount);
        Assert.True(await _services.Commands.TryRunAsync(AiDescriptor.AskCommand));
        Assert.True(barShell.Shown!.IsAskMode);
        Assert.NotNull(barShell.Shown.Ai);
        barShell.Close();

        // "Open Ask AI chat" is a row in the bar while Ask AI is on.
        Assert.True(_services.Commands.All.Single(c => c.Id == AiDescriptor.ChatCommand).ShowInCommandBar);
        Assert.True(await _services.Commands.TryRunAsync(AiDescriptor.ChatCommand));
        Assert.True(chatShell.IsOpen);

        await feature.DisposeAsync();
        Assert.Null(AskAi.Current);
        Assert.False(chatShell.IsOpen);
        Assert.Equal(0, _notch.InputHandlerCount);
        Assert.False(_services.Commands.Contains(AiDescriptor.AskCommand));
        Assert.False(_services.Commands.Contains(AiDescriptor.ChatCommand));
    }

    /// <summary>
    /// Answers are shown only as plain text. This fails if the command bar, the answer in the notch or the chat
    /// window gains anything that can parse, link or load what a service sends: links, rich text, web views,
    /// frames, images or XAML parsing.
    /// </summary>
    [Fact]
    public void TheViews_HoldNoLinksRichTextOrWebViews()
    {
        string[] views =
        [
            Path.Combine(RepoPaths.App, "Features", "CommandBar", "CommandBarWindow.xaml"),
            Path.Combine(RepoPaths.App, "Notch", "Surfaces", "Answer", "AnswerSurface.xaml"),
            Path.Combine(RepoPaths.App, "Features", "Ai", "AiChatWindow.xaml"),
        ];
        string[] refused = ["Hyperlink", "RichTextBox", "WebBrowser", "WebView2", "Frame", "FlowDocument", "FlowDocumentScrollViewer", "DocumentViewer", "Image", "MediaElement"];
        foreach (var file in views)
        {
            var names = XDocument.Load(file).Descendants().Select(e => e.Name.LocalName).ToHashSet(StringComparer.Ordinal);
            foreach (var name in refused)
                Assert.False(names.Contains(name), $"{RepoPaths.Relative(file)} holds a {name}.");
        }

        // The answers are plain text: a TextBlock in the notch, a read-only TextBox in the chat.
        var notchAnswer = Named(views[1], "TextBlock", "Answer");
        Assert.Contains("Last.AnswerText", (string?)notchAnswer.Attribute("Text"));
        var chatAnswer = Named(views[2], "TextBox", "Answer");
        Assert.Equal("True", (string?)chatAnswer.Attribute("IsReadOnly"));
        Assert.Contains("Mode=OneWay", (string?)chatAnswer.Attribute("Text"));

        var code = new[] { "CommandBarWindow.xaml.cs", "CommandBarViewModel.cs" }.Select(f => Path.Combine(RepoPaths.App, "Features", "CommandBar", f))
            .Concat(new[] { "AskAiViewModel.cs", "AiChat.cs", "AiAnswers.cs", "AiChatViewModel.cs", "AiChatWindow.xaml.cs" }.Select(f => Path.Combine(RepoPaths.App, "Features", "Ai", f)));
        foreach (var file in code)
        {
            var text = File.ReadAllText(file);
            foreach (var parser in new[] { "XamlReader", "Hyperlink", "RichTextBox", "WebBrowser", "FlowDocument", "BitmapImage" })
                Assert.False(text.Contains(parser, StringComparison.Ordinal), $"{RepoPaths.Relative(file)} uses {parser}.");
        }
    }

    private static XElement Named(string file, string type, string name) =>
        XDocument.Load(file).Descendants().Single(e => e.Name.LocalName == type
            && (string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == name);

    private CommandBarViewModel Bar(bool askMode = false)
    {
        var pane = new AskAiViewModel(_answers, _services.Text, () =>
        {
            _clipboardReads++;
            return _clipboard;
        });
        return new CommandBarViewModel(_services.Commands, _services.Text, new NoApps(), _ => { }, _ => true, CommandBarDescriptor.SearchPages["google"], pane, askMode);
    }

    private static void Ask(CommandBarViewModel bar, string question)
    {
        bar.Query = question;
        var row = bar.Rows.Single(r => r.Kind == QueryResultKind.AskAi);
        bar.SelectedIndex = bar.Rows.ToList().IndexOf(row);
        Assert.True(bar.RunSelected());
    }

    private sealed class FakeBarShell : ICommandBarShell
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
