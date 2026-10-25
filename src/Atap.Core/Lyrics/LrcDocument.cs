using System.Collections.ObjectModel;

namespace Atap.Core.Lyrics;

/// <summary>One timed lyric line.</summary>
/// <param name="Time">When the line starts, from the start of the song, with the file's offset already applied.</param>
/// <param name="Text">The words, trimmed. Empty for a timed gap, such as an instrumental break or the end of the song.</param>
public sealed record LrcLine(TimeSpan Time, string Text)
{
    /// <summary>Shows the time only. Lyrics never reach a log.</summary>
    public override string ToString() => $"LrcLine {{ Time = {Time} }}";
}

/// <summary>A parsed LRC file: its timed lines in time order and its header tags.</summary>
public sealed class LrcDocument
{
    /// <summary>A document with no lines and no tags.</summary>
    public static LrcDocument Empty { get; } = new([], new Dictionary<string, string>(), TimeSpan.Zero);

    internal LrcDocument(IList<LrcLine> lines, IDictionary<string, string> tags, TimeSpan offset)
    {
        Lines = new ReadOnlyCollection<LrcLine>(lines);
        Tags = new ReadOnlyDictionary<string, string>(tags);
        Offset = offset;
    }

    /// <summary>Timed lines sorted by time. Lines that share a time keep their order in the file.</summary>
    public IReadOnlyList<LrcLine> Lines { get; }

    /// <summary>Header tags by lower-case name, such as <c>ti</c>, <c>ar</c>, <c>al</c>, <c>length</c> and <c>offset</c>.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>
    /// The <c>[offset:]</c> tag, already applied to <see cref="Lines"/>. A positive offset shows lines sooner.
    /// Zero when the tag is missing or not a whole number of milliseconds.
    /// </summary>
    public TimeSpan Offset { get; }

    /// <summary>True when there is at least one timed line.</summary>
    public bool HasLines => Lines.Count > 0;

    /// <summary>The <c>[ti:]</c> tag.</summary>
    public string? Title => Tag("ti");

    /// <summary>The <c>[ar:]</c> tag.</summary>
    public string? Artist => Tag("ar");

    /// <summary>The <c>[al:]</c> tag.</summary>
    public string? Album => Tag("al");

    /// <summary>The <c>[length:]</c> tag, or null when it is missing or not a time.</summary>
    public TimeSpan? Length => LrcParser.TryParseTime(Tag("length"), out var length) ? length : null;

    /// <summary>A tag value by name, or null. Names are matched in lower case.</summary>
    public string? Tag(string name) =>
        Tags.TryGetValue(name.ToLowerInvariant(), out var value) ? value : null;
}
