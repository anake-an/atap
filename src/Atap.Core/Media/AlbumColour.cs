namespace Atap.Core.Media;

/// <summary>A colour as red, green and blue, 0 to 255 each.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// The main colour of an album cover, for the small notch's playing bars and line. Pure and cheap: one pass over
/// the small decoded cover. Bright, colourful pixels count more than grey ones, so a mostly dark cover with a
/// purple sky comes out purple and not a muddy grey.
/// </summary>
public static class AlbumColour
{
    /// <summary>The least lightness, 0 to 1, so the colour reads on the black notch.</summary>
    public const double MinLightness = 0.6;

    // Pixels more see-through than this are left out.
    private const byte MinAlpha = 128;

    /// <summary>
    /// The cover's main colour from its pixels, four bytes each in blue, green, red and alpha order, lifted so it
    /// reads on black. Null when there are no pixels to go by, so the caller keeps its usual accent.
    /// </summary>
    public static Rgb? From(ReadOnlySpan<byte> bgra)
    {
        double red = 0, green = 0, blue = 0, total = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < MinAlpha)
                continue;
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            // Colourful pixels weigh up to five times a grey one.
            var chroma = Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
            var weight = 1 + (4 * chroma / 255.0);
            red += r * weight;
            green += g * weight;
            blue += b * weight;
            total += weight;
        }
        if (total == 0)
            return null;
        return Lift(new Rgb((byte)Math.Round(red / total), (byte)Math.Round(green / total), (byte)Math.Round(blue / total)));
    }

    /// <summary>The same hue and saturation, with the lightness raised to at least <see cref="MinLightness"/>.</summary>
    public static Rgb Lift(Rgb colour)
    {
        var (hue, saturation, lightness) = ToHsl(colour);
        return lightness >= MinLightness ? colour : FromHsl(hue, saturation, MinLightness);
    }

    /// <summary>Lightness from 0 to 1, as in HSL.</summary>
    public static double Lightness(Rgb colour) => ToHsl(colour).Lightness;

    private static (double Hue, double Saturation, double Lightness) ToHsl(Rgb colour)
    {
        var r = colour.R / 255.0;
        var g = colour.G / 255.0;
        var b = colour.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta == 0)
            return (0, 0, lightness);
        var saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        var hue = max == r ? ((g - b) / delta) % 6
            : max == g ? ((b - r) / delta) + 2
            : ((r - g) / delta) + 4;
        hue *= 60;
        if (hue < 0)
            hue += 360;
        return (hue, Math.Min(1, saturation), lightness);
    }

    private static Rgb FromHsl(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = lightness - (chroma / 2);
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        return new Rgb(Channel(r + m), Channel(g + m), Channel(b + m));
    }

    private static byte Channel(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
