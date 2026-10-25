using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atap.Platform.Media;
using Atap.Tests.Infrastructure;
using Windows.Storage.Streams;

namespace Atap.Tests.Media;

public class AlbumArtTests
{
    [Fact]
    public Task Covers_AreSmallPixelsOnly_WithoutTheirImageFile() => Sta.RunAsync(async () =>
    {
        var file = Png(600, 600);
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(file.AsBuffer());
        stream.Seek(0);

        var art = await MediaSessionSource.LoadThumbnailAsync(RandomAccessStreamReference.CreateFromStream(stream));

        Assert.NotNull(art);
        Assert.Equal(128, art.PixelWidth);
        Assert.Equal(128, art.PixelHeight);
        Assert.True(art.IsFrozen);
        // A BitmapImage would keep the whole 600 px file in memory for as long as the cover shows.
        Assert.IsNotType<BitmapImage>(art);
    });

    [Fact]
    public void NoThumbnail_GivesNoCover() => Sta.Run(() =>
        Assert.Null(MediaSessionSource.LoadThumbnailAsync(null).GetAwaiter().GetResult()));

    [Fact]
    public void PixelsOnly_KeepsThePicture() => Sta.Run(() =>
    {
        var red = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, 8);

        var copy = MediaSessionSource.PixelsOnly(red);

        var pixels = new byte[8];
        copy.CopyPixels(pixels, 8, 0);
        Assert.Equal(new byte[] { 0, 0, 255, 255, 0, 0, 255, 255 }, pixels);
        Assert.Equal(PixelFormats.Pbgra32, copy.Format);
    });

    private static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)200);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = new MemoryStream();
        encoder.Save(file);
        return file.ToArray();
    }
}
