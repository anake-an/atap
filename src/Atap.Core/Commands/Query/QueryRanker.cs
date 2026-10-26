namespace Atap.Core.Commands.Query;

/// <summary>Orders command bar rows for a query. The same query and candidates always give the same list.</summary>
/// <remarks>
/// Calculations and conversions come first, because a provider only offers one when the query parsed.
/// Commands and apps follow, if their title or a keyword matches, best score first.
/// Equal scores go by kind (commands before apps), then title without case, then title, then id, all ordinal.
/// The web search row is always last and always kept, even when the list is full. The Ask AI row, when
/// offered, is kept the same way, right above it.
/// Rows with the same kind and id appear once, at their best place.
/// </remarks>
public static class QueryRanker
{
    /// <summary>Rows the command bar shows by default, web search included.</summary>
    public const int DefaultMaxResults = 8;

    /// <summary>The score given to calculations and conversions, which skip text matching.</summary>
    public const int PinnedScore = int.MaxValue;

    private static readonly IComparer<RankedCandidate> RowOrder = Comparer<RankedCandidate>.Create(Compare);

    /// <summary>
    /// Ranks <paramref name="candidates"/> for <paramref name="query"/>. A blank query gives an empty list.
    /// </summary>
    /// <param name="query">The text typed.</param>
    /// <param name="candidates">Rows from every provider. Null entries are skipped.</param>
    /// <param name="maxResults">Most rows returned, web search and Ask AI included. At least 1.</param>
    public static IReadOnlyList<RankedCandidate> Rank(
        string? query, IEnumerable<QueryCandidate?> candidates, int maxResults = DefaultMaxResults)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);

        var prepared = QueryMatcher.Prepare(query);
        if (prepared is null)
            return [];

        var pinned = new List<RankedCandidate>();
        var matched = new List<RankedCandidate>();
        QueryCandidate? webSearch = null;
        QueryCandidate? askAi = null;

        foreach (var candidate in candidates)
        {
            if (candidate is null)
                continue;

            switch (candidate.Kind)
            {
                case QueryResultKind.Calculation or QueryResultKind.Conversion:
                    pinned.Add(new RankedCandidate(candidate, PinnedScore));
                    break;

                case QueryResultKind.WebSearch:
                    if (webSearch is null || string.CompareOrdinal(candidate.Id, webSearch.Id) < 0)
                        webSearch = candidate;
                    break;

                case QueryResultKind.AskAi:
                    if (askAi is null || string.CompareOrdinal(candidate.Id, askAi.Id) < 0)
                        askAi = candidate;
                    break;

                default:
                    var score = QueryMatcher.Score(prepared, candidate);
                    if (score > 0)
                        matched.Add(new RankedCandidate(candidate, score));
                    break;
            }
        }

        var room = Math.Max(0, maxResults - (webSearch is null ? 0 : 1) - (askAi is null ? 0 : 1));
        var results = new List<RankedCandidate>(maxResults);
        var seen = new HashSet<(QueryResultKind, string)>();
        foreach (var row in pinned.Order(RowOrder).Concat(matched.Order(RowOrder)))
        {
            if (results.Count >= room)
                break;
            if (seen.Add((row.Candidate.Kind, row.Candidate.Id)))
                results.Add(row);
        }

        if (askAi is not null)
            results.Add(new RankedCandidate(askAi, 0));
        if (webSearch is not null)
            results.Add(new RankedCandidate(webSearch, 0));
        return results;
    }

    private static int Compare(RankedCandidate a, RankedCandidate b)
    {
        var order = b.Score.CompareTo(a.Score);
        if (order != 0)
            return order;
        order = a.Candidate.Kind.CompareTo(b.Candidate.Kind);
        if (order != 0)
            return order;
        order = StringComparer.OrdinalIgnoreCase.Compare(a.Candidate.Title, b.Candidate.Title);
        if (order != 0)
            return order;
        order = string.CompareOrdinal(a.Candidate.Title, b.Candidate.Title);
        if (order != 0)
            return order;
        return string.CompareOrdinal(a.Candidate.Id, b.Candidate.Id);
    }
}
