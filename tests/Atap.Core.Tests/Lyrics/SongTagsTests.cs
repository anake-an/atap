using Atap.Core.Lyrics;

namespace Atap.Core.Tests.Lyrics;

public sealed class SongTagsTests
{
    [Fact]
    public void AppleMusic_ArtistAndAlbumInOneField_AreSplit()
    {
        var query = SongTags.Query("Kau Rumahku", "Raissa Anggiani — Renung Resah - EP", null, TimeSpan.FromSeconds(200));

        Assert.Equal("Kau Rumahku", query.Title);
        Assert.Equal("Raissa Anggiani", query.Artist);
        Assert.Equal("Renung Resah", query.Album);
        Assert.Equal(TimeSpan.FromSeconds(200), query.Duration);
    }

    [Fact]
    public void AReportedAlbum_IsKept_AndLosesItsReleaseSuffix()
    {
        var query = SongTags.Query("august", "Taylor Swift — folklore", "folklore - Single", null);

        Assert.Equal("Taylor Swift", query.Artist);
        Assert.Equal("folklore", query.Album);
    }

    [Theory]
    [InlineData("Daylight", "Daylight")]
    [InlineData("Stay (feat. Justin Bieber)", "Stay")]
    [InlineData("Let It Be - Remastered 2009", "Let It Be")]
    [InlineData("Yellow - Live", "Yellow")]
    [InlineData("Hello [Official Video]", "Hello")]
    [InlineData("Satu - Dua", "Satu - Dua")]
    [InlineData("(Live)", "(Live)")]
    public void PlainTitle_TakesOffOnlyTheExtras(string title, string plain) =>
        Assert.Equal(plain, SongTags.PlainTitle(title));

    [Fact]
    public void AnArtistWithAHyphen_IsLeftAlone()
    {
        var query = SongTags.Query("Song", "Jay-Z", "Album", null);

        Assert.Equal("Jay-Z", query.Artist);
        Assert.Equal("Album", query.Album);
    }
}
