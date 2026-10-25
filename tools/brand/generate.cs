#:package SkiaSharp@4.153.1

// Builds every Atap brand file from the one SVG master, assets/brand/atap-icon.svg.
// Run from the repo root: dotnet run tools/brand/generate.cs
// The outputs are checked in. Run this again after the master changes and commit the results.
//
// Only SkiaSharp (MIT) is used. The master holds just <rect> and <path> elements with a fill,
// so a small reader below handles it. Svg.Skia is not used because it pulls in Svg.Custom (MS-PL).

using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SkiaSharp;

var root = FindRepoRoot();
var brandDir = Path.Combine(root, "assets", "brand");

// Every size, from the 16 px tray icon up, uses this one drawing, so the Tanggam joint shows everywhere.
var full = MasterSvg.Load(Path.Combine(brandDir, "atap-icon.svg"));

// Master fill colours, mapped to each tile-less mark variant.
const string MasterBeam = "#FAFAF9";
const string MasterBlock = "#2DD4BF";
(string File, string Beam, string Block)[] marks =
[
    ("atap-mark.svg", "#0C0A09", "#0F766E"),
    ("atap-mark-on-dark.svg", "#FAFAF9", "#2DD4BF"),
    ("atap-mark-mono-black.svg", "#000000", "#000000"),
    ("atap-mark-mono-white.svg", "#FFFFFF", "#FFFFFF"),
];
foreach (var (file, beam, block) in marks)
{
    var colours = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [MasterBeam] = beam,
        [MasterBlock] = block,
    };
    Write(Path.Combine(brandDir, file), Encoding.UTF8.GetBytes(full.ToMarkSvg(colours)));
}

// Plain PNGs of the icon with its tile, for docs, the website and anything else that needs a bitmap.
int[] pngSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024];
foreach (var size in pngSizes)
{
    using var image = full.Render(size, size, size, background: null);
    Write(Path.Combine(brandDir, "png", $"atap-{size}.png"), EncodePng(image));
}

// Windows app icon for the exe, taskbar, Start, tray and installer. 128 and 256 are stored as PNG,
// the rest as 32-bit bitmaps for older readers.
int[] icoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
Write(Path.Combine(root, "src", "Atap.App", "Assets", "atap.ico"), BuildIco(icoSizes, full));

// The same drawing as vector shapes for the app's own windows, so the logo in Settings stays sharp at any
// size and display scale instead of stretching a small icon image.
Write(Path.Combine(root, "src", "Atap.App", "Ui", "Theme", "Brand.xaml"), Encoding.UTF8.GetBytes(full.ToXamlDictionary("Brand.Logo")));

// Microsoft Store tiles: the icon centered on an ink background.
// IconShare is the icon edge as a share of the tile's shorter side. The master already has
// padding around the mark inside its tile, so the small logos use the whole square.
var ink = SKColor.Parse("#0C0A09");
(string Name, int Width, int Height, float IconShare)[] tiles =
[
    ("StoreLogo", 50, 50, 1.0f),
    ("Square44x44Logo", 44, 44, 1.0f),
    ("Square71x71Logo", 71, 71, 0.8f),
    ("Square150x150Logo", 150, 150, 0.6f),
    ("Wide310x150Logo", 310, 150, 0.6f),
    ("Square310x310Logo", 310, 310, 0.5f),
    ("SplashScreen", 620, 300, 0.5f),
];
int[] scales = [100, 200];
foreach (var (name, width, height, share) in tiles)
{
    foreach (var scale in scales)
    {
        var w = width * scale / 100;
        var h = height * scale / 100;
        var iconSize = (int)MathF.Round(Math.Min(w, h) * share);
        using var image = full.Render(w, h, iconSize, ink);
        Write(Path.Combine(brandDir, "store", $"{name}.scale-{scale}.png"), EncodePng(image));
    }
}

// Website icons from the same master, so the browser tab, the page header and the app match. They go to
// assets/brand/web, and from there into the website repo by hand.
var site = Path.Combine(brandDir, "web");
Write(Path.Combine(site, "favicon.svg"), File.ReadAllBytes(Path.Combine(brandDir, "atap-icon.svg")));
using (var image = full.Render(48, 48, 48, background: null))
    Write(Path.Combine(site, "favicon-48.png"), EncodePng(image));
using (var image = full.Render(180, 180, 180, ink))
    Write(Path.Combine(site, "apple-touch-icon.png"), EncodePng(image));
