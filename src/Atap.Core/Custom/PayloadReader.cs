using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace Atap.Core.Custom;

/// <summary>How a payload is read before the template's paths run on it.</summary>
public enum PayloadFormat
{
    /// <summary>JSON when the text starts with <c>{</c>, <c>[</c> or <c>"</c> and parses, plain text otherwise.</summary>
    Auto,

    /// <summary>Always plain text. The whole payload, trimmed, becomes one string value at <c>$</c>.</summary>
    Plain,

    /// <summary>Always JSON. Text that does not parse is an error.</summary>
    Json,
}

public enum PayloadStatus
{
    /// <summary><see cref="PayloadReadResult.Root"/> holds the value.</summary>
    Ok,

    /// <summary>The payload is empty or only spaces. Root is JSON null.</summary>
    Empty,

    InvalidJson,

    /// <summary>The bytes are not valid UTF-8.</summary>
    InvalidText,

    TooLarge,
}

/// <summary>A read payload. <see cref="Root"/> is standalone, so it outlives the input and needs no disposal.</summary>
public readonly record struct PayloadReadResult(PayloadStatus Status, JsonElement Root)
{
    public bool IsOk => Status == PayloadStatus.Ok;
}

/// <summary>Turns JSON URL bodies, script output and MQTT payloads into a JSON value for <see cref="ValueTemplate"/>.</summary>
public static class PayloadReader
{
    /// <summary>Largest JSON URL body read, in bytes. Matches the HTTP service's cap for custom JSON.</summary>
    public const int JsonUrlMaxBytes = 256 * 1024;

    /// <summary>
    /// The cap when the caller sets none. Every caller is a custom widget, so this is the JSON URL cap.
    /// Script output and MQTT use their own smaller caps.
    /// </summary>
    public const int DefaultMaxBytes = JsonUrlMaxBytes;

