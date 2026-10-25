namespace Atap.Core.Lyrics;

/// <summary>Where the lyrics stand at one moment.</summary>
/// <param name="Index">The line to show, or -1 before the first line.</param>
/// <param name="NextIndex">The line that comes next, or -1 when no line is left.</param>
/// <param name="NextAt">
/// When the next line starts. Null while paused or when no line is left. A widget arms one timer for it
/// and asks again when it fires or when the player reports a new position.
/// </param>
public readonly record struct LyricsCue(int Index, int NextIndex, DateTimeOffset? NextAt);

/// <summary>
/// Finds the current and the next lyric line for a song position. Lookups are binary searches.
/// The timeline owns no timer. Time is always passed in.
/// </summary>
public sealed class LyricsTimeline
{
    private readonly LrcLine[] _lines;

    /// <summary>Builds a timeline from lines. Lines out of time order are sorted, keeping the given order for equal times.</summary>
    public LyricsTimeline(IEnumerable<LrcLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        // A copy, so later changes to the caller's list cannot break the searches.
        var copy = lines.ToArray();
        if (!IsSorted(copy))
            copy = [.. copy.OrderBy(static line => line.Time)];

        _lines = copy;
        Lines = Array.AsReadOnly(copy);
    }

    /// <summary>Builds a timeline from a parsed LRC document.</summary>
    public LyricsTimeline(LrcDocument document)
        : this(document?.Lines ?? throw new ArgumentNullException(nameof(document)))
    {
    }

    /// <summary>The lines in time order.</summary>
    public IReadOnlyList<LrcLine> Lines { get; }

    /// <summary>Number of lines.</summary>
    public int Count => _lines.Length;

    /// <summary>
    /// Index of the line showing at <paramref name="position"/>: the last line that started at or before it.
    /// -1 before the first line. When several lines share that start time, the first of them.
    /// </summary>
    public int IndexAt(TimeSpan position)
    {
        var after = FirstAfter(position);
        return after == 0 ? -1 : FirstAt(_lines[after - 1].Time);
    }

    /// <summary>Index of the first line that starts after <paramref name="position"/>, or -1 when none is left.</summary>
    public int NextIndexAfter(TimeSpan position)
    {
        var after = FirstAfter(position);
        return after < _lines.Length ? after : -1;
    }

    /// <summary>The line showing at <paramref name="position"/>, or null before the first line.</summary>
    public LrcLine? LineAt(TimeSpan position) => IndexAt(position) is var i and >= 0 ? _lines[i] : null;

    /// <summary>The next line after <paramref name="position"/>, or null when none is left.</summary>
    public LrcLine? NextLineAfter(TimeSpan position) => NextIndexAfter(position) is var i and >= 0 ? _lines[i] : null;

    /// <summary>The current line, the next line and when it starts, for the playback state at <paramref name="now"/>.</summary>
    public LyricsCue Cue(PlaybackSnapshot playback, DateTimeOffset now)
    {
        var position = playback.PositionAt(now);
        var next = NextIndexAfter(position);
        var nextAt = next < 0 ? null : playback.WhenReaches(_lines[next].Time, now);
        return new LyricsCue(IndexAt(position), next, nextAt);
    }

    /// <summary>First index whose time is after <paramref name="position"/>, or the count when none is.</summary>
    private int FirstAfter(TimeSpan position)
    {
        int low = 0, high = _lines.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_lines[middle].Time <= position)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    /// <summary>First index whose time is <paramref name="time"/> or later.</summary>
    private int FirstAt(TimeSpan time)
    {
        int low = 0, high = _lines.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_lines[middle].Time < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static bool IsSorted(LrcLine[] lines)
    {
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Time < lines[i - 1].Time)
                return false;
        }

        return true;
    }
}
