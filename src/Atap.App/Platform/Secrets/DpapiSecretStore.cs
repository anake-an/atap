using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Platform.Secrets;

/// <summary>
/// Secrets (keys, tokens, private addresses) kept with Windows data protection (DPAPI, current user) in
/// %LOCALAPPDATA%\AtapData\secrets.json, which is never synced. Each value is a protected blob under a random
/// id, and settings hold only "secret:&lt;id&gt;". A blob opens only for this Windows user on this PC, so a
/// reference copied from another PC resolves to nothing, and its field asks for the value again.
/// </summary>
/// <remarks>
/// Safe on any thread. Nothing is read or written before the first call, and nothing is kept in memory: each
/// call reads the file, and changes are written with a temp file and a rename. <see cref="Put"/> and
/// <see cref="Delete"/> throw <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the
/// file cannot be written. A file from a newer Atap is read but never written.
/// </remarks>
internal sealed class DpapiSecretStore : ISecretStore
{
    public const int CurrentSchemaVersion = 1;

    // 8 random bytes, written as 16 hex digits.
    private const int IdBytes = 8;
    private const int MaxIdLength = 64;

    private readonly object _gate = new();
    private readonly TimeProvider _time;
    private bool? _available;

    /// <summary>The store in %LOCALAPPDATA%\AtapData\secrets.json.</summary>
    public DpapiSecretStore()
        : this(AtapPaths.Default.SecretsFile)
    {
    }

    /// <param name="filePath">The secrets file.</param>
    /// <param name="time">Clock for the name of a corrupt file's backup.</param>
    public DpapiSecretStore(string filePath, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        _time = time ?? TimeProvider.System;
    }

    public string FilePath { get; }

    public bool IsAvailable
    {
        get
        {
            lock (_gate)
                return _available ??= Probe();
        }
    }

    public string Put(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (!(_available ??= Probe()))
                throw new InvalidOperationException("Windows data protection is not available, so secrets cannot be stored on this PC.");

            var file = ReadForWrite();
            string id;
            do
                id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(IdBytes));
            while (file.Secrets.ContainsKey(id));
            file.Secrets[id] = Protect(id, value);
            Write(file.Root);
            return SettingValues.SecretPrefix + id;
        }
    }

    public string? Get(string reference)
    {
        if (!TryGetId(reference, out var id))
            return null;
        lock (_gate)
        {
            StoredFile? file;
            try
            {
                file = Read(out _);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
            return file?.Secrets[id] is JsonValue blob && blob.TryGetValue<string>(out var text) ? Unprotect(id, text) : null;
        }
    }

    public void Delete(string reference)
    {
        if (!TryGetId(reference, out var id))
            return;
        lock (_gate)
        {
            if (Read(out _) is not { Newer: false } file || !file.Secrets.Remove(id))
                return;
            Write(file.Root);
        }
    }

    /// <summary>The file as stored, null when it is missing or does not parse. Other I/O errors are thrown.</summary>
    private StoredFile? Read(out bool corrupt)
    {
        corrupt = false;
        byte[] bytes;
        try
        {
            using var stream = AtomicFile.OpenRead(FilePath);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(bytes) is JsonObject root)
            {
                var version = root["schemaVersion"] is not { } versionNode ? CurrentSchemaVersion
                    : versionNode is JsonValue value && value.TryGetValue<int>(out var number) ? number : int.MaxValue;
                // A newer Atap's file is never renamed aside or written, whatever its shape.
                if (version > CurrentSchemaVersion)
                    return new StoredFile(root, root["secrets"] as JsonObject ?? new JsonObject(), Newer: true);
                if (root["secrets"] is null)
                    root["secrets"] = new JsonObject();
                if (root["secrets"] is JsonObject secrets)
                    return new StoredFile(root, secrets, version > CurrentSchemaVersion);
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            // Handled below with the other shapes this version cannot read.
        }
        corrupt = true;
        return null;
    }

    /// <summary>The file to add to. A corrupt one is kept aside and a new one started, since its blobs cannot open anyway.</summary>
    private StoredFile ReadForWrite()
    {
        var file = Read(out var corrupt);
        if (corrupt)
        {
            var stamp = _time.GetLocalNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var directory = Path.GetDirectoryName(FilePath) ?? string.Empty;
            File.Move(FilePath, Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(FilePath)}.corrupt-{stamp}.json"), overwrite: true);
        }
        if (file is null)
        {
            var secrets = new JsonObject();
            return new StoredFile(new JsonObject { ["schemaVersion"] = CurrentSchemaVersion, ["secrets"] = secrets }, secrets, Newer: false);
        }
        if (file.Newer)
            throw new InvalidOperationException("The secrets file is from a newer Atap, so this version never writes it.");
        return file;
    }

    private void Write(JsonObject root)
    {
        root["schemaVersion"] = CurrentSchemaVersion;
        AtomicFile.Write(FilePath, stream =>
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            root.WriteTo(writer);
        });
    }

    private static string Protect(string id, string value)
    {
        var plain = Encoding.UTF8.GetBytes(value);
        try
        {
            return Convert.ToBase64String(ProtectedData.Protect(plain, Entropy(id), DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static string? Unprotect(string id, string blob)
    {
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(Convert.FromBase64String(blob), Entropy(id), DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Made for another Windows user or PC, or damaged. The field asks for the value again.
            return null;
        }
        finally
        {
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Ties each blob to its id, so a blob moved to another id does not open.</summary>
    private static byte[] Entropy(string id) => Encoding.UTF8.GetBytes("Atap.Secret." + id);

    private static bool Probe()
    {
        try
        {
            var blob = ProtectedData.Protect([1], null, DataProtectionScope.CurrentUser);
            return ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser) is [1];
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>A "secret:&lt;id&gt;" reference with a plausible id. Anything else never resolves.</summary>
    private static bool TryGetId(string? reference, out string id)
    {
        id = string.Empty;
        if (reference is null || !reference.StartsWith(SettingValues.SecretPrefix, StringComparison.Ordinal))
            return false;
        var candidate = reference[SettingValues.SecretPrefix.Length..];
        if (candidate.Length is 0 or > MaxIdLength || !candidate.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return false;
        id = candidate;
        return true;
    }

    private sealed record StoredFile(JsonObject Root, JsonObject Secrets, bool Newer);
}
