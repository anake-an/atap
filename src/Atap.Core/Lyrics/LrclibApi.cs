using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Lyrics;

/// <summary>
/// Builds LRCLIB request addresses, reads its responses and picks the best search result. No I/O happens here.
/// The widget fetches through the shared HTTP service.
/// </summary>
public static class LrclibApi
{
    /// <summary>The only host lyrics lookups talk to.</summary>
    public const string Host = "lrclib.net";

    /// <summary>Longest duration LRCLIB accepts, in seconds. A longer one is left out of the request.</summary>
    public const int MaxDurationSeconds = 3600;

    /// <summary>Longest title, artist or album sent. Longer values are cut.</summary>
    public const int MaxFieldLength = 256;

    /// <summary>LRCLIB matches durations within 2 seconds. <see cref="PickBest"/> uses the same window.</summary>
    public static TimeSpan DurationTolerance { get; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The <c>/api/get</c> address for an exact match. Null when the title or artist is blank. The album is left out
    /// when blank, and the duration when unknown or outside 1 to 3600 seconds after rounding to whole seconds.
    /// The same query always gives the same address, so the HTTP cache can find it.
    /// </summary>
    public static Uri? GetUri(LyricsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Clean(query.Title) is not { } title || Clean(query.Artist) is not { } artist)
            return null;

        var builder = new StringBuilder("https://lrclib.net/api/get?");
        AddParameter(builder, "track_name", title);
        AddParameter(builder, "artist_name", artist);
        if (Clean(query.Album) is { } album)
            AddParameter(builder, "album_name", album);
        if (WholeSeconds(query.Duration) is { } seconds)
            AddParameter(builder, "duration", seconds.ToString(CultureInfo.InvariantCulture));
        return new Uri(builder.ToString());
    }

    /// <summary>
    /// The <c>/api/search</c> address, the fallback when <c>/api/get</c> finds nothing. Null when the title is blank.
    /// It sends the title and the artist only, so an album named differently on LRCLIB still matches.
    /// </summary>
    public static Uri? SearchUri(LyricsQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Clean(query.Title) is not { } title)
            return null;

        var builder = new StringBuilder("https://lrclib.net/api/search?");
        AddParameter(builder, "track_name", title);
        if (Clean(query.Artist) is { } artist)
            AddParameter(builder, "artist_name", artist);
        return new Uri(builder.ToString());
    }

    /// <summary>Reads one record from an <c>/api/get</c> body. Null for an error body, malformed JSON or a record without an id.</summary>
    public static LrclibTrack? ParseTrack(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return Valid(JsonSerializer.Deserialize(json, LrclibJsonContext.Default.LrclibTrack));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException comes from a lone surrogate, which cannot be read as UTF-8.
            return null;
        }
    }

    /// <inheritdoc cref="ParseTrack(string)"/>
    public static LrclibTrack? ParseTrack(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return Valid(JsonSerializer.Deserialize(utf8Json, LrclibJsonContext.Default.LrclibTrack));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the records from an <c>/api/search</c> body, in server order. Empty for anything that is not an array of records.</summary>
    public static IReadOnlyList<LrclibTrack> ParseSearch(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return ValidList(JsonSerializer.Deserialize(json, LrclibJsonContext.Default.ListLrclibTrack));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return [];
        }
    }

    /// <inheritdoc cref="ParseSearch(string)"/>
    public static IReadOnlyList<LrclibTrack> ParseSearch(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            return ValidList(JsonSerializer.Deserialize(utf8Json, LrclibJsonContext.Default.ListLrclibTrack));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Picks the search result to show, or null when none fits. Records with no lyrics are skipped, and so are
    /// records whose duration is more than <see cref="DurationTolerance"/> away from a known song length. A song length
    /// that <see cref="GetUri"/> leaves out, such as the 0 many browsers report, counts as unknown.
    /// Of the rest it prefers the same artist, then a record whose length matches the song. Among records that match,
    /// it prefers synced over plain over instrumental, then the same title. Without a matching length the same title
    /// comes first, because the timing of a live or other version would not fit. Last comes the closest duration.
    /// A tie keeps the server's order.
    /// </summary>
    public static LrclibTrack? PickBest(IEnumerable<LrclibTrack> candidates, LyricsQuery query)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(query);

        LrclibTrack? best = null;
        var bestScore = default(Score);
        foreach (var track in candidates)
        {
            if (track is null || track.Kind == LyricsKind.None)
                continue;

            var gap = DurationGap(track, query);
            if (gap > DurationTolerance.TotalSeconds)
                continue;

            var score = new Score(
                Same(track.ArtistName, query.Artist), gap is not null, track.Kind, Same(track.TrackName, query.Title), gap ?? 0);
            if (best is null || score.IsBetterThan(bestScore))
            {
                best = track;
                bestScore = score;
            }
        }

        return best;
    }

    private static LrclibTrack? Valid(LrclibTrack? track) => track is { Id: > 0 } ? track : null;

    private static LrclibTrack[] ValidList(List<LrclibTrack>? tracks) =>
        tracks is null ? [] : [.. tracks.Where(static track => track is { Id: > 0 })];

    /// <summary>Adds one escaped query parameter. The builder ends in <c>?</c> before the first one.</summary>
    private static void AddParameter(StringBuilder builder, string name, string value) =>
        builder.Append(builder[^1] == '?' ? "" : "&")
            .Append(name)
            .Append('=')
            .Append(Uri.EscapeDataString(value));

    /// <summary>Trims a field and cuts it to <see cref="MaxFieldLength"/>. Null when blank.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (trimmed.Length <= MaxFieldLength)
            return trimmed;

        // Never cut between the two halves of a surrogate pair.
        var length = char.IsHighSurrogate(trimmed[MaxFieldLength - 1]) ? MaxFieldLength - 1 : MaxFieldLength;
        return trimmed[..length].TrimEnd();
    }

    private static int? WholeSeconds(TimeSpan? duration)
    {
        if (duration is not { } value)
            return null;

        var seconds = Math.Round(value.TotalSeconds, MidpointRounding.AwayFromZero);
        return seconds is >= 1 and <= MaxDurationSeconds ? (int)seconds : null;
    }

    /// <summary>
    /// Seconds between the record and the song. Null when either length is unknown. The song's length is unknown by
    /// the same rule <see cref="GetUri"/> uses, so a length of 0 never rejects a record.
    /// </summary>
    private static double? DurationGap(LrclibTrack track, LyricsQuery query)
    {
        if (query.Duration is not { } wanted || WholeSeconds(wanted) is null)
            return null;
        if (track.Duration is not { } seconds || !double.IsFinite(seconds) || seconds <= 0)
            return null;
        return Math.Abs(seconds - wanted.TotalSeconds);
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>How well a record fits. Timed is true when both lengths are known and within <see cref="DurationTolerance"/>.</summary>
    private readonly record struct Score(bool SameArtist, bool Timed, LyricsKind Kind, bool SameTitle, double Gap)
    {
        public bool IsBetterThan(Score other)
        {
            // A cover by another artist never beats the right artist.
            if (SameArtist != other.SameArtist)
                return SameArtist;
            if (Timed != other.Timed)
                return Timed;

            // From here both are timed or both are not. A matching length means synced timing fits, so the lyrics
            // type leads. Without one the title leads. LyricsKind is declared from least to most useful.
            if (Timed && Kind != other.Kind)
                return Kind > other.Kind;
            if (SameTitle != other.SameTitle)
                return SameTitle;
            if (Kind != other.Kind)
                return Kind > other.Kind;
            return Gap < other.Gap;
        }
    }
}
