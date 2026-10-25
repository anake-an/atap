using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Malaysia;

/// <summary>Thrown inside a parser to stop at the first problem. Never leaves the parser.</summary>
internal sealed class MalaysiaParseException(ParseError error, string? detail) : Exception(detail)
{
    public ParseError Error { get; } = error;

    public string? Detail { get; } = detail;
}

/// <summary>Strict JsonElement readers shared by the Malaysia parsers.</summary>
internal static class MalaysiaJson
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 16 };

    /// <summary>The longest server text kept in a <see cref="ParseResult{T}.Detail"/>.</summary>
    public const int MaxDetailLength = 120;

    /// <summary>
    /// Parses <paramref name="json"/> and runs <paramref name="read"/> on the root.
    /// Any problem becomes a failed result, so callers never see an exception.
    /// </summary>
    public static ParseResult<T> Run<T>(string? json, Func<JsonElement, T> read)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return ParseResult<T>.Fail(ParseError.Empty);

        try
        {
            using var document = JsonDocument.Parse(json, Options);
            return ParseResult<T>.Ok(read(document.RootElement));
        }
        catch (JsonException)
        {
            return ParseResult<T>.Fail(ParseError.NotJson);
        }
        catch (MalaysiaParseException ex)
        {
            return ParseResult<T>.Fail(ex.Error, ex.Detail);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            // A safety net for values the readers did not foresee. Bad input must never throw.
            return ParseResult<T>.Fail(ParseError.BadValue, ex.GetType().Name);
        }
    }

    public static MalaysiaParseException Fail(ParseError error, string? detail = null) => new(error, detail);

    /// <summary>
    /// Text sent by a server, made safe for the App to log. Control and format characters (line
    /// breaks, bidi marks) are removed and the result is cut to <see cref="MaxDetailLength"/>.
    /// </summary>
    public static string Clip(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, MaxDetailLength));
        foreach (var c in text)
        {
            if (builder.Length == MaxDetailLength)
                break;
            if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                builder.Append(c);
        }
        // Do not leave half of a surrogate pair at the cut.
        if (builder.Length > 0 && char.IsHighSurrogate(builder[^1]))
            builder.Length--;
        return builder.ToString();
    }

    public static JsonElement Object(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Object ? value : throw Fail(ParseError.MissingField, name);
    }

    public static JsonElement Array(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Array ? value : throw Fail(ParseError.MissingField, name);
    }

    public static string String(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw Fail(ParseError.MissingField, name);
    }

    /// <summary>The string value, or null when the field is missing, null or not a string.</summary>
    public static string? OptionalString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int Int32(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw Fail(ParseError.MissingField, name);
    }

    public static int? OptionalInt32(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;

    public static long Int64(JsonElement parent, string name)
    {
        var value = Property(parent, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw Fail(ParseError.MissingField, name);
    }

    public static long? OptionalInt64(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : null;

    public static bool OptionalBool(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static JsonElement Property(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            ? value
            : throw Fail(ParseError.MissingField, name);
}
