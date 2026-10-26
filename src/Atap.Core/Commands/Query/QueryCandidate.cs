namespace Atap.Core.Commands.Query;

/// <summary>Where a command bar row comes from. On equal scores the lower value comes first.</summary>
public enum QueryResultKind
{
    /// <summary>A calculator result. Always at the top, with no text matching.</summary>
    Calculation,

    /// <summary>A unit conversion. At the top after calculations, with no text matching.</summary>
    Conversion,

    /// <summary>An Atap command from the command registry.</summary>
    Command,

    /// <summary>An installed app.</summary>
    App,

    /// <summary>The Ask AI row. Right above web search, with no text matching. Only while Ask AI is on.</summary>
    AskAi,

    /// <summary>The web search row. Always the last row, with no text matching.</summary>
    WebSearch,
}

/// <summary>One row the command bar could show.</summary>
/// <param name="Id">Stable id within its kind, such as a command id or an AppsFolder parsing name.</param>
/// <param name="Kind">The provider.</param>
/// <param name="Title">The text shown and matched, in the UI language.</param>
public sealed record QueryCandidate(string Id, QueryResultKind Kind, string Title)
{
    /// <summary>More words that find this row, ranked a little below a title match of the same kind.</summary>
    public IReadOnlyList<string> Keywords { get; init; } = [];
}

/// <summary>A candidate that made the list, with its score from <see cref="QueryMatcher.Score"/>.</summary>
/// <param name="Candidate">The candidate.</param>
/// <param name="Score">
/// The match score. <see cref="QueryRanker.PinnedScore"/> for calculations and conversions, and 0 for web search.
/// </param>
public readonly record struct RankedCandidate(QueryCandidate Candidate, int Score);