Write(Path.Combine(site, "og-image.png"), EncodePng(ShareImage(full)));

Console.WriteLine("Done.");
return;

// The 1200 x 630 picture shown when someone shares a link to the website: the notch at the top of a
// screen, then the icon, the name and the line from the home page. The times match the home page demo.
static SKImage ShareImage(MasterSvg master)
{
    const int W = 1200, H = 630;
    using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;
    using (var sky = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, H), [SKColor.Parse("#0A1230"), SKColor.Parse("#1B44B0")], SKShaderTileMode.Clamp) })
        canvas.DrawRect(0, 0, W, H, sky);

    // The notch, one shape with its curved shoulders.
    const float nw = 480, nh = 150, r = 32, s = 24, x0 = (W - nw) / 2;
    using var black = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using (var notch = new SKPath())
    {
        notch.MoveTo(x0 - s, 0);
        notch.LineTo(x0 + nw + s, 0);
        notch.ArcTo(new SKRect(x0 + nw, 0, x0 + nw + 2 * s, 2 * s), 270, -90, false);
        notch.LineTo(x0 + nw, nh - r);
        notch.ArcTo(new SKRect(x0 + nw - 2 * r, nh - 2 * r, x0 + nw, nh), 0, 90, false);
        notch.LineTo(x0 + r, nh);
        notch.ArcTo(new SKRect(x0, nh - 2 * r, x0 + 2 * r, nh), 90, 90, false);
        notch.LineTo(x0, s);
        notch.ArcTo(new SKRect(x0 - 2 * s, 0, x0, 2 * s), 0, -90, false);
        notch.Close();
        canvas.DrawPath(notch, black);
    }
    using var card = new SKPaint { Color = SKColor.Parse("#1C1C1C"), IsAntialias = true };
    canvas.DrawRoundRect(new SKRect(x0 + 24, 20, x0 + 284, 128), 20, 20, card);
    canvas.DrawRoundRect(new SKRect(x0 + 296, 20, x0 + nw - 24, 128), 20, 20, card);

    var regular = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
    var bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
    void Text(string text, float x, float y, SKTypeface face, float size, string colour)
    {
        using var font = new SKFont(face, size);
        using var paint = new SKPaint { Color = SKColor.Parse(colour), IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }
    Text("Now playing", x0 + 46, 62, bold, 24, "#A8A29E");
    Text("Lampu Kota", x0 + 46, 102, bold, 32, "#FAFAF9");
    Text("Asar", x0 + 318, 62, bold, 24, "#A8A29E");
    Text("16:19", x0 + 318, 106, bold, 42, "#A78BFA");

    using (var icon = master.Render(150, 150, 150, background: null))
        canvas.DrawImage(icon, 96, 300);
    Text("Atap", 286, 418, bold, 128, "#FAFAF9");
    Text("The top of your screen, alive.", 108, 520, bold, 46, "#FAFAF9");
    Text("Free notch for Windows 10 and 11. Open source.", 108, 570, regular, 30, "#A8A29E");
    return surface.Snapshot();
}

void Write(string path, byte[] bytes)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, bytes);
    Console.WriteLine($"  {Path.GetRelativePath(root, path).Replace('\\', '/')}");
}

static string FindRepoRoot()
{
    // dotnet run sets this for file-based apps. It is the folder that holds generate.cs.
    var scriptDir = AppContext.GetData("EntryPointFileDirectoryPath") as string
        ?? throw new InvalidOperationException("Run this with: dotnet run tools/brand/generate.cs");
    var root = Path.GetFullPath(Path.Combine(scriptDir, "..", ".."));
    if (!File.Exists(Path.Combine(root, "Atap.sln")))
        throw new InvalidOperationException($"Atap.sln not found in {root}.");
    return root;
}

