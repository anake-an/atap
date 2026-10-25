using Atap.Tests.Infrastructure;
using Atap.Widgets.Media;

namespace Atap.Tests.Media;

/// <summary>The playing bars and the song progress bar move only when they should.</summary>
public sealed class MediaProgressTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Theory]
    [InlineData(true, true, false, true, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, true, false, false, false)]
    public void PlayingBars_MoveOnlyWhilePlayingShownAndAllowed(bool playing, bool shown, bool calm, bool windowsAnimates, bool moves)
    {
        Assert.Equal(moves, MediaPlayingBars.ShouldMove(playing, shown, calm, windowsAnimates));
    }

    [Fact]
    public void PlayingBars_StandStillWhenNotShown() => Sta.Run(() =>
    {
        // Never loaded into a window, so never shown: playing alone does not start them.
        var bars = new MediaPlayingBars { IsPlaying = true };

        Assert.False(bars.IsAnimating);
    });

    [Fact]
    public void ProgressBar_IsLeftOutWithoutASongLength()
    {
        using var model = new MediaViewModel(_services.Text, () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);

        model.SetProgress(0.4567);
        Assert.True(model.HasProgress);
        Assert.Equal(45.7, model.Progress);

        model.SetProgress(null);
        Assert.False(model.HasProgress);
        Assert.Equal(0, model.Progress);
    }
}
