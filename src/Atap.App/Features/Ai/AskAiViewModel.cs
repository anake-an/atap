using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Ai;
using Atap.Core.LocalApi;
using Atap.Core.Localization;

namespace Atap.Features.Ai;

/// <summary>
/// The answer pane in the command bar. Only <see cref="Ask"/> sends, and the command bar calls it on Enter. The
/// conversation lives here, in memory, so it goes when the bar closes and this is disposed. The answer is plain
/// text, cleaned line by line with <see cref="TextSanitizer"/>. Copied text goes only while the box is ticked,
/// and the tick clears after each question.
/// </summary>
internal sealed class AskAiViewModel : ObservableObject, IDisposable
{
    /// <summary>Earlier turns kept for follow-ups, so a long talk does not grow without end.</summary>
    internal const int MaxTurns = 20;

    /// <summary>How much of the copied text the preview shows.</summary>
    private const int PreviewLength = 280;

    private readonly IAskAi _ai;
    private readonly ILocalizer _text;
    private readonly SynchronizationContext? _ui;
    private readonly Func<string?> _readClipboard;
    private readonly Func<string, bool> _copy;
    private readonly List<AiTurn> _turns = [];
    private readonly StringBuilder _answer = new();
    private CancellationTokenSource? _asking;
    private string? _answerText;
    private string? _question;
    private string? _lastQuestion;
    private string? _lastCopied;
    private bool _includeCopied;
    private string _copied = string.Empty;
    private bool _copiedCut;
    private Func<string>? _status;
    private bool _failed;
    private bool _justCopied;
    private bool _disposed;

    /// <param name="ai">The running Ask AI.</param>
    /// <param name="text">UI text.</param>
    /// <param name="ui">Where answer pieces are shown. Inline when null, for tests.</param>
    /// <param name="readClipboard">Reads the copied text, only when the box is ticked.</param>
    /// <param name="copy">Puts the answer on the clipboard.</param>
    public AskAiViewModel(IAskAi ai, ILocalizer text, SynchronizationContext? ui, Func<string?> readClipboard, Func<string, bool> copy)
    {
        _ai = ai;
        _text = text;
        _ui = ui;
        _readClipboard = readClipboard;
        _copy = copy;
        StopCommand = new RelayCommand(Stop, () => IsAsking);
        CopyCommand = new RelayCommand(Copy, () => AnswerText.Length > 0);
        AgainCommand = new RelayCommand(AskAgain, () => !IsAsking && _lastQuestion is not null);
        _ai.SetupChanged += OnSetupChanged;
    }

    public IRelayCommand StopCommand { get; }

    public IRelayCommand CopyCommand { get; }

    public IRelayCommand AgainCommand { get; }

    /// <summary>What is missing before a question can go. The command bar then offers Settings instead.</summary>
    public AiSetupProblem Problem => _ai.Problem;

    public string ServiceName => _ai.ServiceName;

    /// <summary>True once a question was asked, so the pane shows.</summary>
    public bool HasConversation => _question is not null;

    public bool IsAsking => _asking is not null;

    /// <summary>The question being answered, as typed, without any copied text.</summary>
    public string QuestionText => _question is null ? string.Empty : _text.Format("ai.question", ("question", TextSanitizer.Sanitize(_question)));

    /// <summary>The answer as plain text, cleaned line by line.</summary>
    public string AnswerText => _answerText ??= Clean(_answer.ToString());

    /// <summary>Asking, done, or what went wrong. A screen reader reads it as it changes.</summary>
    public string StatusText => _status?.Invoke() ?? string.Empty;

    public bool HasFailed => _failed;

    public string CopyText => _text[_justCopied ? "ai.copiedDone" : "ai.copy"];

    /// <summary>Send what is on the clipboard with the next question. Reading it happens only when ticked.</summary>
    public bool IncludeCopied
    {
        get => _includeCopied;
        set
        {
            if (!SetProperty(ref _includeCopied, value))
                return;
            var raw = value ? _readClipboard() : null;
            _copied = AiPrompt.Copied(raw);
            _copiedCut = raw is not null && raw.Trim().Length > AiPrompt.MaxCopiedLength;
            OnPropertyChanged(nameof(CopiedPreview));
            OnPropertyChanged(nameof(HasCopiedPreview));
            OnPropertyChanged(nameof(CopiedNote));
            OnPropertyChanged(nameof(HasCopiedNote));
        }
    }

    /// <summary>The start of what would go with the question, while the box is ticked.</summary>
    public string CopiedPreview => !_includeCopied || _copied.Length == 0 ? string.Empty
        : Clean(_copied.Length > PreviewLength ? _copied[..PreviewLength] + "..." : _copied);

    public bool HasCopiedPreview => CopiedPreview.Length > 0;

    /// <summary>Nothing is copied, or only the first 8,000 characters go.</summary>
    public string CopiedNote => !_includeCopied ? string.Empty
        : _copied.Length == 0 ? _text["ai.copiedNone"]
        : _copiedCut ? _text["ai.copiedCut"]
        : string.Empty;

    public bool HasCopiedNote => CopiedNote.Length > 0;

