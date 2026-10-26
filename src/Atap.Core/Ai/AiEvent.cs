namespace Atap.Core.Ai;

public enum AiRole
{
    User,
    Assistant,
}

/// <summary>One turn of the conversation. Kept in memory only while the command bar stays open.</summary>
public sealed record AiTurn(AiRole Role, string Text);

public enum AiEventKind
{
    /// <summary>More of the answer.</summary>
    Delta,

    /// <summary>The answer is complete.</summary>
    Done,
    Failed,
}

/// <summary>Why a question got no answer, or no full answer. Each one has plain words in Settings and the command bar.</summary>
public enum AiError
{
    None,
    KeyNotAccepted,
    OutOfCredit,
    LimitReached,
    ModelNotAvailable,
    ServiceBusy,
    NoAnswerInTime,
    Offline,
    CannotReachServer,

    /// <summary>Plain http to a public address, a redirect, or another host. Nothing was sent, or nothing more was read.</summary>
    NotAllowed,

    /// <summary>The service answered in a way Atap could not read.</summary>
    Unexpected,

    /// <summary>Stopped by the user, a new question, closing the bar or a change of key.</summary>
    Stopped,
}

/// <summary>
/// What the stream gave: a piece of text, the end, or a failure. Events never hold the key, the question or
/// anything the server said about an error, only the kind of error.
/// </summary>
/// <param name="Kind">Delta, Done or Failed.</param>
/// <param name="Text">The new text, for Delta. Empty otherwise.</param>
/// <param name="Error">Why, for Failed.</param>
/// <param name="Capped">For Done: the answer reached the size cap and the rest was not read.</param>
public sealed record AiEvent(AiEventKind Kind, string Text, AiError Error, bool Capped = false)
{
    public static AiEvent Done { get; } = new(AiEventKind.Done, string.Empty, AiError.None);

    public static AiEvent Delta(string text) => new(AiEventKind.Delta, text, AiError.None);

    public static AiEvent Failed(AiError error) => new(AiEventKind.Failed, string.Empty, error);
}

/// <summary>One request, for the log: the service, the HTTP status and the kind of error. Nothing else.</summary>
public sealed record AiAttempt(AiProvider Provider, int? StatusCode, AiError Error);
