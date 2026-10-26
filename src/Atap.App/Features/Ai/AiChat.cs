using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Atap.Core.Ai;
using Atap.Core.LocalApi;
using Atap.Core.Localization;

namespace Atap.Features.Ai;

/// <summary>
/// One conversation, in memory only. The notch shows its last answer and the chat window shows all of it. Only
/// <see cref="Ask"/> sends. The answer is plain text, cleaned line by line with <see cref="TextSanitizer"/>.
/// Pieces of the answer are gathered and shown at most every 75 ms, and only the new text is cleaned, so a long
/// answer stays cheap. Each chat keeps its own earlier turns, at most 20.
/// </summary>
internal sealed class AiChat : ObservableObject, IDisposable
{
    /// <summary>Earlier turns kept for follow-ups, so a long talk does not grow without end.</summary>
    internal const int MaxTurns = 20;

    /// <summary>How often new pieces of the answer reach the screen, at most.</summary>
    internal static readonly TimeSpan RefreshEvery = TimeSpan.FromMilliseconds(75);

    /// <summary>How much of the first question the chat list shows as the title.</summary>
    private const int TitleLength = 40;

    private readonly IAiService _ai;
    private readonly ILocalizer _text;
    private readonly SynchronizationContext? _ui;
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

    // The question being answered. Only the UI thread uses it.
    private AiExchange? _current;
    private bool _disposed;

    /// <param name="ai">The running Ask AI.</param>
    /// <param name="text">UI text.</param>
    /// <param name="ui">Where answer pieces are shown. Inline when null, for tests.</param>
    /// <param name="copy">Puts an answer on the clipboard.</param>
    /// <param name="time">The clock for the 75 ms refresh. The system clock when null.</param>
    public AiChat(IAiService ai, ILocalizer text, SynchronizationContext? ui, Func<string, bool> copy, TimeProvider? time = null)
    {
        _ai = ai;
        _text = text;
        _ui = ui;
        _copy = copy;
        _time = time ?? TimeProvider.System;
        _ai.SetupChanged += OnSetupChanged;
    }

    /// <summary>Every question and its answer, oldest first.</summary>
    public ObservableCollection<AiExchange> Exchanges { get; } = [];

    /// <summary>The latest question and its answer, or null before the first.</summary>
    public AiExchange? Last => Exchanges.Count > 0 ? Exchanges[^1] : null;

    public bool IsEmpty => Exchanges.Count == 0;

    /// <summary>The start of the first question, or "New chat" before it.</summary>
    public string Title => Exchanges.Count == 0 ? _text["ai.chat.new"] : Shorten(Exchanges[0].QuestionLine);

    public bool IsAsking
    {
        get
        {
            lock (_gate)
                return _asking is not null;
        }
    }

    /// <summary>The earlier turns that go with the next question. For tests.</summary>
    internal IReadOnlyList<AiTurn> Turns => _turns;

