using System.Security.Cryptography;
using System.Text;

namespace Atap.Core.Net;

/// <summary>One cached response, with what a conditional request needs.</summary>
internal sealed record CacheEntry(byte[] Body, DateTimeOffset FetchedAt, string? ETag, string? LastModified);

/// <summary>
/// The response cache: memory, plus one disk file per URL. File names are the SHA-256 of the request, and
/// the files hold no URL text, so nothing on disk says what was fetched. Requests with headers or from a
/// secret URL stay in memory only. Thread-safe.
/// </summary>
internal sealed class HttpCache
{
    public const string FileExtension = ".cache";

    /// <summary>Memory kept for bodies. Past it, the least recently used ones go (disk copies stay on disk).</summary>
    public const long MemoryLimit = 8 * 1024 * 1024;

    /// <summary>A file larger than this is not a cache file Atap wrote. It is deleted instead of read.</summary>
    private const long LargestFile = 8 * 1024 * 1024;

    private const int Magic = 0x31435441; // "ATC1"

    private readonly Dictionary<string, (CacheEntry Entry, long LastUsed)> _memory = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _memoryBytes;
    private long _uses;
    private volatile string? _folder;

    /// <summary>The disk cache folder. Null keeps everything in memory.</summary>
    public string? Folder
    {
        get => _folder;
        set => _folder = value;
    }

    public int MemoryCount
    {
        get
        {
            lock (_gate)
                return _memory.Count;
        }
    }

    /// <summary>The cache key, and the file name without its extension: SHA-256 of the URL and any headers, in hex.</summary>
    public static string KeyFor(FetchRequest request)
    {
        var text = new StringBuilder(request.Url.AbsoluteUri);
        if (request.Headers is { Count: > 0 } headers)
        {
            foreach (var (name, value) in headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
                text.Append('\n').Append(name.ToLowerInvariant()).Append(':').Append(value);
        }
        return Hash(text.ToString());
    }

    /// <summary>SHA-256 of <paramref name="text"/> in lower-case hex.</summary>
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Only plain requests go to disk: no headers, not from a secret, and DiskCache on.</summary>
    public static bool MayUseDisk(FetchRequest request) =>
        request.DiskCache && !request.FromSecret && request.Headers is not { Count: > 0 };

    public CacheEntry? FromMemory(string key)
    {
        lock (_gate)
        {
            if (!_memory.TryGetValue(key, out var found))
                return null;
            _memory[key] = (found.Entry, ++_uses);
            return found.Entry;
        }
    }

    /// <summary>The entry from memory, or else from its disk file. Null when neither has it.</summary>
    public async Task<CacheEntry?> GetAsync(string key, bool disk, CancellationToken cancellationToken)
    {
        if (FromMemory(key) is { } entry)
            return entry;
        if (!disk || PathFor(key) is not { } path || !File.Exists(path))
            return null;

        var loaded = await ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (loaded is not null)
            Remember(key, loaded);
        return loaded;
    }

    /// <summary>Keeps <paramref name="entry"/> in memory, and on disk when allowed. A disk failure only loses the disk copy.</summary>
    public async Task PutAsync(string key, CacheEntry entry, bool disk, CancellationToken cancellationToken)
    {
        Remember(key, entry);
        if (!disk || PathFor(key) is not { } path)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, Serialize(entry), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder was cleared or is read-only. The memory copy still works.
        }
    }

    /// <summary>Forgets every entry and deletes the disk folder's files.</summary>
    public ClearResult Clear()
    {
        lock (_gate)
        {
            _memory.Clear();
            _memoryBytes = 0;
        }
        return Folder is { } folder ? CacheFiles.Clear(folder) : default;
    }

    internal static byte[] Serialize(CacheEntry entry)
    {
        using var stream = new MemoryStream(entry.Body.Length + 128);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(entry.FetchedAt.ToUnixTimeMilliseconds());
            writer.Write(entry.ETag ?? string.Empty);
            writer.Write(entry.LastModified ?? string.Empty);
            writer.Write(entry.Body.Length);
            writer.Write(entry.Body);
        }
        return stream.ToArray();
    }

    internal static CacheEntry? Deserialize(byte[] data)
    {
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            if (reader.ReadInt32() != Magic)
                return null;
            var fetchedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
            var etag = reader.ReadString();
            var lastModified = reader.ReadString();
            var length = reader.ReadInt32();
            if (length < 0 || length > data.Length)
                return null;
            var body = reader.ReadBytes(length);
            if (body.Length != length)
                return null;
            return new CacheEntry(body, fetchedAt, etag.Length == 0 ? null : etag, lastModified.Length == 0 ? null : lastModified);
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static async Task<CacheEntry?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (new FileInfo(path).Length > LargestFile)
            {
                File.Delete(path);
                return null;
            }
            var entry = Deserialize(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
            if (entry is null)
                File.Delete(path);
            return entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? PathFor(string key) => Folder is { } folder ? Path.Combine(folder, key + FileExtension) : null;

    private void Remember(string key, CacheEntry entry)
    {
        lock (_gate)
        {
            if (_memory.TryGetValue(key, out var old))
                _memoryBytes -= old.Entry.Body.Length;
            _memory[key] = (entry, ++_uses);
            _memoryBytes += entry.Body.Length;

            while (_memoryBytes > MemoryLimit && _memory.Count > 1)
            {
                var oldest = _memory.Where(m => m.Key != key).MinBy(m => m.Value.LastUsed);
                _memory.Remove(oldest.Key);
                _memoryBytes -= oldest.Value.Entry.Body.Length;
            }
        }
    }
}
