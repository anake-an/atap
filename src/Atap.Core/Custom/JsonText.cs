using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Atap.Core.Custom;

/// <summary>
/// Reads JSON strings and member names without throwing. System.Text.Json throws when an escape such as
/// <c>\uD800</c> leaves a lone surrogate. Remote payloads can hold that, so these fall back to unescaping
/// the raw text and keep the lone surrogate for <see cref="ValueText.Clean"/> to replace.
/// <see cref="PayloadReader"/> rewrites those escapes before parsing, so this slow path only runs for JSON parsed elsewhere.
/// </summary>
internal static class JsonText
{
    public static string GetString(JsonElement element)
    {
        try
        {
            return element.GetString() ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return Unescape(element.GetRawText());
        }
    }

    public static string GetName(JsonProperty property)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            return Unescape(Encoding.UTF8.GetString(JsonMarshal.GetRawUtf8PropertyName(property)));
        }
    }

    /// <summary>Unescapes JSON string text, with or without its quotes. Unknown escapes are kept as written.</summary>
    private static string Unescape(string raw)
    {
        var text = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw.AsSpan(1, raw.Length - 2) : raw.AsSpan();
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                builder.Append(c);
                continue;
            }

            var escaped = text[++i];
            switch (escaped)
            {
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u' when i + 4 < text.Length
                    && ushort.TryParse(text.Slice(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code):
                    builder.Append((char)code);
                    i += 4;
                    break;
                case '"' or '\\' or '/':
                    builder.Append(escaped);
                    break;
                default:
                    builder.Append('\\').Append(escaped);
                    break;
            }
        }
        return builder.ToString();
    }
}
