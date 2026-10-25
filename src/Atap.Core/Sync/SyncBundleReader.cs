using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace Atap.Core.Sync;

public enum SyncBundleReadOutcome
{
    /// <summary>The bundle was read. Malformed units, if any, are in <see cref="SyncBundleReadResult.Skipped"/>.</summary>
    Loaded,

    /// <summary>Not JSON, text that is not valid UTF-8, or not a format 1 bundle.</summary>
    Invalid,

    /// <summary>Over <see cref="SyncLimits.MaxBundleBytes"/>.</summary>
    TooLarge,

    /// <summary>Nested deeper than <see cref="SyncLimits.MaxDepth"/>.</summary>
    TooDeep,

    /// <summary>Written by a newer Atap in a format this version does not know.</summary>
    NewerFormat,
}

public sealed class SyncBundleReadResult
{
    internal SyncBundleReadResult(SyncBundleReadOutcome outcome, SyncBundle? bundle = null, IReadOnlyList<SyncSkip>? skipped = null)
    {
        Outcome = outcome;
        Bundle = bundle;
        Skipped = skipped ?? [];
    }

    public SyncBundleReadOutcome Outcome { get; }

    /// <summary>The bundle when <see cref="Outcome"/> is Loaded. Otherwise null, and the file is ignored.</summary>
    public SyncBundle? Bundle { get; }

    public IReadOnlyList<SyncSkip> Skipped { get; }

    /// <summary>
    /// True when this PC may write over the file: it was read, or it is broken. A file from a newer
    /// Atap, or one over the limits, is left alone so no other PC's data is lost.
    /// </summary>
    /// <remarks>
    /// While such a file is there, every PC's export is <see cref="SyncBundleOutcome.Blocked"/>. A file
    /// over the limits may also be garbage, so the App shows a banner for TooLarge and TooDeep with a
    /// "Replace the sync file" action. When the user picks it, run the pass with
    /// <see cref="SyncMergeRequest.Main"/> null and <see cref="SyncMergeRequest.MainCanBeReplaced"/> true.
    /// For NewerFormat the banner asks the user to update Atap instead.
    /// </remarks>
    public bool CanBeReplaced => Outcome is SyncBundleReadOutcome.Loaded or SyncBundleReadOutcome.Invalid;
}

/// <summary>
/// Reads a sync bundle. The text is untrusted: it comes from a folder any synced PC, or anyone
/// with access to the OneDrive account, can write.
/// </summary>
/// <remarks>
/// Check the file length before reading the file. A file longer than
/// <see cref="SyncLimits.MaxBundleFileBytes"/> is TooLarge, so it never has to be loaded.
/// </remarks>
public static class SyncBundleReader
{
    private const char ByteOrderMark = '\uFEFF';

    /// <summary>Reads a bundle from its text, with or without a byte order mark.</summary>
    public static SyncBundleReadResult Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(SyncBundleReadOutcome.Invalid);
        if (text[0] == ByteOrderMark)
            text = text[1..];

        // UTF-8 never takes fewer bytes than UTF-16 chars, so a long string fails before it is encoded.
        if (text.Length > SyncLimits.MaxBundleBytes || Encoding.UTF8.GetByteCount(text) > SyncLimits.MaxBundleBytes)
            return new(SyncBundleReadOutcome.TooLarge);

        return ReadUtf8(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Reads a bundle from the bytes of the file, with or without a UTF-8 byte order mark. Bytes that
    /// are not valid UTF-8, such as a file saved as ANSI in Notepad, make the bundle Invalid.
    /// </summary>
    public static SyncBundleReadResult Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith(Encoding.UTF8.Preamble))
            utf8 = utf8[Encoding.UTF8.Preamble.Length..];
        if (utf8.Length > SyncLimits.MaxBundleBytes)
            return new(SyncBundleReadOutcome.TooLarge);