    /// <summary>Deepest JSON nesting read. Deeper JSON does not parse.</summary>
    public const int MaxDepth = 32;

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxDepth };

    private static readonly JsonElement NullElement = ParseStandalone("null");

    /// <summary>Reads a payload from text. A lone surrogate becomes U+FFFD. Never throws.</summary>
    /// <param name="maxBytes">Largest accepted size in UTF-8 bytes.</param>
    public static PayloadReadResult Read(string? text, PayloadFormat format = PayloadFormat.Auto, int maxBytes = DefaultMaxBytes) =>
        Read(text, format, maxBytes, noValueWords: null);

    /// <summary>Reads a payload from UTF-8 bytes. A byte order mark is skipped. Never throws.</summary>
    /// <param name="maxBytes">Largest accepted size in bytes.</param>
    public static PayloadReadResult Read(ReadOnlySpan<byte> utf8, PayloadFormat format = PayloadFormat.Auto, int maxBytes = DefaultMaxBytes) =>
        Read(utf8, format, maxBytes, noValueWords: null);

    /// <inheritdoc cref="Read(string, PayloadFormat, int)"/>
    /// <param name="noValueWords">Whole payloads that mean there is no value. They give Empty before any JSON parsing.</param>
    internal static PayloadReadResult Read(string? text, PayloadFormat format, int maxBytes, IReadOnlyList<string>? noValueWords)
    {
        if (text is null)
            return new PayloadReadResult(PayloadStatus.Empty, NullElement);
        // Each UTF-16 character takes at most 3 UTF-8 bytes, so most text skips the exact count.
        if ((long)text.Length * 3 > maxBytes && Encoding.UTF8.GetByteCount(text) > maxBytes)
            return new PayloadReadResult(PayloadStatus.TooLarge, default);
        return ReadText(text, format, noValueWords);
    }

    /// <inheritdoc cref="Read(ReadOnlySpan{byte}, PayloadFormat, int)"/>
    /// <param name="noValueWords">Whole payloads that mean there is no value. They give Empty before any JSON parsing.</param>
    internal static PayloadReadResult Read(ReadOnlySpan<byte> utf8, PayloadFormat format, int maxBytes, IReadOnlyList<string>? noValueWords)
    {
        if (utf8.Length > maxBytes)
            return new PayloadReadResult(PayloadStatus.TooLarge, default);
        if (utf8.StartsWith("\uFEFF"u8))
            utf8 = utf8[3..];
        if (!Utf8.IsValid(utf8))
            return new PayloadReadResult(PayloadStatus.InvalidText, default);
        return ReadText(Encoding.UTF8.GetString(utf8), format, noValueWords);
    }

    /// <summary>A standalone JSON string value, as plain payloads become.</summary>
    internal static JsonElement StringElement(string value)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>(value.Length + 16);
        using (var writer = new Utf8JsonWriter(buffer))
            writer.WriteStringValue(ReplaceLoneSurrogates(value));
        using var document = JsonDocument.Parse(buffer.WrittenMemory, DocumentOptions);
        return document.RootElement.Clone();
    }

    internal static JsonElement Null => NullElement;

    private static PayloadReadResult ReadText(string text, PayloadFormat format, IReadOnlyList<string>? noValueWords)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.StartsWith("\uFEFF", StringComparison.Ordinal))
            trimmed = trimmed[1..].TrimStart();
        if (trimmed.IsEmpty || IsNoValueWord(trimmed, noValueWords))
            return new PayloadReadResult(PayloadStatus.Empty, NullElement);

        // The JSON parser throws ArgumentException on a lone surrogate, so replace them first.
        var body = ReplaceLoneSurrogates(trimmed.ToString());
        var looksLikeJson = body[0] is '{' or '[' or '"';
        if (format == PayloadFormat.Json || (format == PayloadFormat.Auto && looksLikeJson))
        {
            if (TryParse(ReplaceLoneSurrogateEscapes(body), out var root))
                return new PayloadReadResult(PayloadStatus.Ok, root);
            if (format == PayloadFormat.Json)
                return new PayloadReadResult(PayloadStatus.InvalidJson, default);
        }

        return new PayloadReadResult(PayloadStatus.Ok, StringElement(body));
    }

    private static bool TryParse(string text, out JsonElement root)
    {
        try
        {
            root = ParseStandalone(text);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            root = default;
            return false;
        }
    }

    private static JsonElement ParseStandalone(string json)
    {
        using var document = JsonDocument.Parse(json, DocumentOptions);
        return document.RootElement.Clone();
    }

    private static bool IsNoValueWord(ReadOnlySpan<char> text, IReadOnlyList<string>? words)
    {
        if (words is null)
            return false;
        foreach (var word in words)
        {
            if (word is not null && text.Equals(word.AsSpan().Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rewrites each <c>\u</c> escape for a lone surrogate, such as <c>\uD800</c>, to the escape for U+FFFD. System.Text.Json
    /// parses these but throws each time the string or name is read, and the fallback for that is slow on large payloads.
    /// One pass, and the text keeps its length. A pair of escapes that makes one character is kept.
    /// Raw lone surrogates are already gone, so an escape can only pair with another escape.
    /// </summary>
    private static string ReplaceLoneSurrogateEscapes(string json)
    {
        char[]? copy = null;
        for (var i = 0; i < json.Length - 1; i++)
        {
            if (json[i] != '\\')
                continue;
            // Step onto the escaped character, so the second backslash of "\\" never starts an escape.
            i++;
            if (json[i] != 'u' || !TryReadHex4(json, i + 1, out var code) || !char.IsSurrogate(code))
                continue;

            if (char.IsHighSurrogate(code) && i + 6 < json.Length && json[i + 5] == '\\' && json[i + 6] == 'u'
                && TryReadHex4(json, i + 7, out var next) && char.IsLowSurrogate(next))
            {
                i += 10;
                continue;
            }

            copy ??= json.ToCharArray();
            "FFFD".CopyTo(copy.AsSpan(i + 1));
            i += 4;
        }
        return copy is null ? json : new string(copy);
    }

    private static bool TryReadHex4(string text, int start, out char code)
    {
        code = '\0';
        if (start + 4 > text.Length
            || !ushort.TryParse(text.AsSpan(start, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            return false;
        code = (char)value;
        return true;
    }

    private static string ReplaceLoneSurrogates(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }
            return string.Create(value.Length, value, static (span, source) =>
            {
                source.AsSpan().CopyTo(span);
                for (var j = 0; j < span.Length; j++)
                {
                    if (char.IsHighSurrogate(span[j]) && j + 1 < span.Length && char.IsLowSurrogate(span[j + 1]))
                        j++;
                    else if (char.IsSurrogate(span[j]))
                        span[j] = '\uFFFD';
                }
            });
        }
        return value;
    }
}
