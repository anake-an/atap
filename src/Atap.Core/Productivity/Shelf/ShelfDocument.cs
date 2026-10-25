using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Productivity.Shelf;

/// <summary>
/// The files and folders on the shelf, newest first. Only the paths are kept, never the files themselves, in
/// machine storage because paths do not mean anything on another PC.
/// </summary>
public sealed record ShelfDocument
{
    public const int MaxItems = 30;

    public int Version { get; init; } = 1;

    public List<string> Paths { get; init; } = [];

    /// <summary>Adds the paths at the top. A path already on the shelf moves up. Blank and relative paths are ignored.</summary>
    public bool Add(IEnumerable<string> paths)
    {
        var changed = false;
        foreach (var path in paths.Reverse())
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path))
                continue;
            var full = System.IO.Path.TrimEndingDirectorySeparator(path.Trim());
            Paths.RemoveAll(p => p.Equals(full, StringComparison.OrdinalIgnoreCase));
            Paths.Insert(0, full);
            changed = true;
        }
        if (Paths.Count > MaxItems)
            Paths.RemoveRange(MaxItems, Paths.Count - MaxItems);
        return changed;
    }

    public bool Remove(string path) => Paths.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase)) > 0;

    public ShelfDocument Normalize()
    {
        var copy = new ShelfDocument { Version = Version };
        copy.Add(Paths);
        return copy;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(ShelfDocument))]
public sealed partial class ShelfJsonContext : JsonSerializerContext;
