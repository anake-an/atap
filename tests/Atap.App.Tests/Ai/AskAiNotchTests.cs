using System.Windows.Input;
using Atap.Core.Ai;
using Atap.Core.Notch;
using Atap.Features.Ai;
using Atap.Notch;
using Atap.Tests.Infrastructure;

namespace Atap.Tests.Ai;

/// <summary>The answer in the notch and the chat window: what streams, what stops it, and what carries over.</summary>
public sealed class AskAiNotchTests : IDisposable
{
    private readonly TestServices _services = new();
    private readonly FakeAiService _ai = new();
    private readonly NotchControl _notch = new();
    private readonly FakeChatShell _shell = new();
    private readonly List<string> _copied = [];
    private readonly AiAnswers _answers;

    public AskAiNotchTests()
    {
        _answers = new AiAnswers(_ai, _services.Text, _notch, _shell, ui: null, v =>
        {
            _copied.Add(v);
            return true;
        }, _services.Time);
    }

    public void Dispose()
    {
        _answers.Dispose();
        _services.Dispose();
    }

    private AiNotchAnswer Shown => Assert.IsType<AiNotchAnswer>(_notch.GetSurfaceContent(NotchSurface.Answer));

    [Fact]
    public void TheAnswer_StreamsIntoTheNotch_AndAFollowUpCarriesTheEarlierTurns()
    {
        Assert.True(_answers.Ask("capital of Malaysia?", null));

        _ai.Last.Delta("Kuala ");
        _ai.Last.Delta("Lumpur");
        Assert.Equal(string.Empty, Shown.Last!.AnswerText);
        _services.Time.Advance(AiChat.RefreshEvery);
        Assert.Equal("Kuala Lumpur", Shown.Last!.AnswerText);
        _ai.Last.Finish(AiEvent.Done);

        Assert.False(Shown.Last.IsAsking);
        Assert.False(Shown.Last.HasStatus);
        Assert.Equal("Claude", Shown.Last.ServiceName);
        var first = Shown;
        Assert.True(_answers.Ask("and of Japan?", null));
        Assert.Same(first, Shown);
        Assert.Equal("and of Japan?", Shown.Last!.QuestionLine);
        Assert.Equal(
            [new AiTurn(AiRole.User, "capital of Malaysia?"), new AiTurn(AiRole.Assistant, "Kuala Lumpur"), new AiTurn(AiRole.User, "and of Japan?")],
            _ai.Last.Turns);
    }

    [Fact]
    public void Close_StopsTheAnswer_AndClearsTheNotch()
    {
        _answers.Ask("a long question", null);
        var question = _ai.Last;

        Shown.CloseCommand.Execute(null);

        Assert.True(question.Token.IsCancellationRequested);
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
        Assert.Null(_answers.Shown);
        Assert.Empty(_answers.Chats);
        question.Finish(AiEvent.Failed(AiError.Stopped));

        // The next question starts a new conversation.
        _answers.Ask("hello", null);
        Assert.Equal([new AiTurn(AiRole.User, "hello")], _ai.Last.Turns);
    }