static byte[] EncodePng(SKImage image)
{
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static byte[] BuildIco(int[] sizes, MasterSvg master)
{
    var images = new List<byte[]>();
    foreach (var size in sizes)
    {
        using var image = master.Render(size, size, size, background: null);
        images.Add(size >= 128 ? EncodePng(image) : EncodeIcoBitmap(image));
    }

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    // ICONDIR
    writer.Write((ushort)0); // reserved
    writer.Write((ushort)1); // type: icon
    writer.Write((ushort)sizes.Length);

    // ICONDIRENTRY per image. A width or height of 0 means 256.
    var offset = 6 + 16 * sizes.Length;
    for (var i = 0; i < sizes.Length; i++)
    {
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        writer.Write((byte)0);   // colour count: 0 for 32-bit
        writer.Write((byte)0);   // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32); // bits per pixel
        writer.Write(images[i].Length);
        writer.Write(offset);
        offset += images[i].Length;
    }

    foreach (var image in images)
        writer.Write(image);

    writer.Flush();
    return stream.ToArray();
}

// One icon image as a BITMAPINFOHEADER, 32-bit BGRA rows (bottom-up) and a 1-bit AND mask.
static byte[] EncodeIcoBitmap(SKImage image)
{
    var size = image.Width;
    var info = new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
    using var bitmap = new SKBitmap(info);
    if (!image.ReadPixels(bitmap.PeekPixels()))
        throw new InvalidOperationException($"Could not read the {size} px icon pixels.");
    var pixels = bitmap.GetPixelSpan();

    var maskStride = (size + 31) / 32 * 4;
    var colourBytes = size * size * 4;
    var maskBytes = maskStride * size;

    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);

    writer.Write(40);       // header size
    writer.Write(size);     // width
    writer.Write(size * 2); // height covers the colour rows and the mask rows
    writer.Write((ushort)1);  // planes
    writer.Write((ushort)32); // bits per pixel
    writer.Write(0);        // BI_RGB, no compression
    writer.Write(colourBytes + maskBytes);
    writer.Write(0);        // horizontal resolution
    writer.Write(0);        // vertical resolution
    writer.Write(0);        // colours used
    writer.Write(0);        // important colours

    for (var y = size - 1; y >= 0; y--)
        writer.Write(pixels.Slice(y * size * 4, size * 4));

    // AND mask: a set bit marks a fully transparent pixel.
    var row = new byte[maskStride];
    for (var y = size - 1; y >= 0; y--)
    {
        Array.Clear(row);
        for (var x = 0; x < size; x++)
        {
            if (pixels[(y * size + x) * 4 + 3] == 0)
                row[x / 8] |= (byte)(0x80 >> (x % 8));
        }
        writer.Write(row);
    }

    writer.Flush();
    return stream.ToArray();
}

