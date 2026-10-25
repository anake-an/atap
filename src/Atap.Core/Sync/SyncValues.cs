using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Atap.Core.Sync;

/// <summary>
/// Value rules for sync units: a canonical hash, and the parts of a value that stay on this PC.
/// </summary>
/// <remarks>
/// A string starting with <c>secret:</c> is a secret reference. It points at a value in this PC's
/// secrets.json, so it means nothing on another PC. References are left out of every exported
/// value and never taken from a bundle, so a bundle cannot point a widget at one of this PC's
/// secrets. On import the local references are put back where the remote value has room for them.
/// A field whose setting has type Secret stays on this PC even when it holds a plain value. The
/// scope names those fields, and they are handled like Machine fields.
/// </remarks>
public static class SyncValues
{
    public const string SecretPrefix = "secret:";

    /// <summary>
    /// SHA-256 of the canonical form, as lowercase hex. Property order, number spelling and string
    /// escaping do not change it. Of duplicate properties the last one counts, as when deserializing.
    /// </summary>
    public static string Hash(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, value);
        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    /// <summary>True for a string value that is a secret reference. The prefix is matched without case.</summary>
    public static bool IsSecretReference(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
        && value.GetString()!.StartsWith(SecretPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The value as it may leave this PC: secret references removed at any depth, and top-level
    /// fields for which <paramref name="isLocalField"/> is true removed. Null when the whole value
    /// is a secret reference, so the unit stays on this PC.
    /// </summary>
    /// <param name="isLocalField">True for a top-level field that stays on this PC: a Machine or Secret field.</param>
    public static JsonElement? Strip(JsonElement value, Func<string, bool>? isLocalField = null)
    {
        if (IsSecretReference(value))
            return null;
        if (!HasLocalOnlyParts(value, isLocalField, topLevel: true))
            return value;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteStripped(writer, value, isLocalField);
        return Parse(buffer.WrittenSpan);
    }

    /// <summary>
    /// Puts this PC's secret references, Machine fields and Secret fields from <paramref name="local"/> back into
    /// <paramref name="remote"/>, which must already be stripped. A field goes back only where the
    /// remote value has the same object at the same path and lacks that field. An array item gets
    /// local parts back only from a local item that equals it once stripped, so a reordered array
    /// keeps each secret with its own item. A remote item that matches no local item is taken as it
    /// is, without secrets.
    /// </summary>
    /// <remarks>
    /// Secrets (secret references, and top-level fields <paramref name="isSecretField"/> names) go back only
    /// while the unit still connects where it did: every field named like an address (url, host, server,
    /// endpoint, port and the like) and every string that is a web address must be unchanged. Otherwise
    /// another PC, or anyone who can write the sync folder, could point a widget at their own server and
    /// receive its token. The secrets are then left out, so the widget asks for them again. Machine fields
    /// always go back.
    /// </remarks>
    public static JsonElement Restore(JsonElement remote, JsonElement? local, Func<string, bool>? isLocalField = null, Func<string, bool>? isSecretField = null)
    {
        if (local is not { } localValue || !HasLocalOnlyParts(localValue, isLocalField, topLevel: true))
            return remote;

        var keepSecrets = Strip(localValue, isLocalField) is { } exported && SameDestinations(exported, remote);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteRestored(writer, remote, localValue, new RestoreRules(isLocalField, isSecretField, keepSecrets), topLevel: true);
        return Parse(buffer.WrittenSpan);
    }

    /// <summary>True when both values name the same places to connect to.</summary>
    internal static bool SameDestinations(JsonElement a, JsonElement b)
    {
        var left = new Dictionary<string, string>(StringComparer.Ordinal);
        var right = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectDestinations(a, "$", left);
        CollectDestinations(b, "$", right);
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    private static readonly HashSet<string> DestinationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "urls", "uri", "href", "link", "host", "hostname", "hosts", "server", "servers", "endpoint", "endpoints",
        "address", "addr", "domain", "proxy", "webhook", "ip", "port", "broker", "base", "origin", "target", "site",
    };

    /// <summary>A field name such as apiUrl, base_url or Host, split into words.</summary>
    private static bool IsDestinationName(string name) =>
        System.Text.RegularExpressions.Regex.Split(name, "(?<=[a-z0-9])(?=[A-Z])|[^A-Za-z0-9]+").Any(DestinationWords.Contains);

    private static void CollectDestinations(JsonElement value, string path, Dictionary<string, string> found)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    var child = path + "." + property.Name;
                    if (IsDestinationName(property.Name) && property.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                        found[child] = property.Value.GetRawText();
                    CollectDestinations(property.Value, child, found);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                    CollectDestinations(item, $"{path}[{index++}]", found);
                break;
            case JsonValueKind.String:
                if (Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) && !uri.IsFile)
                    found[path] = value.GetString()!;
                break;
        }
    }

    private sealed record RestoreRules(Func<string, bool>? IsLocalField, Func<string, bool>? IsSecretField, bool KeepSecrets)
    {
        /// <summary>A local-only part that goes back: Machine fields always, secrets only when allowed.</summary>
        public bool PutsBack(string name, JsonElement value, bool topLevel) =>
            KeepSecrets || !(IsSecretReference(value) || (topLevel && IsSecretField is not null && IsSecretField(name)));
    }