    [Fact]
    public void Esc_OnTheAnswer_ClosesIt_ButOnlyThere()
    {
        _notch.AddInputHandler(_answers);
        _answers.Ask("q", null);

        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Key, NotchSurface.Expanded) { Key = Key.Escape }));
        Assert.False(_notch.Offer(new NotchInput(NotchInputKind.Key, NotchSurface.Answer) { Key = Key.Enter }));
        Assert.NotNull(_answers.Shown);

        Assert.True(_notch.Offer(new NotchInput(NotchInputKind.Key, NotchSurface.Answer) { Key = Key.Escape }));

        Assert.True(_ai.Last.Token.IsCancellationRequested);
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
    }

    [Fact]
    public void ANewQuestion_StopsTheLastOne_WhichIsNotKept()
    {
        _answers.Ask("first", null);
        var first = _ai.Last;
        first.Delta("half an ");
        var firstExchange = Shown.Last!;

        _answers.Ask("second", null);

        Assert.True(first.Token.IsCancellationRequested);
        Assert.Equal("half an", firstExchange.AnswerText);
        Assert.False(firstExchange.IsAsking);
        first.Finish(AiEvent.Failed(AiError.Stopped));
        Assert.True(Shown.Last!.IsAsking);
        Assert.Equal([new AiTurn(AiRole.User, "second")], _ai.Last.Turns);
    }

    [Fact]
    public void ChangingTheKey_StopsTheAnswer()
    {
        _answers.Ask("first", null);

        _ai.RaiseSetupChanged();

        Assert.True(_ai.Last.Token.IsCancellationRequested);
        _ai.Last.Finish(AiEvent.Failed(AiError.Stopped));
        Assert.Equal("Stopped.", Shown.Last!.StatusText);
        Assert.False(Shown.Last.HasFailed);
    }

    [Fact]
    public void AnError_ShowsPlainWords_AndIsLeftOutOfTheConversation()
    {
        _answers.Ask("hello", null);

        _ai.Last.Finish(AiEvent.Failed(AiError.OutOfCredit));

        Assert.True(Shown.Last!.HasFailed);
        Assert.Equal("Your Claude account is out of credit. Top it up on their site.", Shown.Last.StatusText);
        _answers.Ask("again", null);
        Assert.Equal([new AiTurn(AiRole.User, "again")], _ai.Last.Turns);
    }

    [Fact]
    public void WithoutAKey_NothingIsSent()
    {
        _ai.Problem = AiSetupProblem.NoKey;

        Assert.False(_answers.Ask("hello", null));

        Assert.Empty(_ai.Asked);
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
    }

    [Fact]
    public void TheAnswer_IsPlainText_WithLineBreaksKept_AndControlsTakenOut()
    {
        _answers.Ask("q", null);

        _ai.Last.Delta("First line‮ reversed\u0007\r\n\r\n\r\n\r\nSecond   line\n");
        _services.Time.Advance(AiChat.RefreshEvery);

        Assert.Equal("First line reversed\n\nSecond line", Shown.Last!.AnswerText);
    }

    [Fact]
    public void ManyTinyPieces_AreGathered_AndShownFast()
    {
        _answers.Ask("q", null);
        var exchange = Shown.Last!;
        var shown = 0;
        exchange.PropertyChanged += (_, e) => shown += e.PropertyName == nameof(AiExchange.AnswerText) ? 1 : 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var i = 0; i < 65_000; i++)
        {
            _ai.Last.Delta(i % 100 == 99 ? "\n" : "a");
            // A refresh every 1,000 pieces, as a fast stream past the 75 ms timer would get.
            if (i % 1_000 == 999)
                _services.Time.Advance(AiChat.RefreshEvery);
        }
        _ai.Last.Finish(AiEvent.Done);

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}.");
        Assert.True(shown < 100, $"Shown {shown} times.");
        Assert.Equal(650, exchange.AnswerText.Split('\n').Length);
        Assert.Equal(64_350, exchange.AnswerText.Count(c => c == 'a'));
    }

    [Fact]
    public void Copy_PutsTheAnswerOnTheClipboard()
    {
        _answers.Ask("q", null);
        _ai.Last.Delta("The answer");
        _services.Time.Advance(AiChat.RefreshEvery);

        Shown.Last!.CopyCommand.Execute(null);

        Assert.Equal(["The answer"], _copied);
        Assert.Equal("Copied", Shown.Last.CopyText);
    }

    [Fact]
    public void ContinueInChat_CarriesTheConversation_StillAnswering()
    {
        _answers.Ask("capital of Malaysia?", null);
        _ai.Last.Delta("Kuala Lumpur");
        _services.Time.Advance(AiChat.RefreshEvery);
        _ai.Last.Finish(AiEvent.Done);
        _answers.Ask("and its population?", null);
        var running = _ai.Last;

        Shown.ContinueCommand.Execute(null);

        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
        Assert.False(running.Token.IsCancellationRequested);
        var window = _shell.Shown!;
        var chat = Assert.Single(_answers.Chats);
        Assert.Same(chat, window.SelectedChat);
        Assert.Equal(2, chat.Exchanges.Count);
        Assert.Equal("capital of Malaysia?", chat.Title);
        running.Delta("About 2 million.");
        _services.Time.Advance(AiChat.RefreshEvery);
        Assert.Equal("About 2 million.", chat.Last!.AnswerText);
        running.Finish(AiEvent.Done);

        window.Draft = "and Penang?";
        Assert.True(window.Send());
        Assert.Equal(string.Empty, window.Draft);
        Assert.Equal(5, _ai.Last.Turns.Count);
        Assert.Equal(new AiTurn(AiRole.User, "and Penang?"), _ai.Last.Turns[^1]);
    }

    [Fact]
    public void Chats_KeepTheirOwnConversations()
    {
        _answers.OpenChat();
        var window = _shell.Shown!;
        var first = window.SelectedChat!;
        window.Draft = "first chat";
        window.Send();
        _ai.Last.Delta("one");
        _services.Time.Advance(AiChat.RefreshEvery);
        _ai.Last.Finish(AiEvent.Done);

        window.NewChat();
        var second = window.SelectedChat!;
        Assert.NotSame(first, second);
        window.Draft = "second chat";
        window.Send();

        Assert.Equal([new AiTurn(AiRole.User, "second chat")], _ai.Last.Turns);
        Assert.Equal([second, first], _answers.Chats);
        _ai.Last.Finish(AiEvent.Done);

        window.SelectedChat = first;
        window.Draft = "more";
        window.Send();
        Assert.Equal(
            [new AiTurn(AiRole.User, "first chat"), new AiTurn(AiRole.Assistant, "one"), new AiTurn(AiRole.User, "more")],
            _ai.Last.Turns);
        // The chat with the newest question moves to the top.
        Assert.Same(first, _answers.Chats[0]);
    }

    [Fact]
    public void ClosingTheChatWindow_StopsARunningAnswer_AndKeepsTheChat()
    {
        _answers.OpenChat();
        var window = _shell.Shown!;
        window.Draft = "a long answer please";
        window.Send();
        var running = _ai.Last;
        window.NewChat();

        _shell.Close();

        Assert.True(running.Token.IsCancellationRequested);
        running.Finish(AiEvent.Failed(AiError.Stopped));
        // The empty new chat goes, the one with a question stays for next time.
        var kept = Assert.Single(_answers.Chats);
        Assert.Equal("Stopped.", kept.Last!.StatusText);
        Assert.Null(_answers.Window);
        // Only the kept chat still listens for a new key. The window and the empty chat let go.
        Assert.Equal(1, _ai.Subscribers);
    }

    [Fact]
    public void TheChatWindow_HasOneInstance()
    {
        _answers.OpenChat();
        _answers.OpenChat();

        Assert.Equal(1, _shell.Shows);
        Assert.Equal(1, _shell.Activations);
        Assert.Single(_answers.Chats);
    }

    [Fact]
    public void TheChatWindow_SendsNothing_UntilSend_AndNeverCopiedText()
    {
        _answers.OpenChat();
        var window = _shell.Shown!;

        window.Draft = "typing only";
        Assert.Empty(_ai.Asked);
        Assert.True(window.SendCommand.CanExecute(null));

        window.SendCommand.Execute(null);
        Assert.Equal("typing only", Assert.Single(_ai.Asked).Turns[0].Text);

        _ai.Problem = AiSetupProblem.NoKey;
        _ai.RaiseSetupChanged();
        window.Draft = "no key";
        Assert.False(window.SendCommand.CanExecute(null));
        Assert.Equal("Add your key for Ask AI in Settings first.", window.SetupText);
    }

    [Fact]
    public void TurningAskAiOff_ClosesEverything_AndForgetsTheChats()
    {
        _answers.Ask("in the notch", null);
        var inNotch = _ai.Last;
        _answers.OpenChat();
        _shell.Shown!.Draft = "in the chat";
        _shell.Shown.Send();
        var inChat = _ai.Last;

        _answers.Dispose();

        Assert.True(inNotch.Token.IsCancellationRequested);
        Assert.True(inChat.Token.IsCancellationRequested);
        Assert.False(_shell.IsOpen);
        Assert.Empty(_answers.Chats);
        Assert.Null(_notch.GetSurfaceContent(NotchSurface.Answer));
        Assert.Equal(0, _ai.Subscribers);
    }
}
