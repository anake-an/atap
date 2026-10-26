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
/// text, cleaned line by line with <see cref="TextSanitizer"/>. Pieces of the answer are gathered and shown at
/// most every 75 ms, and only the new text is cleaned, so a long answer stays cheap. Copied text goes only while
/// the box is ticked, and the tick clears after each question.
/// </summary>
internal sealed class AskAiViewModel : ObservableObject, IDisposable
{
    /// <summary>Earlier turns kept for follow-ups, so a long talk does not grow without end.</summary>
    internal const int MaxTurns = 20;

    /// <summary>How often new pieces of the answer reach the screen, at most.</summary>
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromMilliseconds(75);

    /// <summary>How much of the copied text the preview shows.</summary>
    private const int PreviewLength = 280;

    private readonly IAskAi _ai;
    private readonly ILocalizer _text;
    private readonly SynchronizationContext? _ui;
    private readonly Func<string?> _readClipboard;
    private readonly Func<string, bool> _copy;
    private readonly TimeProvider _time;
    private readonly List<AiTurn> _turns = [];

    // Pieces arrive on a worker thread. Everything below the lock is shared with it.
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly StringBuilder _answer = new();
    private readonly AnswerCleaner _cleaner = new();
    private ITimer? _refresh;
    private CancellationTokenSource? _asking;
    private string? _answerText;

    private string? _question;
    private string? _lastQuestion;
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
    /// <param name="time">The clock for the 75 ms refresh. The system clock when null.</param>
    public AskAiViewModel(IAskAi ai, ILocalizer text, SynchronizationContext? ui, Func<string?> readClipboard, Func<string, bool> copy, TimeProvider? time = null)
    {
        _ai = ai;
        _text = text;
        _ui = ui;
        _readClipboard = readClipboard;
        _copy = copy;
        _time = time ?? TimeProvider.System;
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

    public bool IsAsking
    {
        get
        {
            lock (_gate)
                return _asking is not null;
        }
    }

    /// <summary>The question being answered, as typed, without any copied text.</summary>
    public string QuestionText => _question is null ? string.Empty : _text.Format("ai.question", ("question", TextSanitizer.Sanitize(_question)));

    /// <summary>The answer as plain text, cleaned line by line.</summary>
    public string AnswerText
    {
        get
        {
            lock (_gate)
                return _answerText ??= _cleaner.Text;
        }
    }

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
        Send(asked, TakeCopied());
        return true;
    }

    /// <summary>Stops the answer. What arrived stays on screen.</summary>
    public void Stop()
    {
        lock (_gate)
            _asking?.Cancel();
    }

