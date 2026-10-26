using Atap.Core.Lyrics;
using Atap.Core.Scheduling;
using Atap.Core.Shared;
using Microsoft.Extensions.Time.Testing;

namespace Atap.Core.Tests.Lyrics;

public sealed class LyricLineTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 21, 0, 0, TimeSpan.Zero));
    private readonly Scheduler _scheduler;
    private readonly SharedServices _shared;

    public LyricLineTests()
    {
        _scheduler = new Scheduler(_time);
        _shared = new SharedServices(new SynchronizationContext(), _time, _scheduler);
    }

    public void Dispose()
    {
        _shared.Dispose();
        _scheduler.Dispose();
    }

    [Fact]
    public void Publish_SharesTheLine_AndBlankClearsIt()
    {
        using var lease = _shared.Acquire<LyricLine>();
        var line = lease.Value;
        var changes = 0;
        line.Changed += (_, _) => changes++;

        line.Publish("Di bawah lampu kota");
        line.Publish("Di bawah lampu kota");
        Assert.Equal("Di bawah lampu kota", line.Text);
        Assert.Equal(1, changes);

        line.Publish("  ");
        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Want_IsHeldUntilEveryReaderLetsGo()
    {
        using var lease = _shared.Acquire<LyricLine>();
        var line = lease.Value;
        var changes = 0;
        line.WantedChanged += (_, _) => changes++;

        var first = line.Want();
        var second = line.Want();
        Assert.True(line.IsWanted);

        first.Dispose();
        first.Dispose();
        Assert.True(line.IsWanted);

        second.Dispose();
        Assert.False(line.IsWanted);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void BothWidgets_GetTheSameLine()
    {
        using var lyrics = _shared.Acquire<LyricLine>();
        using var music = _shared.Acquire<LyricLine>();

        lyrics.Value.Publish("one");

        Assert.Same(lyrics.Value, music.Value);
        Assert.Equal("one", music.Value.Text);
    }
}
