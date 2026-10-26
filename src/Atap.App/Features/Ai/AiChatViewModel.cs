using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Ai;
using Atap.Core.Localization;

namespace Atap.Features.Ai;

/// <summary>
/// The chat window: the chats on the left and the chosen one on the right, with a box to type in. Only Send, or
/// Enter in the box, sends. Copied text never goes from here. Made when the window opens and dropped when it closes.
/// </summary>
internal sealed class AiChatViewModel : ObservableObject, IDisposable
{
    private readonly AiAnswers _answers;
    private readonly ILocalizer _text;
    private AiChat? _selected;
    private string _draft = string.Empty;
    private bool _disposed;

    public AiChatViewModel(AiAnswers answers, ILocalizer text)
    {
        _answers = answers;
        _text = text;
        SendCommand = new RelayCommand(() => Send(), () => _selected is not null && _draft.Trim().Length > 0 && _answers.Problem == AiSetupProblem.None);
        StopCommand = new RelayCommand(Stop, () => _selected?.IsAsking == true);
        NewChatCommand = new RelayCommand(NewChat);
        _answers.SetupChanged += OnSetupChanged;
    }

    /// <summary>Every chat, newest first.</summary>
    public ObservableCollection<AiChat> Chats => _answers.Chats;

    public AiChat? SelectedChat
    {
        get => _selected;
        set
        {
            var before = _selected;
            if (!SetProperty(ref _selected, value))
                return;
            if (before is not null)
                before.PropertyChanged -= OnChatChanged;
            if (value is not null)
                value.PropertyChanged += OnChatChanged;
            OnPropertyChanged(nameof(IsChatEmpty));
            RaiseCommands();
        }
    }

    /// <summary>True while the chosen chat has no question yet, so a hint shows in its place.</summary>
    public bool IsChatEmpty => _selected?.IsEmpty ?? true;

    /// <summary>What is typed in the box. Nothing goes until Send.</summary>
    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value ?? string.Empty))
                SendCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>What is missing before a question can go, in plain words, or empty when nothing is.</summary>
    public string SetupText => _answers.Problem == AiSetupProblem.None ? string.Empty : _text["ai.chat.setup"];

    public bool HasSetupText => SetupText.Length > 0;

    public IRelayCommand SendCommand { get; }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand NewChatCommand { get; }

    /// <summary>Sends what is typed in the chosen chat and clears the box. False when nothing was sent.</summary>
    public bool Send()
    {
        if (_disposed || _selected is not { } chat || !chat.Ask(_draft))
            return false;
        Draft = string.Empty;
        // A chat moves to the top of the list when it gets a new question.
        var index = Chats.IndexOf(chat);
        if (index > 0)
            Chats.Move(index, 0);
        return true;
    }

    public void Stop() => _selected?.Stop();

    /// <summary>Starts a new chat and chooses it.</summary>
    public void NewChat() => SelectedChat = _answers.AddChat();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _answers.SetupChanged -= OnSetupChanged;
        SelectedChat = null;
    }

    private void OnChatChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiChat.IsEmpty))
            OnPropertyChanged(nameof(IsChatEmpty));
        if (e.PropertyName is nameof(AiChat.IsAsking))
            RaiseCommands();
    }

    private void OnSetupChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(SetupText));
        OnPropertyChanged(nameof(HasSetupText));
        RaiseCommands();
    }

    private void RaiseCommands()
    {
        SendCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
