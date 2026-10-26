using Atap.Core.Ai;
using Atap.Features.Ai;
using Atap.Platform.Apps;

namespace Atap.Tests.Ai;

/// <summary>An Ask AI service that keeps every question until the test finishes it.</summary>
internal sealed class FakeAiService : IAiService
{
    private EventHandler? _setupChanged;

    public AiSetupProblem Problem { get; set; } = AiSetupProblem.None;

    public AiProvider Provider => AiProvider.Anthropic;

    public string ServiceName => "Claude";

    public List<FakeQuestion> Asked { get; } = [];

    public FakeQuestion Last => Asked[^1];

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
        var question = new FakeQuestion(turns.ToList(), onDelta, cancellationToken);
        Asked.Add(question);
        return question.Result.Task;
    }
}

internal sealed record FakeQuestion(IReadOnlyList<AiTurn> Turns, Action<AiEvent> OnDelta, CancellationToken Token)
{
    public TaskCompletionSource<AiEvent> Result { get; } = new();

    public void Delta(string text) => OnDelta(AiEvent.Delta(text));

    /// <summary>From a thread with no UI, as the transport does, so the chat finishes before this returns.</summary>
    public void Finish(AiEvent result)
    {
        var thread = new Thread(() => Result.SetResult(result));
        thread.Start();
        thread.Join();
    }
}

/// <summary>A chat window that is only a flag.</summary>
internal sealed class FakeChatShell : IAiChatShell
{
    private Action? _closed;

    public AiChatViewModel? Shown { get; private set; }

    public bool IsOpen { get; private set; }

    public int Shows { get; private set; }

    public int Activations { get; private set; }

    public void Show(AiChatViewModel viewModel, Action closed)
    {
        Shown = viewModel;
        IsOpen = true;
        Shows++;
        _closed = closed;
    }

    public void Activate() => Activations++;

    public void Close()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        var closed = _closed;
        _closed = null;
        closed?.Invoke();
    }

    public void Dispose() => Close();
}

internal sealed class NoApps : IAppCatalog
{
    public Task<IReadOnlyList<InstalledApp>> LoadAsync() => Task.FromResult<IReadOnlyList<InstalledApp>>([]);

    public void Launch(InstalledApp app)
    {
    }
}
