using Atap.Core.Media;

namespace Atap.Core.Tests.Media;

public class AlbumColourTests
{
    [Fact]
    public void ADarkCover_IsLifted_SoItReadsOnBlack()
    {
        // A deep purple cover, far too dark for the black notch as it is.
        var colour = AlbumColour.From(Cover(new Rgb(48, 16, 72)));

        Assert.NotNull(colour);
        Assert.True(AlbumColour.Lightness(colour.Value) >= AlbumColour.MinLightness - 0.01);
        // Still purple: blue and red stay above green.
        Assert.True(colour.Value.B > colour.Value.G && colour.Value.R > colour.Value.G);
    }

    [Fact]
    public void ABrightCover_KeepsItsColour()
    {
        var orange = new Rgb(250, 180, 90);

        Assert.Equal(orange, AlbumColour.From(Cover(orange)));
    }

    [Fact]
    public void NoPixels_OrOnlySeeThroughOnes_GiveNoColour()
    {
        Assert.Null(AlbumColour.From([]));
        Assert.Null(AlbumColour.From(Cover(new Rgb(200, 0, 0), alpha: 0)));
    }

    [Fact]
    public void ColourfulPixels_CountMoreThanGreyOnes()
    {
        // Half grey, half purple: the result leans to purple instead of a plain mix.
        var pixels = Cover(new Rgb(90, 90, 90)).Concat(Cover(new Rgb(140, 40, 200))).ToArray();

        var colour = AlbumColour.From(pixels)!.Value;

        Assert.True(colour.B - colour.G > (200 - 40) / 2);
    }

    // 16 pixels of one colour, as blue, green, red and alpha.
    private static byte[] Cover(Rgb colour, byte alpha = 255) =>
        Enumerable.Repeat(new[] { colour.B, colour.G, colour.R, alpha }, 16).SelectMany(p => p).ToArray();
}
