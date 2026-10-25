using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Atap.Core.Storage;

namespace Atap.Widgets.Shelf;

/// <summary>
/// The clipboard history kept on this PC when the user turns on "Keep my history". The list is protected with
/// Windows data protection (DPAPI, current user), the same way as secrets, so it opens only for this Windows user
/// on this PC. It lives in the instance's machine state, which is never synced, and is never logged.
/// </summary>
/// <remarks>
/// Never throws for a file that cannot be read, written or opened. A history that does not open starts empty.
/// </remarks>
internal sealed class ClipboardHistoryStore(IWidgetStorage storage)
{
    public const string FileName = "history";

    // Ties the blob to this file, so a blob made for something else does not open here.
    private static readonly byte[] Entropy = "Atap.Clipboard.History"u8.ToArray();

    /// <summary>The saved items, newest first. Empty when there are none or they do not open.</summary>
    public IReadOnlyList<string> Load()
    {
        var file = storage.Load(FileName, ClipboardJson.Default.ClipboardHistoryFile, new ClipboardHistoryFile(), DataScope.Machine);
        if (string.IsNullOrEmpty(file.Data))
            return [];
        byte[]? plain = null;
        try
        {
            plain = ProtectedData.Unprotect(Convert.FromBase64String(file.Data), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize(plain, ClipboardJson.Default.ListString) ?? [];
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or PlatformNotSupportedException)
        {
            // Made for another Windows user or PC, or damaged. The next save replaces it.
            return [];
        }
        finally
        {
            if (plain is not null)
                CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <summary>Saves the items, or deletes the file when there are none.</summary>
    public void Save(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            Delete();
            return;
        }
        var plain = JsonSerializer.SerializeToUtf8Bytes(items.ToList(), ClipboardJson.Default.ListString);
        try
        {
            var blob = Convert.ToBase64String(ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser));
            storage.Save(FileName, new ClipboardHistoryFile { Data = blob }, ClipboardJson.Default.ClipboardHistoryFile, DataScope.Machine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or PlatformNotSupportedException)
        {
            // Not kept this time. The next change tries again.
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Delete()
    {
        try
        {
            storage.Delete(FileName, DataScope.Machine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked for now. Turning the setting off again, or removing the widget, deletes it.
        }
    }
}

/// <summary>The saved file: only a protected blob, never the text itself.</summary>
internal sealed record ClipboardHistoryFile
{
    public int Version { get; init; } = 1;

    public string Data { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClipboardHistoryFile))]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class ClipboardJson : JsonSerializerContext;
