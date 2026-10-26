using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Ai;
using Atap.Core.Localization;
using Atap.Core.Notch;
using Atap.Notch;

namespace Atap.Features.Ai;

/// <summary>Opens and closes the chat window. Tests pass their own.</summary>
internal interface IAiChatShell : IDisposable
{
    bool IsOpen { get; }

    /// <summary>Shows the window. <paramref name="closed"/> runs once when it closes, however it closes.</summary>
    void Show(AiChatViewModel viewModel, Action closed);

    /// <summary>Brings the open window to the front.</summary>
    void Activate();

    void Close();
}

/// <summary>
/// Where Ask AI answers go. A question from the command bar shows in the notch, on its Answer surface, and stays
/// until Close, Esc while the pointer is on it, or Continue in chat. A follow-up from the bar while it shows goes
/// in the same conversation. Continue in chat moves that conversation into the chat window, where each chat keeps
/// its own. Chats live in memory only, so they go when Atap closes or Ask AI is turned off.
/// </summary>
internal sealed class AiAnswers : IAskAi, INotchInputHandler, IDisposable
{
    public const string Owner = AiDescriptor.FeatureId;

    private readonly IAiService _ai;
    private readonly ILocalizer _text;
    private readonly INotchControl _notch;
    private readonly IAiChatShell _shell;
    private readonly SynchronizationContext? _ui;
    private readonly Func<string, bool> _copy;
    private readonly TimeProvider? _time;
    private AiChatViewModel? _window;
    private bool _disposed;

    /// <param name="ai">The running Ask AI.</param>
    /// <param name="text">UI text.</param>
    /// <param name="notch">Where the answer shows.</param>
    /// <param name="shell">Opens the chat window.</param>
    /// <param name="ui">Where answer pieces are shown. Inline when null, for tests.</param>
    /// <param name="copy">Puts an answer on the clipboard.</param>
    /// <param name="time">The clock for the 75 ms refresh. The system clock when null.</param>
    public AiAnswers(IAiService ai, ILocalizer text, INotchControl notch, IAiChatShell shell, SynchronizationContext? ui, Func<string, bool> copy, TimeProvider? time = null)
    {
        _ai = ai;
        _text = text;
        _notch = notch;
        _shell = shell;
        _ui = ui;
        _copy = copy;
        _time = time;
    }

    public event EventHandler? SetupChanged
    {
        add => _ai.SetupChanged += value;
        remove => _ai.SetupChanged -= value;
    }

    public AiSetupProblem Problem => _ai.Problem;

    public string ServiceName => _ai.ServiceName;

    /// <summary>The answer in the notch, or null when none shows.</summary>
    public AiNotchAnswer? Shown { get; private set; }

    /// <summary>The chats in the chat window, newest first.</summary>
    public ObservableCollection<AiChat> Chats { get; } = [];

    /// <summary>The chat window while it is open.</summary>
    public AiChatViewModel? Window => _window;

    /// <summary>A question from the command bar. The answer shows in the notch. False when nothing was sent.</summary>
    public bool Ask(string question, string? copied)
    {
        if (_disposed || question.Trim().Length == 0 || _ai.Problem != AiSetupProblem.None)
            return false;
        if (Shown is null)
        {
            Shown = new AiNotchAnswer(NewChat(), CloseAnswer, ContinueInChat);
            if (!Shown.Chat.Ask(question, copied))
            {
                Shown.Detach().Dispose();
                Shown = null;
                return false;
            }
            _notch.SetSurfaceContent(NotchSurface.Answer, Shown, Owner);
            return true;
        }
        return Shown.Chat.Ask(question, copied);
    }

    /// <summary>Close in the notch, or Esc on it: stops the answer and forgets that conversation.</summary>
    public void CloseAnswer()
    {
        if (Shown is not { } shown)
            return;
        Shown = null;
        _notch.SetSurfaceContent(NotchSurface.Answer, null, Owner);
        shown.Detach().Dispose();
    }

    /// <summary>Moves the notch conversation into the chat window, still answering if it was, and opens the window on it.</summary>
    public void ContinueInChat()
    {
        if (Shown is not { } shown)
            return;
        Shown = null;
        _notch.SetSurfaceContent(NotchSurface.Answer, null, Owner);
        var chat = shown.Detach();
        Chats.Insert(0, chat);
        OpenChat(chat);
    }

    /// <summary>Opens the chat window, or brings it to the front, on <paramref name="chat"/> or on a new chat.</summary>
    public void OpenChat(AiChat? chat = null)
    {
        if (_disposed)
            return;
        if (_window is { } open && _shell.IsOpen)
        {
            if (chat is not null)
                open.SelectedChat = chat;
            _shell.Activate();
            return;
        }
        _window = new AiChatViewModel(this, _text);
        if (chat is not null)
            _window.SelectedChat = chat;
        else
            _window.NewChat();
        var window = _window;
        _shell.Show(window, () => OnWindowClosed(window));
    }

    /// <summary>A new empty chat for the chat window. An empty one at the top is used again.</summary>
    internal AiChat AddChat()
    {
        if (Chats.Count > 0 && Chats[0] is { IsEmpty: true } empty)
            return empty;
        var chat = NewChat();
        Chats.Insert(0, chat);
        return chat;
    }

    /// <summary>Esc while the pointer is on the answer puts it away.</summary>
    public bool TryHandle(NotchInput input)
    {
        if (input.Kind != NotchInputKind.Key || input.Surface != NotchSurface.Answer || input.Key != Key.Escape || Shown is null)
            return false;
        CloseAnswer();
        return true;
    }

    /// <summary>Stops every answer, closes the window and forgets every chat.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CloseAnswer();
        _shell.Dispose();
        _window = null;
        foreach (var chat in Chats)
            chat.Dispose();
        Chats.Clear();
    }

    private AiChat NewChat() => new(_ai, _text, _ui, _copy, _time);

    /// <summary>Closing the window stops every answer in it. The chats stay for next time, except empty ones.</summary>
    private void OnWindowClosed(AiChatViewModel window)
    {
        if (ReferenceEquals(_window, window))
            _window = null;
        window.Dispose();
        foreach (var chat in Chats)
            chat.Stop();
        foreach (var empty in Chats.Where(c => c.IsEmpty).ToList())
        {
            Chats.Remove(empty);
            empty.Dispose();
        }
    }
}

/// <summary>The answer in the notch: the latest question and answer of one conversation, with Continue in chat and Close.</summary>
internal sealed class AiNotchAnswer : ObservableObject
{
    private AiChat? _chat;

    public AiNotchAnswer(AiChat chat, Action close, Action continueInChat)
    {
        _chat = chat;
        chat.PropertyChanged += OnChatChanged;
        CloseCommand = new RelayCommand(close);
        ContinueCommand = new RelayCommand(continueInChat);
    }

    /// <summary>The conversation shown.</summary>
    public AiChat Chat => _chat ?? throw new ObjectDisposedException(nameof(AiNotchAnswer));

    /// <summary>The latest question and its answer.</summary>
    public AiExchange? Last => _chat?.Last;

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand ContinueCommand { get; }

    /// <summary>Stops following the conversation and hands it over.</summary>
    internal AiChat Detach()
    {
        var chat = Chat;
        chat.PropertyChanged -= OnChatChanged;
        _chat = null;
        return chat;
    }

    private void OnChatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiChat.Last))
            OnPropertyChanged(nameof(Last));
    }
}
