namespace Atap.Core.Media;

/// <summary>Where the current media session is.</summary>
public enum PlaybackState
{
    /// <summary>No session, or it closed.</summary>
    None,

    Stopped,

    Paused,

    Playing,

    /// <summary>Between tracks or loading.</summary>
    Changing,
}

/// <summary>What Windows' current media session plays, as the media widget and the media commands see it.</summary>
public sealed record NowPlaying(bool HasSession, string? Title, string? Artist, PlaybackState State, bool CanPrevious, bool CanNext)
{
    public static NowPlaying Nothing { get; } = new(false, null, null, PlaybackState.None, false, false);

    /// <summary>Playing right now. The notch shows music as live only then.</summary>
    public bool IsPlaying => State == PlaybackState.Playing;
}

/// <summary>Turns what Windows reports about a media session into <see cref="NowPlaying"/> values.</summary>
public static class MediaStateMapper
{
    /// <summary>
    /// The state for Windows' <c>GlobalSystemMediaTransportControlsSessionPlaybackStatus</c>: Closed 0, Opened 1,
    /// Changing 2, Stopped 3, Playing 4, Paused 5. An app that opened but never played reads as stopped.
    /// </summary>
    public static PlaybackState FromSessionStatus(int status) => status switch
    {
        1 or 3 => PlaybackState.Stopped,
        2 => PlaybackState.Changing,
        4 => PlaybackState.Playing,
        5 => PlaybackState.Paused,
        _ => PlaybackState.None,
    };

    /// <summary>A title or artist as shown: trimmed, and null when an app sends only spaces or nothing.</summary>
    public static string? CleanText(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