    internal static JsonElement Parse(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 256 });
        return JsonElement.ParseValue(ref reader);
    }

    private static bool IsLocalOnly(string name, JsonElement value, Func<string, bool>? isLocalField, bool topLevel) =>
        IsSecretReference(value) || (topLevel && isLocalField is not null && isLocalField(name));

    private static bool HasLocalOnlyParts(JsonElement value, Func<string, bool>? isLocalField, bool topLevel)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (IsLocalOnly(property.Name, property.Value, isLocalField, topLevel)
                        || HasLocalOnlyParts(property.Value, isLocalField, topLevel: false))
                        return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    if (IsSecretReference(item) || HasLocalOnlyParts(item, isLocalField, topLevel: false))
                        return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static void WriteStripped(Utf8JsonWriter writer, JsonElement value, Func<string, bool>? isLocalField, bool topLevel = true)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    if (IsLocalOnly(property.Name, property.Value, isLocalField, topLevel))
                        continue;
                    writer.WritePropertyName(property.Name);
                    WriteStripped(writer, property.Value, isLocalField, topLevel: false);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                {
                    if (IsSecretReference(item))
                        continue;
                    WriteStripped(writer, item, isLocalField, topLevel: false);
                }
                writer.WriteEndArray();
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void WriteRestored(Utf8JsonWriter writer, JsonElement remote, JsonElement local, RestoreRules rules, bool topLevel)
    {
        var isLocalField = rules.IsLocalField;
        if (remote.ValueKind == JsonValueKind.Object && local.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in remote.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                if (local.TryGetProperty(property.Name, out var localChild)
                    && !IsLocalOnly(property.Name, localChild, isLocalField, topLevel))
                    WriteRestored(writer, property.Value, localChild, rules, topLevel: false);
                else
                    property.Value.WriteTo(writer);
            }
            foreach (var property in local.EnumerateObject())
            {
                if (IsLocalOnly(property.Name, property.Value, isLocalField, topLevel) && !remote.TryGetProperty(property.Name, out _)
                    && rules.PutsBack(property.Name, property.Value, topLevel))
                {
                    writer.WritePropertyName(property.Name);
                    property.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }
        else if (remote.ValueKind == JsonValueKind.Array && local.ValueKind == JsonValueKind.Array
            && rules.KeepSecrets && Strip(local) is { } strippedLocal && Hash(strippedLocal) == Hash(remote))
        {
            // The array did not change, only another field of the unit did: it stays exactly as it is here,
            // bare secret references included.
            local.WriteTo(writer);
        }
        else if (remote.ValueKind == JsonValueKind.Array && local.ValueKind == JsonValueKind.Array)
        {
            // Items match by content, not by position. A bare reference in a changed array cannot be placed and is dropped.
            writer.WriteStartArray();
            var localItems = local.EnumerateArray().ToArray();
            var strippedItems = Array.ConvertAll(localItems, item => Strip(item));
            var withLocalParts = Enumerable.Range(0, localItems.Length)
                .Where(i => HasLocalOnlyParts(localItems[i], isLocalField, topLevel: false))
                .ToArray();
            var used = new bool[localItems.Length];
            var index = 0;
            foreach (var item in remote.EnumerateArray())
            {
                var match = MatchingItem(item, index, strippedItems, withLocalParts, used);
                if (match >= 0)
                {
                    used[match] = true;
                    WriteRestored(writer, item, localItems[match], rules, topLevel: false);
                }
                else
                {
                    item.WriteTo(writer);
                }
                index++;
            }
            writer.WriteEndArray();
        }
        else
        {
            remote.WriteTo(writer);
        }
    }

    /// <summary>
    /// The unused local item that equals <paramref name="remote"/> once stripped, or -1. The item at
    /// the same position is tried first, so equal items keep their order. Only items with local-only
    /// parts are searched, because any other item restores to the remote item as it is.
    /// </summary>
    private static int MatchingItem(JsonElement remote, int index, JsonElement?[] strippedItems, int[] withLocalParts, bool[] used)
    {
        if (index < strippedItems.Length && !used[index] && Matches(index))
            return index;
        foreach (var candidate in withLocalParts)
        {
            if (!used[candidate] && Matches(candidate))
                return candidate;
        }
        return -1;

        bool Matches(int item) => strippedItems[item] is { } stripped && JsonElement.DeepEquals(stripped, remote);
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                    properties[property.Name] = property.Value;
                writer.WriteStartObject();
                foreach (var name in properties.Keys.Order(StringComparer.Ordinal))
                {
                    writer.WritePropertyName(name);
                    WriteCanonical(writer, properties[name]);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                WriteCanonicalNumber(writer, value);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }

    private static void WriteCanonicalNumber(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.TryGetInt64(out var integer))
        {
            writer.WriteNumberValue(integer);
            return;
        }
        if (value.TryGetDouble(out var number) && double.IsFinite(number))
        {
            // 1.0 and 1e0 hash like 1.
            if (Math.Floor(number) == number && number is >= long.MinValue and < long.MaxValue)
                writer.WriteNumberValue((long)number);
            else
                writer.WriteNumberValue(number);
            return;
        }
        value.WriteTo(writer);
    }
}