    /// <summary>Stops the answer and forgets the conversation. The command bar calls it when it closes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _ai.SetupChanged -= OnSetupChanged;
        lock (_gate)
        {
            _asking?.Cancel();
            _asking = null;
            _refresh?.Dispose();
            _refresh = null;
            _pending.Clear();
            _answer.Clear();
            _cleaner.Clear();
        }
        _turns.Clear();
        _copied = string.Empty;
        _lastQuestion = null;
    }

    /// <summary>Sends the last question again. Copied text goes only if the box is ticked now.</summary>
    private void AskAgain()
    {
        if (_lastQuestion is { } question)
            Send(question, TakeCopied());
    }

    /// <summary>The copied text while the box is ticked, else null. The tick clears, so it goes once.</summary>
    private string? TakeCopied()
    {
        var copied = _includeCopied && _copied.Length > 0 ? _copied : null;
        IncludeCopied = false;
        return copied;
    }

    private void Send(string question, string? copied)
    {
        // A new question stops the last one. An unfinished turn is not kept.
        if (_turns.Count > 0 && _turns[^1].Role == AiRole.User)
            _turns.RemoveAt(_turns.Count - 1);
        while (_turns.Count >= MaxTurns)
            _turns.RemoveRange(0, 2);

        _lastQuestion = question;
        _question = question;
        _failed = false;
        _justCopied = false;
        _turns.Add(new AiTurn(AiRole.User, AiPrompt.Build(question, copied)));
        var asking = new CancellationTokenSource();
        lock (_gate)
        {
            _asking?.Cancel();
            _asking = asking;
            _refresh?.Dispose();
            _refresh = null;
            _pending.Clear();
            _answer.Clear();
            _cleaner.Clear();
            _answerText = null;
        }
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
            result = await _ai.AskAsync(turns, e => Gather(e, asking), asking.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The transport never throws for network problems. Anything else ends this question only.
            result = AiEvent.Failed(AiError.Unexpected);
        }
        OnUi(() => Finish(result, asking));
    }

    /// <summary>On the worker thread: keeps the piece, and asks for one refresh 75 ms on if none is waiting.</summary>
    private void Gather(AiEvent piece, CancellationTokenSource asking)
    {
        if (piece.Kind != AiEventKind.Delta || piece.Text.Length == 0)
            return;
        lock (_gate)
        {
            if (!ReferenceEquals(_asking, asking))
                return;
            _pending.Append(piece.Text);
            _refresh ??= _time.CreateTimer(_ => OnUi(() => Refresh(asking)), null, RefreshEvery, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Cleans only the new text and shows it.</summary>
    private void Refresh(CancellationTokenSource asking)
    {
        lock (_gate)
        {
            // A refresh for an earlier question leaves the current one's timer alone.
            if (!ReferenceEquals(_asking, asking))
                return;
            _refresh?.Dispose();
            _refresh = null;
            if (_pending.Length == 0)
                return;
            var added = _pending.ToString();
            _pending.Clear();
            _answer.Append(added);
            _cleaner.Add(added);
            _answerText = null;
        }
        OnPropertyChanged(nameof(AnswerText));
        CopyCommand.NotifyCanExecuteChanged();
    }

    private void Finish(AiEvent result, CancellationTokenSource asking)
    {
        // Whatever is still waiting shows before the end.
        Refresh(asking);
        string answer;
        lock (_gate)
        {
            if (!ReferenceEquals(_asking, asking))
            {
                asking.Dispose();
                return;
            }
            _asking = null;
            answer = _answer.ToString();
        }
        asking.Dispose();
        var service = _ai.ServiceName;
        if (result.Kind == AiEventKind.Done)
        {
            _turns.Add(new AiTurn(AiRole.Assistant, answer));
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
        var answer = AnswerText;
        if (answer.Length == 0 || !_copy(answer))
            return;
        _justCopied = true;
        OnPropertyChanged(nameof(CopyText));
    }

    /// <summary>The service, key or model changed: the answer in flight stops, and copied text is not kept for it.</summary>
    private void OnSetupChanged(object? sender, EventArgs e)
    {
        Stop();
        IncludeCopied = false;
    }

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
        var cleaner = new AnswerCleaner();
        cleaner.Add(text);
        return cleaner.Text;
    }

    /// <summary>
    /// Cleans text as it arrives. Finished lines are cleaned once and kept. Only the unfinished last line is
    /// cleaned again when more of it comes. At most one empty line stays between paragraphs.
    /// </summary>
    private sealed class AnswerCleaner
    {
        private readonly StringBuilder _done = new();
        private string _partial = string.Empty;
        private int _blank;

        public string Text
        {
            get
            {
                var last = TextSanitizer.Sanitize(_partial);
                if (last.Length == 0)
                    return _done.ToString().TrimEnd('\n');
                var text = new StringBuilder(_done.Length + last.Length + 1).Append(_done);
                if (text.Length > 0)
                    text.Append('\n');
                return text.Append(last).ToString();
            }
        }

        public void Add(string text)
        {
            if (text.Length == 0)
                return;
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                AddLine(_partial + text[start..i]);
                _partial = string.Empty;
                start = i + 1;
            }
            _partial += text[start..];
        }

        public void Clear()
        {
            _done.Clear();
            _partial = string.Empty;
            _blank = 0;
        }

        private void AddLine(string line)
        {
            // A line break of "\r\n" leaves a "\r" that the cleaning takes out as a control character.
            var clean = TextSanitizer.Sanitize(line);
            if (clean.Length == 0)
            {
                if (++_blank > 1 || _done.Length == 0)
                    return;
            }
            else
            {
                _blank = 0;
            }
            if (_done.Length > 0)
                _done.Append('\n');
            _done.Append(clean);
        }
    }
}
