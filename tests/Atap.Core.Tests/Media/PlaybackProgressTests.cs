using Atap.Core.Lyrics;
using Atap.Core.Media;

namespace Atap.Core.Tests.Media;

public class PlaybackProgressTests
{
    private static readonly DateTimeOffset Sampled = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Song = TimeSpan.FromMinutes(4);

    [Fact]
    public void WhilePlaying_TheBarMovesWithTheClock()
    {
        var playing = new PlaybackSnapshot(TimeSpan.FromMinutes(1), Sampled, IsPlaying: true);

        Assert.Equal(0.25, PlaybackProgress.Fraction(playing, Song, Sampled));
        Assert.Equal(0.5, PlaybackProgress.Fraction(playing, Song, Sampled.AddMinutes(1)));
        Assert.True(PlaybackProgress.Moves(playing, Song));
    }

    [Fact]
    public void WhilePaused_TheBarStaysAndNeedsNoTick()
    {
        var paused = new PlaybackSnapshot(TimeSpan.FromMinutes(1), Sampled, IsPlaying: false);

        Assert.Equal(0.25, PlaybackProgress.Fraction(paused, Song, Sampled.AddMinutes(2)));
        Assert.False(PlaybackProgress.Moves(paused, Song));
    }

    [Fact]
    public void TheBar_StaysBetweenTheEnds()
    {
        var playing = new PlaybackSnapshot(TimeSpan.FromMinutes(1), Sampled, IsPlaying: true);
        var before = new PlaybackSnapshot(TimeSpan.FromSeconds(-5), Sampled, IsPlaying: false);

        Assert.Equal(1, PlaybackProgress.Fraction(playing, Song, Sampled.AddHours(1)));
        Assert.Equal(0, PlaybackProgress.Fraction(before, Song, Sampled));
    }

    [Fact]
    public void WithoutALengthOrAPosition_ThereIsNoBar()
    {
        var playing = new PlaybackSnapshot(TimeSpan.FromMinutes(1), Sampled, IsPlaying: true);

        Assert.Null(PlaybackProgress.Fraction(playing, null, Sampled));
        Assert.Null(PlaybackProgress.Fraction(playing, TimeSpan.Zero, Sampled));
        Assert.Null(PlaybackProgress.Fraction(null, Song, Sampled));
        Assert.False(PlaybackProgress.Moves(playing, null));
        Assert.False(PlaybackProgress.Moves(null, Song));
    }
}
