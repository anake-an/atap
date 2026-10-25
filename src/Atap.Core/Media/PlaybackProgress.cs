using Atap.Core.Lyrics;

namespace Atap.Core.Media;

/// <summary>How far through the song playback is, for the thin progress bar. Pure.</summary>
public static class PlaybackProgress
{
    /// <summary>
    /// The share of the song played at <paramref name="now"/>, from 0 to 1. Null when the player reports no length
    /// or no position, so the bar is left out instead of showing a guess.
    /// </summary>
    public static double? Fraction(PlaybackSnapshot? position, TimeSpan? duration, DateTimeOffset now)
    {
        if (position is not { } snapshot || duration is not { } length || length <= TimeSpan.Zero)
            return null;
        var played = snapshot.PositionAt(now);
        return Math.Clamp(played.Ticks / (double)length.Ticks, 0, 1);
    }

    /// <summary>True when the bar moves on its own, so a tick once a second is worth it.</summary>
    public static bool Moves(PlaybackSnapshot? position, TimeSpan? duration) =>
        position is { IsPlaying: true } && duration is { } length && length > TimeSpan.Zero;
}