    /// <summary>
    /// Sends <paramref name="question"/>, with the earlier turns and the copied text if the box is ticked. A
    /// question already in flight stops first. False when nothing was sent.
    /// </summary>
    public bool Ask(string question)
    {
        var asked = question.Trim();
        if (_disposed || asked.Length == 0 || _ai.Problem != AiSetupProblem.None)
            return false;
        var copied = _includeCopied && _copied.Length > 0 ? _copied : null;
        IncludeCopied = false;
        Send(asked, copied);
        return true;
    }

    /// <summary>Stops the answer. What arrived stays on screen.</summary>
    public void Stop() => _asking?.Cancel();

    /// <summary>Stops the answer and forgets the conversation. The command bar calls it when it closes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ai.SetupChanged -= OnSetupChanged;
        _asking?.Cancel();
        _asking = null;
        _turns.Clear();
        _answer.Clear();
        _copied = string.Empty;
        _lastCopied = null;
        _lastQuestion = null;
    }

    private void AskAgain()
    {
        if (_lastQuestion is { } question)
            Send(question, _lastCopied);
    }

    private void Send(string question, string? copied)
    {
        // A new question stops the last one. An unfinished turn is not kept.
        _asking?.Cancel();
        if (_turns.Count > 0 && _turns[^1].Role == AiRole.User)
            _turns.RemoveAt(_turns.Count - 1);
        while (_turns.Count >= MaxTurns)
            _turns.RemoveRange(0, 2);

        _lastQuestion = question;
        _lastCopied = copied;
        _question = question;
        _answer.Clear();
        _answerText = null;
        _failed = false;
        _justCopied = false;
        _turns.Add(new AiTurn(AiRole.User, AiPrompt.Build(question, copied)));
        var asking = new CancellationTokenSource();
        _asking = asking;
        var service = _ai.ServiceName;
        SetStatus(() => _text.Format("ai.asking", ("service", service)));
        RaiseAll();
        _ = RunAsync(_turns.ToList(), asking);
    }

    private async Task RunAsync(IReadOnlyList<AiTurn> turns, CancellationTokenSource asking)
    {
        AiEvent result;
        try
        {
            result = await _ai.AskAsync(turns, e => OnUi(() => Append(e, asking)), asking.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The transport never throws for network problems. Anything else ends this question only.
            result = AiEvent.Failed(AiError.Unexpected);
        }
        OnUi(() => Finish(result, asking));
    }

    private void Append(AiEvent piece, CancellationTokenSource asking)
    {
        if (!ReferenceEquals(_asking, asking) || piece.Kind != AiEventKind.Delta)
            return;
        _answer.Append(piece.Text);
        _answerText = null;
        OnPropertyChanged(nameof(AnswerText));
        CopyCommand.NotifyCanExecuteChanged();
    }

    private void Finish(AiEvent result, CancellationTokenSource asking)
    {
        asking.Dispose();
        if (!ReferenceEquals(_asking, asking))
            return;
        _asking = null;
        var service = _ai.ServiceName;
        if (result.Kind == AiEventKind.Done)
        {
            _turns.Add(new AiTurn(AiRole.Assistant, _answer.ToString()));
            var capped = result.Capped;
            SetStatus(() => capped
                ? _text["ai.capped"] + " " + _text.Format("ai.done", ("service", service))
                : _text.Format("ai.done", ("service", service)));
        }
        else
        {
            // The question stays out of the conversation, so a follow-up does not repeat it.
            if (_turns.Count > 0 && _turns[^1].Role == AiRole.User)
                _turns.RemoveAt(_turns.Count - 1);
            _failed = result.Error != AiError.Stopped;
            var error = result.Error;
            var provider = _ai.Provider;
            SetStatus(() => AiText.Error(_text, error, provider));
        }
        RaiseAll();
    }

    private void Copy()
    {
        if (AnswerText.Length == 0 || !_copy(AnswerText))
            return;
        _justCopied = true;
        OnPropertyChanged(nameof(CopyText));
    }

    /// <summary>The service, key or model changed: the answer in flight stops.</summary>
    private void OnSetupChanged(object? sender, EventArgs e) => Stop();

    private void SetStatus(Func<string> status) => _status = status;

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasConversation));
        OnPropertyChanged(nameof(IsAsking));
        OnPropertyChanged(nameof(QuestionText));
        OnPropertyChanged(nameof(AnswerText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasFailed));
        OnPropertyChanged(nameof(CopyText));
        StopCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        AgainCommand.NotifyCanExecuteChanged();
    }

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    /// <summary>Each line cleaned on its own, so line breaks stay and control and direction characters go.</summary>
    internal static string Clean(string text)
    {
        if (text.Length == 0)
            return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var cleaned = new StringBuilder(text.Length);
        var blank = 0;
        foreach (var line in lines)
        {
            var clean = TextSanitizer.Sanitize(line);
            if (clean.Length == 0)
            {
                // At most one empty line between paragraphs.
                if (++blank > 1 || cleaned.Length == 0)
                    continue;
            }
            else
            {
                blank = 0;
            }
            if (cleaned.Length > 0)
                cleaned.Append('\n');
            cleaned.Append(clean);
        }
        return cleaned.ToString().TrimEnd('\n');
    }
}