/// <summary>A brand master: a square viewBox with filled rect and path shapes, nothing else.</summary>
sealed class MasterSvg
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    private MasterSvg(SKRect viewBox, List<Shape> shapes)
    {
        ViewBox = viewBox;
        Shapes = shapes;
    }

    public SKRect ViewBox { get; }

    public List<Shape> Shapes { get; }

    public static MasterSvg Load(string path)
    {
        var svg = XDocument.Load(path).Root
            ?? throw new InvalidDataException($"{path} is empty.");
        var box = Numbers(Attr(svg, "viewBox"));
        if (box.Length != 4 || box[2] != box[3])
            throw new InvalidDataException($"{path}: the viewBox must be square.");

        var shapes = new List<Shape>();
        foreach (var element in svg.Elements())
        {
            if (element.Name == Svg + "title")
                continue;

            var fill = Attr(element, "fill");
            if (element.Name == Svg + "rect")
            {
                var rect = SKRect.Create(Num(element, "x"), Num(element, "y"), Num(element, "width"), Num(element, "height"));
                var rx = element.Attribute("rx") is null ? 0 : Num(element, "rx");
                var ry = element.Attribute("ry") is null ? rx : Num(element, "ry");
                using var builder = new SKPathBuilder();
                builder.AddRoundRect(rect, rx, ry, SKPathDirection.Clockwise);
                shapes.Add(new Shape(builder.Detach(), fill, PathData: null, rect, rx, ry));
            }
            else if (element.Name == Svg + "path")
            {
                var data = Attr(element, "d");
                var shape = SKPath.ParseSvgPathData(data)
                    ?? throw new InvalidDataException($"{path}: could not read the path data \"{data}\".");
                shapes.Add(new Shape(shape, fill, data));
            }
            else
            {
                throw new InvalidDataException($"{path}: <{element.Name.LocalName}> is not supported. Use only <rect> and <path> with a fill.");
            }
        }

        return new MasterSvg(SKRect.Create(box[0], box[1], box[2], box[3]), shapes);
    }

    /// <summary>
    /// Draws the master as a square icon of <paramref name="iconSize"/> px, centered on a
    /// <paramref name="width"/> by <paramref name="height"/> image.
    /// </summary>
    public SKImage Render(int width, int height, int iconSize, SKColor? background)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info)
            ?? throw new InvalidOperationException($"Could not create a {width}x{height} surface.");
        var canvas = surface.Canvas;
        canvas.Clear(background ?? SKColors.Transparent);

        // Whole-pixel offsets keep the edges sharp.
        canvas.Translate((width - iconSize) / 2, (height - iconSize) / 2);
        canvas.Scale(iconSize / ViewBox.Width);
        canvas.Translate(-ViewBox.Left, -ViewBox.Top);

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        foreach (var shape in Shapes)
        {
            paint.Color = SKColor.Parse(shape.Fill);
            canvas.DrawPath(shape.Path, paint);
        }

        return surface.Snapshot();
    }

    /// <summary>The paths without the tile, recoloured, in a viewBox that fits them tightly.</summary>
    public string ToMarkSvg(IReadOnlyDictionary<string, string> colours)
    {
        var paths = Shapes.Where(s => s.PathData is not null).ToList();
        var bounds = paths[0].Path.TightBounds;
        foreach (var shape in paths.Skip(1))
            bounds.Union(shape.Path.TightBounds);

        var width = Format(bounds.Width);
        var height = Format(bounds.Height);
        var text = new StringBuilder();
        text.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{Format(bounds.Left)} {Format(bounds.Top)} {width} {height}\" width=\"{width}\" height=\"{height}\">\n");
        text.Append("  <title>Atap</title>\n");
        foreach (var shape in paths)
        {
            if (!colours.TryGetValue(shape.Fill, out var colour))
                throw new InvalidDataException($"No mark colour set for the master fill {shape.Fill}.");
            text.Append($"  <path d=\"{shape.PathData}\" fill=\"{colour}\"/>\n");
        }
        text.Append("</svg>\n");
        return text.ToString();
    }

    /// <summary>
    /// The icon as a WPF resource dictionary holding one DrawingImage under <paramref name="key"/>: the same
    /// shapes and colours, so the app draws the logo sharp at any size and display scale.
    /// </summary>
    public string ToXamlDictionary(string key)
    {
        var text = new StringBuilder();
        text.Append("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n");
        text.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n");
        text.Append("                    xmlns:po=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation/options\"\n");
        text.Append("                    xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"\n");
        text.Append("                    mc:Ignorable=\"po\">\n\n");
        text.Append("  <!-- The Atap logo. tools/brand/generate.cs writes this file from assets/brand/atap-icon.svg, so do not edit it by hand.\n");
        text.Append("       Shapes rather than an icon image, so it stays sharp at any size and display scale. It keeps its own colours\n");
        text.Append("       in every theme and in high contrast, like the icon on the taskbar. -->\n");
        text.Append($"  <DrawingImage x:Key=\"{key}\" po:Freeze=\"True\">\n");
        text.Append("    <DrawingImage.Drawing>\n");
        text.Append("      <DrawingGroup>\n");
        foreach (var shape in Shapes)
        {
            if (shape.PathData is not null)
            {
                text.Append($"        <GeometryDrawing Brush=\"{shape.Fill}\" Geometry=\"{shape.PathData}\" />\n");
                continue;
            }

            var box = shape.Box;
            text.Append($"        <GeometryDrawing Brush=\"{shape.Fill}\">\n");
            text.Append("          <GeometryDrawing.Geometry>\n");
            text.Append($"            <RectangleGeometry Rect=\"{Format(box.Left)},{Format(box.Top)},{Format(box.Width)},{Format(box.Height)}\" RadiusX=\"{Format(shape.Rx)}\" RadiusY=\"{Format(shape.Ry)}\" />\n");
            text.Append("          </GeometryDrawing.Geometry>\n");
            text.Append("        </GeometryDrawing>\n");
        }
        text.Append("      </DrawingGroup>\n");
        text.Append("    </DrawingImage.Drawing>\n");
        text.Append("  </DrawingImage>\n\n");
        text.Append("</ResourceDictionary>\n");
        return text.ToString();
    }

    private static string Attr(XElement element, string name) =>
        element.Attribute(name)?.Value
        ?? throw new InvalidDataException($"<{element.Name.LocalName}> has no {name} attribute.");

    private static float Num(XElement element, string name) =>
        float.Parse(Attr(element, name), CultureInfo.InvariantCulture);

    private static float[] Numbers(string value) =>
        value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(n => float.Parse(n, CultureInfo.InvariantCulture))
            .ToArray();

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>One filled shape. PathData is the original d attribute, or null for a rect, which keeps its box and corner radii.</summary>
sealed record Shape(SKPath Path, string Fill, string? PathData, SKRect Box = default, float Rx = 0, float Ry = 0);
