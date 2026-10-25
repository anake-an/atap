using Atap.Core.Media;

namespace Atap.Core.Tests.Media;

public class MediaMathTests
{
    [Theory]
    [InlineData(0, PlaybackState.None)]
    [InlineData(1, PlaybackState.Stopped)]
    [InlineData(2, PlaybackState.Changing)]
    [InlineData(3, PlaybackState.Stopped)]
    [InlineData(4, PlaybackState.Playing)]
    [InlineData(5, PlaybackState.Paused)]
    [InlineData(99, PlaybackState.None)]
    public void SessionStatus_MapsToPlaybackState(int status, PlaybackState state)
    {
        Assert.Equal(state, MediaStateMapper.FromSessionStatus(status));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Lagu Rindu ", "Lagu Rindu")]
    public void Text_IsTrimmed_AndBlankIsNothing(string? text, string? shown)
    {
        Assert.Equal(shown, MediaStateMapper.CleanText(text));
    }

    [Fact]
    public void OnlyPlaying_IsLive()
    {
        Assert.True((NowPlaying.Nothing with { State = PlaybackState.Playing }).IsPlaying);
        Assert.False((NowPlaying.Nothing with { State = PlaybackState.Paused }).IsPlaying);
        Assert.False(NowPlaying.Nothing.HasSession);
    }

    [Theory]
    [InlineData(36, 1, 38)]
    [InlineData(37, 1, 38)]
    [InlineData(37, -1, 36)]
    [InlineData(36, -1, 34)]
    [InlineData(50, 3, 56)]
    [InlineData(99, 1, 100)]
    [InlineData(100, 1, 100)]
    [InlineData(1, -1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(42, 0, 42)]
    [InlineData(150, -1, 98)]
    public void VolumeSteps_SnapToTheGrid_AndStayInRange(int percent, int steps, int next)
    {
        Assert.Equal(next, VolumeStep.Next(percent, steps));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(0.425f, 43)]
    [InlineData(1f, 100)]
    [InlineData(1.2f, 100)]
    [InlineData(-0.1f, 0)]
    public void Scalar_BecomesWholePercent(float scalar, int percent)
    {
        Assert.Equal(percent, VolumeStep.ToPercent(scalar));
    }
}
