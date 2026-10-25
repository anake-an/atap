using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Atap.Core.Lyrics;

/// <summary>
/// Songs LRCLIB had no lyrics for, so they are not asked again for <see cref="Keep"/>. Songs are stored as hashes,
/// never as titles, and the list is capped.
/// </summary>
public sealed class LyricsMisses
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    public const int MaxEntries = 500;

    public Dictionary<string, DateTimeOffset> Until { get; set; } = [];

    /// <summary>A short hash of the title and artist, ignoring case and spaces at the ends.</summary>
    public static string Key(LyricsQuery query)
    {
        var text = $"{query.Title.Trim().ToUpperInvariant()}\n{query.Artist.Trim().ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 16);
    }

    public bool IsMissing(string key, DateTimeOffset now) => Until.TryGetValue(key, out var until) && until > now;

    public void Add(string key, DateTimeOffset now)
    {
        foreach (var expired in Until.Where(e => e.Value <= now).Select(e => e.Key).ToList())
            Until.Remove(expired);
        Until[key] = now + Keep;
        if (Until.Count > MaxEntries)
        {
            foreach (var oldest in Until.OrderBy(e => e.Value).Take(Until.Count - MaxEntries).Select(e => e.Key).ToList())
                Until.Remove(oldest);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LyricsMisses))]
public sealed partial class LyricsMissesJsonContext : JsonSerializerContext;
