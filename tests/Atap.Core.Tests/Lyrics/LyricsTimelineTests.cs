using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class LyricsTimelineTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(8));

    // Lines at 10 s, 15 s, 15 s (a second line sharing the time), 20 s and 30 s.
    private static readonly LyricsTimeline Timeline = new(LrcParser.Parse("""
        [00:10.00]Satu
        [00:15.00]Dua
        [00:15.00]Dua, terjemahan
        [00:20.00]Tiga
        [00:30.00]
        """));

    [Theory]
    [InlineData(0, -1)]
    [InlineData(9_999, -1)]
    [InlineData(10_000, 0)]
    [InlineData(12_500, 0)]
    [InlineData(15_000, 1)]
    [InlineData(17_000, 1)]
    [InlineData(20_000, 3)]
    [InlineData(29_999, 3)]
    [InlineData(30_000, 4)]
    [InlineData(600_000, 4)]
    public void IndexAt_FindsTheLineStartedLast(int milliseconds, int index)
    {
        Assert.Equal(index, Timeline.IndexAt(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9_999, 0)]
    [InlineData(10_000, 1)]
    [InlineData(14_999, 1)]
    [InlineData(15_000, 3)]
    [InlineData(20_000, 4)]
    [InlineData(30_000, -1)]
    [InlineData(600_000, -1)]
    public void NextIndexAfter_FindsTheFirstLineStillToCome(int milliseconds, int index)
    {
        Assert.Equal(index, Timeline.NextIndexAfter(TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void LineAt_AndNextLineAfter_ReturnTheLines()
    {
        Assert.Null(Timeline.LineAt(TimeSpan.FromSeconds(5)));
        Assert.Equal("Satu", Timeline.NextLineAfter(TimeSpan.FromSeconds(5))?.Text);
        Assert.Equal("Dua", Timeline.LineAt(TimeSpan.FromSeconds(16))?.Text);
        Assert.Equal("Tiga", Timeline.NextLineAfter(TimeSpan.FromSeconds(16))?.Text);
        Assert.Null(Timeline.NextLineAfter(TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void EmptyTimeline_HasNoLines()
    {
        var timeline = new LyricsTimeline(LrcDocument.Empty);

        Assert.Equal(0, timeline.Count);
        Assert.Equal(-1, timeline.IndexAt(TimeSpan.FromSeconds(5)));
        Assert.Equal(-1, timeline.NextIndexAfter(TimeSpan.Zero));
        Assert.Equal(new LyricsCue(-1, -1, null), timeline.Cue(new PlaybackSnapshot(TimeSpan.Zero, Noon, true), Noon));
    }

    [Fact]
    public void UnsortedLines_AreSortedKeepingTheGivenOrderForEqualTimes()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(20), "Tiga"),
            new(TimeSpan.FromSeconds(10), "Satu"),
            new(TimeSpan.FromSeconds(20), "Tiga, terjemahan"),
        };

        var timeline = new LyricsTimeline(lines);
        lines.Clear();

        Assert.Equal(["Satu", "Tiga", "Tiga, terjemahan"], timeline.Lines.Select(l => l.Text));
        Assert.Equal(1, timeline.IndexAt(TimeSpan.FromSeconds(25)));
    }

    [Fact]
    public void Cue_WhilePlaying_GivesTheCurrentLineAndWhenTheNextStarts()
    {
        // The player said 12 s at noon. Two seconds later the position is 14 s, so line 1 starts in 1 s.
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(12), Noon, IsPlaying: true);

        var cue = Timeline.Cue(playback, Noon.AddSeconds(2));

        Assert.Equal(new LyricsCue(0, 1, Noon.AddSeconds(3)), cue);
    }

    [Fact]
    public void Cue_WhenTheTimerFires_MovesToTheNextLine()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(12), Noon, IsPlaying: true);
        var first = Timeline.Cue(playback, Noon);

        var second = Timeline.Cue(playback, first.NextAt!.Value);

        Assert.Equal(1, second.Index);
        Assert.Equal(3, second.NextIndex);
        Assert.Equal(Noon.AddSeconds(8), second.NextAt);
    }

    [Fact]
    public void Cue_WhilePaused_HasNoNextTime()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(12), Noon, IsPlaying: false);

        var cue = Timeline.Cue(playback, Noon.AddMinutes(5));

        Assert.Equal(new LyricsCue(0, 1, null), cue);
    }

    [Fact]
    public void Cue_AtDoubleSpeed_ArmsTheNextLineSooner()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(12), Noon, IsPlaying: true, Rate: 2);

        var cue = Timeline.Cue(playback, Noon.AddSeconds(1));

        // 14 s now, 1 s of song left until 15 s, which takes half a second at double speed.
        Assert.Equal(new LyricsCue(0, 1, Noon.AddSeconds(1.5)), cue);
    }

    [Fact]
    public void Cue_AfterASeek_ReSyncsFromTheNewPosition()
    {
        var before = Timeline.Cue(new PlaybackSnapshot(TimeSpan.FromSeconds(12), Noon, true), Noon);
        var seekBack = Timeline.Cue(new PlaybackSnapshot(TimeSpan.FromSeconds(2), Noon.AddSeconds(1), true), Noon.AddSeconds(1));
        var seekAhead = Timeline.Cue(new PlaybackSnapshot(TimeSpan.FromSeconds(25), Noon.AddSeconds(2), true), Noon.AddSeconds(2));

        Assert.Equal(0, before.Index);
        Assert.Equal(new LyricsCue(-1, 0, Noon.AddSeconds(9)), seekBack);
        Assert.Equal(new LyricsCue(3, 4, Noon.AddSeconds(7)), seekAhead);
    }

    [Fact]
    public void Cue_AfterTheLastLine_HasNothingToArm()
    {
        var cue = Timeline.Cue(new PlaybackSnapshot(TimeSpan.FromSeconds(31), Noon, true), Noon);

        Assert.Equal(new LyricsCue(4, -1, null), cue);
    }

    [Fact]
    public void PositionAt_MovesOnlyWhilePlaying()
    {
        var playing = new PlaybackSnapshot(TimeSpan.FromSeconds(30), Noon, IsPlaying: true);
        var paused = playing with { IsPlaying = false };

        Assert.Equal(TimeSpan.FromSeconds(40), playing.PositionAt(Noon.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(30), paused.PositionAt(Noon.AddSeconds(10)));
    }

    [Fact]
    public void PositionAt_ClockBeforeTheSample_NeverRewinds()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(30), Noon, IsPlaying: true);

        Assert.Equal(TimeSpan.FromSeconds(30), playback.PositionAt(Noon.AddSeconds(-5)));
    }

    [Fact]
    public void PositionAt_NegativePosition_CountsAsZero()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(-3), Noon, IsPlaying: true);

        Assert.Equal(TimeSpan.Zero, playback.PositionAt(Noon));
        Assert.Equal(TimeSpan.FromSeconds(1), playback.PositionAt(Noon.AddSeconds(1)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(double.NaN, 1)]
    [InlineData(double.PositiveInfinity, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(1000, 16)]
    [InlineData(0.0001, 0.0625)]
    public void EffectiveRate_FallsBackToNormalSpeedAndStaysInRange(double rate, double effective)
    {
        Assert.Equal(effective, new PlaybackSnapshot(TimeSpan.Zero, Noon, true, rate).EffectiveRate);
    }

    [Fact]
    public void DefaultSnapshot_PlaysAtNormalSpeed()
    {
        var playback = default(PlaybackSnapshot) with { IsPlaying = true, SampledAt = Noon };

        Assert.Equal(TimeSpan.FromSeconds(5), playback.PositionAt(Noon.AddSeconds(5)));
    }

    [Fact]
    public void WhenReaches_RoundsUpSoATimerNeverFiresEarly()
    {
        var playback = new PlaybackSnapshot(TimeSpan.Zero, Noon, IsPlaying: true, Rate: 3);

        var due = playback.WhenReaches(TimeSpan.FromTicks(10), Noon)!.Value;

        Assert.Equal(Noon.AddTicks(4), due);
        Assert.True(playback.PositionAt(due) >= TimeSpan.FromTicks(10));
    }

    [Fact]
    public void WhenReaches_APassedTarget_IsNow()
    {
        var playback = new PlaybackSnapshot(TimeSpan.FromSeconds(30), Noon, IsPlaying: true);

        Assert.Equal(Noon.AddSeconds(1), playback.WhenReaches(TimeSpan.FromSeconds(10), Noon.AddSeconds(1)));
    }

    [Fact]
    public void ExtremeValues_DoNotOverflow()
    {
        var huge = new PlaybackSnapshot(TimeSpan.MaxValue, DateTimeOffset.MinValue, IsPlaying: true, Rate: 16);

        Assert.Equal(TimeSpan.MaxValue, huge.PositionAt(DateTimeOffset.MaxValue));

        var slow = new PlaybackSnapshot(TimeSpan.Zero, DateTimeOffset.MaxValue.AddDays(-1), IsPlaying: true, Rate: 0.0625);
        Assert.Null(slow.WhenReaches(TimeSpan.FromDays(1), DateTimeOffset.MaxValue.AddDays(-1)));
    }
}
