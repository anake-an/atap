using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atap.Core;
using Atap.Core.Layout;
using Atap.Core.Media;
using Atap.Core.Widgets;
using Atap.Notch;
using Atap.Tests.Infrastructure;
using Atap.Widgets.Media;

namespace Atap.Tests.Media;

/// <summary>The small notch while music plays: the title or lyric line, the album colour and the scroll.</summary>
public sealed class CompactMusicTests : IDisposable
{
    private readonly TestServices _services = new();

    public void Dispose() => _services.Dispose();

    [Fact]
    public void TheTitle_ShowsWithAMutedArtist_ByDefault()
    {
        using var model = Model();
        model.SetTrack("Lampu Kota", "Hujan Petang");
        model.LyricLine = "Di bawah lampu kota";

        Assert.Equal("Lampu Kota", model.CompactText);
        Assert.Equal(" · Hujan Petang", model.CompactDetail);
        Assert.False(model.ShowsLyricLine);
    }

    [Fact]
    public void TheLyricLine_ShowsWhenChosen_AndFallsBackToTheTitleWithoutOne()
    {
        using var model = Model();
        model.PrefersLyricLine = true;
        model.SetTrack("Lampu Kota", "Hujan Petang");

        // No lyrics, or the lyrics widget is off: the title as usual.
        Assert.Equal("Lampu Kota", model.CompactText);
        Assert.Equal(" · Hujan Petang", model.CompactDetail);

        model.LyricLine = "Di bawah lampu kota";
        Assert.True(model.ShowsLyricLine);
        Assert.Equal("Di bawah lampu kota", model.CompactText);
        Assert.Equal(string.Empty, model.CompactDetail);

        model.LyricLine = string.Empty;
        Assert.Equal("Lampu Kota", model.CompactText);
    }

    [Fact]
    public void NoArtist_LeavesOutTheDot()
    {
        using var model = Model();
        model.SetTrack("Lampu Kota", null);

        Assert.Equal(string.Empty, model.CompactDetail);
    }

    [Fact]
    public void ADarkCover_GivesALiftedAlbumColour() => Sta.Run(() =>
    {
        using var model = Model();

        model.AlbumArt = Cover(Color.FromRgb(40, 12, 64));

        Assert.True(model.HasAlbumColour);
        var colour = Assert.IsType<SolidColorBrush>(model.AlbumBrush).Color;
        Assert.True(model.AlbumBrush!.IsFrozen);
        Assert.True(AlbumColour.Lightness(new Rgb(colour.R, colour.G, colour.B)) >= AlbumColour.MinLightness - 0.01);
        Assert.True(colour.B > colour.G);
    });

    [Fact]
    public void NoCover_FallsBackToTheMusicAccent() => Sta.Run(() =>
    {
        using var model = Model();
        model.AlbumArt = Cover(Colors.Purple);

        model.AlbumArt = null;

        Assert.False(model.HasAlbumColour);
        Assert.Null(model.AlbumBrush);
    });

    [Theory]
    [InlineData(40, true, false, true, true)]
    [InlineData(0.5, true, false, true, false)]
    [InlineData(40, false, false, true, false)]
    [InlineData(40, true, true, true, false)]
    [InlineData(40, true, false, false, false)]
    public void TheTitle_ScrollsOnlyWhenTooLongShownAndAllowed(double overflow, bool shown, bool calm, bool windowsAnimates, bool scrolls)
    {
        Assert.Equal(scrolls, MediaMarquee.ShouldScroll(overflow, shown, calm, windowsAnimates));
    }

    [Fact]
    public void TheScroll_PausesAndKeepsACalmPace()
    {
        Assert.True(MediaMarquee.Speed <= 30);
        // 120 pixels too long: two seconds still, four seconds of scroll, a second and a half still.
        Assert.Equal(TimeSpan.FromSeconds(7.5), MediaMarquee.RoundFor(120));
    }

    [Fact]
    public void TheMarquee_StandsStillWhenNotShown() => Sta.Run(() =>
    {
        var marquee = new MediaMarquee { Child = new System.Windows.Controls.TextBlock { Text = new string('x', 200) } };
        marquee.Measure(new System.Windows.Size(50, 20));
        marquee.Arrange(new System.Windows.Rect(0, 0, 50, 20));

        Assert.False(marquee.IsScrolling);
    });

    [Fact]
    public void TheMiddle_ReportsWhenItIsLeftOut_SoNothingTicksForIt() => Sta.Run(() =>
    {
        using var model = Model();
        var middle = new System.Windows.Controls.Border { DataContext = model };
        MediaShownReport.SetIsOn(middle, true);
        var window = new System.Windows.Window
        {
            Content = middle,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = System.Windows.WindowStyle.None,
            Left = -20000,
            Top = -20000,
            Width = 10,
            Height = 10,
        };
        window.Show();
        try
        {
            Assert.True(model.IsCompactMiddleShown);

            // A side edge collapses the middle.
            middle.Visibility = System.Windows.Visibility.Collapsed;
            Assert.False(model.IsCompactMiddleShown);
        }
        finally
        {
            window.Close();
        }
    });

    [Fact]
    public void TheMiddleOfTheSmallNotch_OnlyTakesAViewMadeForIt()
    {
        var gap = new PlacementViewModel("battery", "battery", new object(), NotchState.Compact, WidgetSize.Small, PlacementSlot.Center) { IsGap = true };

        Assert.Equal(["battery.Compact.Center"], gap.TemplateKeys());
    }

    private MediaViewModel Model() =>
        new(_services.Text, () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask);

    private static BitmapSource Cover(Color colour)
    {
        var pixels = new byte[16 * 16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = colour.B;
            pixels[i + 1] = colour.G;
            pixels[i + 2] = colour.R;
            pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Pbgra32, null, pixels, 16 * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
