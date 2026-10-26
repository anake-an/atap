using System.Text.RegularExpressions;

namespace Atap.Core.Lyrics;

/// <summary>
/// Tidies what a music app reports before a lyrics lookup, pure. Music apps fill the fields in their own ways, so
/// the same song can be sent with extra words that make LRCLIB find nothing.
/// </summary>
public static partial class SongTags
{
    /// <summary>
    /// The lookup for the playing song. Apple Music puts "Artist — Album" in the artist field, so that is split when
    /// no album is reported. " - EP" and " - Single" come off the album.
    /// </summary>
    public static LyricsQuery Query(string title, string artist, string? album, TimeSpan? duration)
    {
        var name = artist.Trim();
        var record = string.IsNullOrWhiteSpace(album) ? null : album.Trim();
        if (SplitArtist(name) is { } split)
        {
            name = split.Artist;
            record ??= split.Album;
        }
        return new LyricsQuery(title.Trim(), name, record is null ? null : ReleaseSuffix().Replace(record, string.Empty).Trim(), duration);
    }

    /// <summary>
    /// The title without words LRCLIB rarely has, such as "(feat. Someone)", "(Remastered 2011)", " - Live" or
    /// "[Official Video]". The same title when there is nothing to take off.
    /// </summary>
    public static string PlainTitle(string title)
    {
        var plain = Extras().Replace(title, string.Empty);
        plain = DashExtras().Replace(plain, string.Empty).Trim();
        return plain.Length > 0 ? plain : title.Trim();
    }

    private static (string Artist, string Album)? SplitArtist(string artist)
    {
        foreach (var separator in (string[])[" — ", " – "])
        {
            var at = artist.IndexOf(separator, StringComparison.Ordinal);
            if (at <= 0)
                continue;
            var name = artist[..at].Trim();
            var album = artist[(at + separator.Length)..].Trim();
            if (name.Length > 0 && album.Length > 0)
                return (name, album);
        }
        return null;
    }

    [GeneratedRegex(@"\s+-\s+(EP|Single)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseSuffix();

    [GeneratedRegex(@"\s*[\(\[](feat\.?|ft\.?|featuring|with|remaster(ed)?|live|official|lyrics?|audio|video|explicit|clean|radio edit|mono|stereo)\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Extras();

    [GeneratedRegex(@"\s+-\s+(\d{4}\s+)?(remaster(ed)?|live|radio edit|mono|stereo|single version|album version)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DashExtras();
}
