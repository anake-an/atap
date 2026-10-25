using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public class LyricsMissesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AMiss_LastsSevenDays_AndKeysHideTheSong()
    {
        var misses = new LyricsMisses();
        var key = LyricsMisses.Key(new LyricsQuery(" Song ", "BAND"));

        misses.Add(key, Now);

        Assert.Equal(key, LyricsMisses.Key(new LyricsQuery("song", "band")));
        Assert.DoesNotContain("song", key, StringComparison.OrdinalIgnoreCase);
        Assert.True(misses.IsMissing(key, Now.AddDays(6)));
        Assert.False(misses.IsMissing(key, Now.AddDays(7)));
    }

    [Fact]
    public void TheList_IsCapped_AndExpiredEntriesGo()
    {
        var misses = new LyricsMisses();
        misses.Add("old", Now.AddDays(-8));
        for (var i = 0; i < LyricsMisses.MaxEntries + 5; i++)
            misses.Add($"k{i}", Now.AddSeconds(i));

        Assert.Equal(LyricsMisses.MaxEntries, misses.Until.Count);
        Assert.DoesNotContain("old", misses.Until.Keys);
        Assert.DoesNotContain("k0", misses.Until.Keys);
    }
}