    /// <summary>
    /// Sends <paramref name="question"/> with the earlier turns, and <paramref name="copied"/> as quoted data when
    /// given. A question already in flight stops first. False when nothing was sent.
    /// </summary>
    public bool Ask(string question, string? copied = null)
    {
        var asked = question.Trim();
        if (_disposed || asked.Length == 0 || _ai.Problem != AiSetupProblem.None)
            return false;
        StopCurrent();

        // An unfinished turn is not kept.
        if (_turns.Count > 0 && _turns[^1].Role == AiRole.User)
            _turns.RemoveAt(_turns.Count - 1);
        while (_turns.Count >= MaxTurns)
            _turns.RemoveRange(0, 2);
        _turns.Add(new AiTurn(AiRole.User, AiPrompt.Build(asked, copied)));

        var service = _ai.ServiceName;
        var exchange = new AiExchange(asked, service, _text, _copy)
        {
            IsAsking = true,
            StatusText = _text.Format("ai.asking", ("service", service)),
        };
        var asking = new CancellationTokenSource();
        lock (_gate)
        {
            _asking = asking;
            _refresh?.Dispose();
            _refresh = null;
            _pending.Clear();
            _answer.Clear();
            _cleaner.Clear();
        }
        _current = exchange;
        Exchanges.Add(exchange);
        OnPropertyChanged(nameof(Last));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsAsking));
        if (Exchanges.Count == 1)
            OnPropertyChanged(nameof(Title));
        _ = RunAsync(_turns.ToList(), asking);
        return true;
    }

    /// <summary>Stops the answer. What arrived stays on screen.</summary>
    public void Stop()
    {
        lock (_gate)
            _asking?.Cancel();
    }

    /// <summary>Stops the answer and forgets the conversation.</summary>
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
        _current = null;
        _turns.Clear();
    }

    /// <summary>The last question stops at once when a new one goes. What arrived of it stays on screen.</summary>
    private void StopCurrent()
    {
        CancellationTokenSource? asking;
        lock (_gate)
            asking = _asking;
        if (asking is null || _current is not { } exchange)
            return;
        Refresh(asking);
        lock (_gate)
        {
            asking.Cancel();
            _asking = null;
        }
        exchange.IsAsking = false;
        exchange.StatusText = _text["ai.error.stopped"];
        _current = null;
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
        string text;
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
            text = _cleaner.Text;
        }
        if (_current is { } exchange)
            exchange.AnswerText = text;
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
        if (_current is not { } exchange)
            return;
        _current = null;
        exchange.IsAsking = false;
        if (result.Kind == AiEventKind.Done)
        {
            _turns.Add(new AiTurn(AiRole.Assistant, answer));
            exchange.StatusText = result.Capped ? _text["ai.capped"] : string.Empty;
        }
        else
        {
            // The question stays out of the conversation, so a follow-up does not repeat it.
            if (_turns.Count > 0 && _turns[^1].Role == AiRole.User)
                _turns.RemoveAt(_turns.Count - 1);
            exchange.HasFailed = result.Error != AiError.Stopped;
            exchange.StatusText = AiText.Error(_text, result.Error, _ai.Provider);
        }
        OnPropertyChanged(nameof(IsAsking));
    }

    /// <summary>The service, key or model changed: the answer in flight stops.</summary>
    private void OnSetupChanged(object? sender, EventArgs e) => Stop();

    private void OnUi(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
            action();
        else
            _ui.Post(_ => action(), null);
    }

    private static string Shorten(string line) =>
        line.Length <= TitleLength ? line : line[..TitleLength].TrimEnd() + "...";

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

/// <summary>One question and its answer, as the notch and the chat window show them. Plain text only.</summary>
internal sealed class AiExchange : ObservableObject
{
    private readonly ILocalizer _text;
    private readonly Func<string, bool> _copy;
    private string _answer = string.Empty;
    private string _status = string.Empty;
    private bool _isAsking;
    private bool _hasFailed;
    private bool _justCopied;

    public AiExchange(string question, string serviceName, ILocalizer text, Func<string, bool> copy)
    {
        _text = text;
        _copy = copy;
        QuestionText = AiChat.Clean(question);
        QuestionLine = TextSanitizer.Sanitize(question);
        ServiceName = serviceName;
        CopyCommand = new RelayCommand(Copy, () => _answer.Length > 0);
    }

    /// <summary>The question as typed, with its line breaks, without any copied text.</summary>
    public string QuestionText { get; }

    /// <summary>The question on one line, for the notch and the chat list.</summary>
    public string QuestionLine { get; }

    /// <summary>The service that answered, such as "Claude".</summary>
    public string ServiceName { get; }

    public IRelayCommand CopyCommand { get; }

    public string CopyText => _text[_justCopied ? "ai.copiedDone" : "ai.copy"];

    /// <summary>The answer as plain text, cleaned line by line.</summary>
    public string AnswerText
    {
        get => _answer;
        internal set
        {
            if (!SetProperty(ref _answer, value))
                return;
            OnPropertyChanged(nameof(HasAnswer));
            CopyCommand.NotifyCanExecuteChanged();
        }
    }

    public bool HasAnswer => _answer.Length > 0;

    /// <summary>Asking, a cut answer, or what went wrong. Empty once a full answer is in. A screen reader reads it as it changes.</summary>
    public string StatusText
    {
        get => _status;
        internal set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => _status.Length > 0;

    public bool IsAsking
    {
        get => _isAsking;
        internal set => SetProperty(ref _isAsking, value);
    }

    /// <summary>True when the answer did not come, not counting Stop.</summary>
    public bool HasFailed
    {
        get => _hasFailed;
        internal set => SetProperty(ref _hasFailed, value);
    }

    private void Copy()
    {
        if (_answer.Length == 0 || !_copy(_answer))
            return;
        _justCopied = true;
        OnPropertyChanged(nameof(CopyText));
    }
}
