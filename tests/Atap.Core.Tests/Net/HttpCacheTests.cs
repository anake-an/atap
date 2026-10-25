using System.Text;
using Atap.Core.Net;

namespace Atap.Core.Tests.Net;

public sealed class HttpCacheTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Atap.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Keys_AreHashesOfTheUrlAndHeaders()
    {
        var url = new Uri("https://example.com/data.json?q=1");
        var plain = HttpCache.KeyFor(new FetchRequest(url, TimeSpan.Zero));
        var withHeaders = HttpCache.KeyFor(new FetchRequest(url, TimeSpan.Zero) { Headers = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" } });
        var reordered = HttpCache.KeyFor(new FetchRequest(url, TimeSpan.Zero) { Headers = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" } });

        Assert.Matches("^[0-9a-f]{64}$", plain);
        Assert.Equal(plain, HttpCache.KeyFor(new FetchRequest(url, TimeSpan.FromHours(1))));
        Assert.NotEqual(plain, withHeaders);
        Assert.Equal(withHeaders, reordered);
        Assert.NotEqual(plain, HttpCache.KeyFor(new FetchRequest(new Uri("https://example.com/data.json?q=2"), TimeSpan.Zero)));
    }

    [Fact]
    public void Entries_RoundTrip_AndDamagedFilesAreRejected()
    {
        var entry = new CacheEntry(Encoding.UTF8.GetBytes("hello"), Now, "\"v1\"", "Sun, 25 Oct 2026 09:00:00 GMT");

        var bytes = HttpCache.Serialize(entry);
        var back = HttpCache.Deserialize(bytes)!;

        Assert.Equal(entry.Body, back.Body);
        Assert.Equal((entry.FetchedAt, entry.ETag, entry.LastModified), (back.FetchedAt, back.ETag, back.LastModified));
        Assert.Null(HttpCache.Deserialize(bytes[..^2]));
        Assert.Null(HttpCache.Deserialize([1, 2, 3, 4, 5, 6, 7, 8]));
        Assert.Null(HttpCache.Deserialize([]));
    }

    [Fact]
    public async Task ADamagedFile_IsDeleted_AndReadsAsAMiss()
    {
        var cache = new HttpCache { Folder = _folder };
        Directory.CreateDirectory(_folder);
        var key = new string('a', 64);
        await File.WriteAllTextAsync(Path.Combine(_folder, key + HttpCache.FileExtension), "not a cache file");

        Assert.Null(await cache.GetAsync(key, disk: true, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Memory_KeepsWithinItsLimit_DroppingTheLeastRecentlyUsed()
    {
        var cache = new HttpCache();
        var body = new byte[3 * 1024 * 1024];
        await cache.PutAsync("one", new CacheEntry(body, Now, null, null), disk: false, CancellationToken.None);
        await cache.PutAsync("two", new CacheEntry(body, Now, null, null), disk: false, CancellationToken.None);
        Assert.NotNull(cache.FromMemory("one"));

        await cache.PutAsync("three", new CacheEntry(body, Now, null, null), disk: false, CancellationToken.None);

        Assert.Equal(2, cache.MemoryCount);
        Assert.NotNull(cache.FromMemory("one"));
        Assert.Null(cache.FromMemory("two"));
        Assert.NotNull(cache.FromMemory("three"));
    }

    [Fact]
    public void ClearingAFolder_ReportsWhatItFreed_AndKeepsTheFolder()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "http"));
        File.WriteAllBytes(Path.Combine(_folder, "http", "a.cache"), new byte[1000]);
        File.WriteAllBytes(Path.Combine(_folder, "other.bin"), new byte[500]);

        Assert.Equal(1500, CacheFiles.Size(_folder));
        var result = CacheFiles.Clear(_folder);

        Assert.Equal(new ClearResult(1500, 0), result);
        Assert.True(Directory.Exists(_folder));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_folder));
        Assert.Equal(0, CacheFiles.Size(_folder));
        Assert.Equal(default, CacheFiles.Clear(Path.Combine(_folder, "missing")));
    }

    [Fact]
    public void AFileInUse_IsLeft_AndCounted()
    {
        Directory.CreateDirectory(_folder);
        var locked = Path.Combine(_folder, "locked.cache");
        File.WriteAllBytes(locked, new byte[100]);
        File.WriteAllBytes(Path.Combine(_folder, "free.cache"), new byte[200]);

        ClearResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            result = CacheFiles.Clear(_folder);

        Assert.Equal(new ClearResult(200, 1), result);
    }
}