        // The JSON parser lets bad UTF-8 through inside strings, and reading such a string later throws.
        if (!Utf8.IsValid(utf8))
            return new(SyncBundleReadOutcome.Invalid);
        return ReadUtf8(utf8.ToArray());
    }

    private static SyncBundleReadResult ReadUtf8(ReadOnlyMemory<byte> utf8)
    {
        var scan = Scan(utf8.Span);
        if (scan != SyncBundleReadOutcome.Loaded)
            return new(scan);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = SyncLimits.MaxDepth, AllowDuplicateProperties = false });
        }
        catch (JsonException)
        {
            return new(SyncBundleReadOutcome.Invalid);
        }

        using (document)
            return ReadRoot(document.RootElement);
    }

    private static SyncBundleReadResult ReadRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new(SyncBundleReadOutcome.Invalid);

        // Format first, so a newer bundle with a different shape is reported as newer, not broken.
        if (!root.TryGetProperty("format", out var formatElement)
            || formatElement.ValueKind != JsonValueKind.Number
            || !formatElement.TryGetInt32(out var format)
            || format < 1)
            return new(SyncBundleReadOutcome.Invalid);
        if (format > SyncLimits.CurrentFormat)
            return new(SyncBundleReadOutcome.NewerFormat);

        if (!root.TryGetProperty("deviceId", out var deviceElement)
            || deviceElement.ValueKind != JsonValueKind.String
            || !SyncVersion.TryNormalizeDevice(deviceElement.GetString(), out var deviceId))
            return new(SyncBundleReadOutcome.Invalid);

        if (!root.TryGetProperty("units", out var unitsElement) || unitsElement.ValueKind != JsonValueKind.Object)
            return new(SyncBundleReadOutcome.Invalid);

        var units = new Dictionary<string, SyncBundleUnit>(StringComparer.Ordinal);
        var skipped = new List<SyncSkip>();
        foreach (var property in unitsElement.EnumerateObject())
        {
            if (!SyncUnitNames.IsWellFormed(property.Name))
            {
                skipped.Add(new SyncSkip(SafeName(property.Name), SyncSkipReason.MalformedUnit, "name"));
                continue;
            }
            if (!TryReadUnit(property.Value, deviceId, out var unit, out var problem))
            {
                skipped.Add(new SyncSkip(property.Name, SyncSkipReason.MalformedUnit, problem));
                continue;
            }
            units[property.Name] = unit;
        }

        return new(SyncBundleReadOutcome.Loaded, new SyncBundle(deviceId, units) { Format = format }, skipped);
    }

    private static bool TryReadUnit(JsonElement element, string bundleDevice, out SyncBundleUnit unit, out string? problem)
    {
        unit = null!;
        problem = null;
        if (element.ValueKind != JsonValueKind.Object)
        {
            problem = "not an object";
            return false;
        }

        if (!element.TryGetProperty("stamp", out var stampElement)
            || stampElement.ValueKind != JsonValueKind.String
            || !SyncVersion.TryParseStamp(stampElement.GetString(), out var stamp)
            // The initial stamp, and anything before it, never leaves a PC, so a bundle that has one is malformed.
            || stamp <= SyncVersion.InitialStamp)
        {
            problem = "stamp";
            return false;
        }

        var device = bundleDevice;
        if (element.TryGetProperty("device", out var deviceElement)
            && (deviceElement.ValueKind != JsonValueKind.String || !SyncVersion.TryNormalizeDevice(deviceElement.GetString(), out device)))
        {
            problem = "device";
            return false;
        }

        var deleted = false;
        if (element.TryGetProperty("deleted", out var deletedElement))
        {
            if (deletedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                problem = "deleted";
                return false;
            }
            deleted = deletedElement.GetBoolean();
        }

        var version = new SyncVersion(stamp, device);
        if (deleted)
        {
            unit = SyncBundleUnit.Deleted(version);
            return true;
        }

        if (!element.TryGetProperty("value", out var value))
        {
            problem = "value";
            return false;
        }
        unit = new SyncBundleUnit(version, value.Clone());
        return true;
    }

    /// <summary>
    /// Checks syntax, nesting and escaped strings in one pass, stopping at the first level past the
    /// limit. The bytes must already be valid UTF-8.
    /// </summary>
    private static SyncBundleReadOutcome Scan(ReadOnlySpan<byte> utf8)
    {
        // The reader's own limit (64) is never reached, because the scan stops at MaxDepth + 1.
        var reader = new Utf8JsonReader(utf8);
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray
                    && reader.CurrentDepth + 1 > SyncLimits.MaxDepth)
                    return SyncBundleReadOutcome.TooDeep;

                // An escaped lone surrogate, such as "\uD800", parses but cannot be read as a string.
                // Reading it here keeps that from throwing later in the merge.
                if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueIsEscaped)
                    _ = reader.GetString();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return SyncBundleReadOutcome.Invalid;
        }
        return SyncBundleReadOutcome.Loaded;
    }

    /// <summary>A malformed name made safe to log: at most 64 chars, anything outside printable ASCII shown as '?'.</summary>
    private static string SafeName(string name)
    {
        var chars = name.Length <= 64 ? name.ToCharArray() : name[..64].ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is < ' ' or > '~')
                chars[i] = '?';
        }
        return new string(chars);
    }
}
