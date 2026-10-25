using System.Text.Json.Serialization;

namespace Atap.Core.Lyrics;

/// <summary>What a lyrics record holds.</summary>
public enum LyricsKind
{
    /// <summary>No words and not marked instrumental.</summary>
    None,

    /// <summary>The track has no words.</summary>
    Instrumental,

    /// <summary>Words without timing.</summary>
    Plain,

    /// <summary>Words with line timing, in LRC.</summary>
    Synced,
}

/// <summary>
/// One lyrics record from LRCLIB, in the shape <c>/api/get</c> and <c>/api/search</c> return.
/// Fields Atap does not use, such as <c>name</c>, <c>hasWordSync</c> and <c>lyricsfile</c>, are ignored.
/// </summary>
public sealed record LrclibTrack
{
    /// <summary>LRCLIB's stable record id. Always above 0 for a record from the parser.</summary>
    public long Id { get; init; }

    public string? TrackName { get; init; }

    public string? ArtistName { get; init; }

    public string? AlbumName { get; init; }

    /// <summary>Track length in seconds.</summary>
    public double? Duration { get; init; }

    public bool Instrumental { get; init; }

    public string? PlainLyrics { get; init; }

    /// <summary>LRC text. Read it with <see cref="LrcParser.Parse"/>.</summary>
    public string? SyncedLyrics { get; init; }

    /// <summary>
    /// What the record holds. Instrumental wins over any text. Synced lyrics count when they are not blank,
    /// so a widget should still fall back to plain lyrics when the LRC has no timed lines.
    /// </summary>
    [JsonIgnore]
    public LyricsKind Kind =>
        Instrumental ? LyricsKind.Instrumental
        : !string.IsNullOrWhiteSpace(SyncedLyrics) ? LyricsKind.Synced
        : !string.IsNullOrWhiteSpace(PlainLyrics) ? LyricsKind.Plain
        : LyricsKind.None;

    /// <summary>Shows the id and kind only. Lyrics and song details never reach a log.</summary>
    public override string ToString() => $"LrclibTrack {{ Id = {Id}, Kind = {Kind} }}";
}

/// <summary>The playing song, as the media session reports it, for a lyrics lookup.</summary>
/// <param name="Title">Track title. Required.</param>
/// <param name="Artist">Artist name. Required by <c>/api/get</c>, optional for <c>/api/search</c>.</param>
/// <param name="Album">Album name, when known. It makes <c>/api/get</c> more precise.</param>
/// <param name="Duration">Track length, when known. LRCLIB matches it within 2 seconds.</param>
public sealed record LyricsQuery(string Title, string Artist, string? Album = null, TimeSpan? Duration = null)
{
    /// <summary>Shows the duration only. Song details never reach a log.</summary>
    public override string ToString() => $"LyricsQuery {{ Duration = {Duration} }}";
}

/// <summary>Source-generated JSON metadata for LRCLIB records, so parsing needs no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(LrclibTrack))]
[JsonSerializable(typeof(List<LrclibTrack>))]
public sealed partial class LrclibJsonContext : JsonSerializerContext
{
}
