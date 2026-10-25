using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Atap.Core.LocalApi;

/// <summary>
/// Reads and writes local API messages. Each message is one UTF-8 JSON object with an <c>op</c> field:
/// <list type="bullet">
/// <item><c>{"op":"ping"}</c></item>
/// <item><c>{"op":"push","id":"backup","title":"Backup NAS","subtitle":"Disk 2","progress":64,"state":"running","icon":"backup","accent":"blue","ttl":600}</c></item>
/// <item><c>{"op":"end","id":"backup"}</c></item>
/// <item><c>{"op":"alert","title":"Backup done","subtitle":"12 GB","icon":"check"}</c></item>
/// </list>
/// Field names and enum values ignore case. A null value counts as left out. Unknown and repeated fields
/// are refused. <c>ttl</c> is whole seconds, or a text duration such as <c>"10m"</c> or <c>"1h30m"</c>.
/// Text is cleaned with <see cref="TextSanitizer"/> before limits are checked. Pushes only display:
/// no field carries a URL, image or action.
/// </summary>
public static class PushProtocol
{
    /// <summary>Largest message in UTF-8 bytes, for requests and responses.</summary>
    public const int MaxMessageBytes = 8 * 1024;

    public const int MaxIdLength = 32;

    /// <summary>Counted in Unicode scalar values after cleaning.</summary>
    public const int MaxTitleLength = 48;

    /// <summary>Counted in Unicode scalar values after cleaning.</summary>
    public const int MaxSubtitleLength = 64;

    public const double MinProgress = 0;

    public const double MaxProgress = 100;

    public static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan MaxTtl = TimeSpan.FromHours(24);

    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    /// <summary>Messages are flat objects, so anything deeper is refused early.</summary>
    private const int MaxJsonDepth = 4;

    /// <summary>Digits allowed in one part of a text duration. Six keeps any sum far from overflow.</summary>
    private const int MaxDurationDigits = 6;

    private const char ByteOrderMark = (char)0xFEFF;

    private const string UnknownOpMessage = "The op is not known. Use ping, push, end or alert.";
    private const string TooLargeMessage = "The message is over 8192 bytes.";
    private const string IdMessage = "The field 'id' must be 1 to 32 lower-case letters, digits or hyphens, and start with a letter or digit.";
    private const string ProgressMessage = "The field 'progress' must be a number from 0 to 100.";
    private const string TtlMessage = "The field 'ttl' must be whole seconds from 10 s to 24 h, such as 600 or \"10m\".";

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = MaxJsonDepth };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly HashSet<string> PingFields = Fields("op");
    private static readonly HashSet<string> PushFields = Fields("op", "id", "title", "subtitle", "progress", "state", "icon", "accent", "ttl");
    private static readonly HashSet<string> EndFields = Fields("op", "id");
    private static readonly HashSet<string> AlertFields = Fields("op", "title", "subtitle", "icon");

    /// <summary>Parses one request. Never throws: bad input gives an <see cref="ApiError"/>.</summary>
    public static ApiParseResult Parse(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return Fail(ApiErrorCode.Invalid, "The message is empty.");
        if (IsTooLarge(json))
            return Fail(ApiErrorCode.TooLarge, TooLargeMessage);

        var start = json[0] == ByteOrderMark ? 1 : 0;
        try
        {
            using var document = JsonDocument.Parse(json.AsMemory(start), DocumentOptions);
            return Read(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            // InvalidOperationException comes from text holding escaped lone surrogates.
            return Fail(ApiErrorCode.Invalid, "The message is not valid JSON.");
        }
    }

    /// <summary>Parses one request from the UTF-8 payload of a frame. Never throws.</summary>
    public static ApiParseResult Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaxMessageBytes)
            return Fail(ApiErrorCode.TooLarge, TooLargeMessage);

        string json;
        try
        {
            json = StrictUtf8.GetString(utf8);
        }
        catch (ArgumentException)
        {
            return Fail(ApiErrorCode.Invalid, "The message is not valid UTF-8.");
        }
        return Parse(json);
    }

    /// <summary>
    /// Checks a request against every limit. Text must already be clean, which is always true for requests
    /// from <see cref="Parse(string?)"/>. Returns null when the request is valid.
    /// </summary>
    public static ApiError? Validate(ApiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request switch
        {
            PingRequest => null,
            EndRequest end => CheckId(end.Id),
            PushRequest push => CheckId(push.Id)
                ?? CheckText("title", push.Title, MaxTitleLength, required: true)
                ?? CheckText("subtitle", push.Subtitle, MaxSubtitleLength, required: false)
                ?? CheckProgress(push.Progress)
                ?? CheckChoice("state", ApiNames.IsDefined(push.State), ApiNames.States)
                ?? CheckChoice("icon", push.Icon is not { } icon || ApiNames.IsDefined(icon), ApiNames.Icons)
                ?? CheckChoice("accent", push.Accent is not { } accent || ApiNames.IsDefined(accent), ApiNames.Accents)
                ?? CheckTtl(push.Ttl),
            AlertRequest alert => CheckText("title", alert.Title, MaxTitleLength, required: true)
                ?? CheckText("subtitle", alert.Subtitle, MaxSubtitleLength, required: false)
                ?? CheckChoice("icon", alert.Icon is not { } icon || ApiNames.IsDefined(icon), ApiNames.Icons),
            _ => new ApiError(ApiErrorCode.UnknownOp, UnknownOpMessage),
        };
    }

    /// <summary>True when <paramref name="id"/> matches <c>^[a-z0-9][a-z0-9-]{0,31}$</c>.</summary>
    public static bool IsValidId([NotNullWhen(true)] string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || id[0] == '-')
            return false;
        foreach (var c in id)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Writes a request as compact JSON that <see cref="Parse(string?)"/> reads back as an equal request.
    /// Throws <see cref="ArgumentException"/> when the request fails <see cref="Validate"/>.
    /// </summary>
    public static string Format(ApiRequest request)
    {
        var error = Validate(request);
        if (error is not null)
            throw new ArgumentException(error.Message, nameof(request));

        return Write(writer =>
        {
            writer.WriteString("op", ApiNames.Of(request.Op));
            switch (request)
            {
                case EndRequest end:
                    writer.WriteString("id", end.Id);
                    break;
                case PushRequest push:
                    writer.WriteString("id", push.Id);
                    writer.WriteString("title", push.Title);
                    if (push.Subtitle is not null)
                        writer.WriteString("subtitle", push.Subtitle);
                    if (push.Progress is { } progress)
                        writer.WriteNumber("progress", progress);
                    writer.WriteString("state", ApiNames.Of(push.State));
                    if (push.Icon is { } icon)
                        writer.WriteString("icon", ApiNames.Of(icon));
                    if (push.Accent is { } accent)
                        writer.WriteString("accent", ApiNames.Of(accent));
                    if (push.Ttl is { } ttl)
                        writer.WriteNumber("ttl", (long)ttl.TotalSeconds);
                    break;
                case AlertRequest alert:
                    writer.WriteString("title", alert.Title);
                    if (alert.Subtitle is not null)
                        writer.WriteString("subtitle", alert.Subtitle);
                    if (alert.Icon is { } alertIcon)
                        writer.WriteString("icon", ApiNames.Of(alertIcon));
                    break;
            }
        });
    }

    /// <summary>Writes a response as compact JSON that <see cref="TryParseResponse"/> reads back as an equal response.</summary>
    public static string FormatResponse(ApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return Write(writer =>
        {
            writer.WriteBoolean("ok", response.Ok);
            if (response.Error is { } code)
                writer.WriteString("error", ApiNames.Of(code));
            if (response.Message is not null)
                writer.WriteString("message", response.Message);
            if (response.Version is not null)
                writer.WriteString("version", response.Version);
        });
    }

    /// <summary>
    /// Reads a response. Unknown fields are ignored so an older client can read a newer server. Returns false
    /// for anything else that is wrong. Never throws.
    /// </summary>
    public static bool TryParseResponse(string? json, [NotNullWhen(true)] out ApiResponse? response)
    {
        response = null;
        if (string.IsNullOrEmpty(json) || IsTooLarge(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json, DocumentOptions);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            bool? ok = null;
            ApiErrorCode? code = null;
            string? message = null;
            string? version = null;
            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value;
                switch (property.Name.ToLowerInvariant())
                {
                    case "ok" when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        ok = value.GetBoolean();
                        break;
                    case "error" when value.ValueKind == JsonValueKind.String:
                        if (!ApiNames.TryParse(value.GetString(), out ApiErrorCode parsed))
                            return false;
                        code = parsed;
                        break;
                    case "message" when value.ValueKind == JsonValueKind.String:
                        message = value.GetString();
                        break;
                    case "version" when value.ValueKind == JsonValueKind.String:
                        version = value.GetString();
                        break;
                }
            }

            // ok must be present and agree with whether an error code was sent.
            if (ok is null || ok.Value == (code is not null))
                return false;
            response = new ApiResponse { Error = code, Message = message, Version = version };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a duration such as <c>"45s"</c>, <c>"10m"</c>, <c>"2h"</c> or <c>"1h30m15s"</c>, or bare digits as
    /// seconds. Units go from hours down to seconds, each at most once, and ignore case. Range is not checked here.
    /// </summary>
    public static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrEmpty(text))
            return false;

        long seconds = 0;
        var previousUnit = int.MaxValue;   // Seconds per unit of the part before. Units must get smaller.
        var index = 0;
        while (index < text.Length)
        {
            var digitsStart = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
                index++;
            var digits = index - digitsStart;
            if (digits is 0 or > MaxDurationDigits)
                return false;
            var number = long.Parse(text.AsSpan(digitsStart, digits), NumberStyles.None, CultureInfo.InvariantCulture);

            if (index == text.Length)
            {
                // Bare digits are seconds, but only as the whole text.
                if (digitsStart != 0)
                    return false;
                seconds = number;
                break;
            }

            var unit = char.ToLowerInvariant(text[index]) switch
            {
                'h' => 3600,
                'm' => 60,
                's' => 1,
                _ => 0,
            };
            if (unit == 0 || unit >= previousUnit)
                return false;
            previousUnit = unit;
            seconds += number * unit;
            index++;
        }

        duration = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>Writes whole seconds as <c>"1h30m"</c>, <c>"10m"</c> or <c>"45s"</c>. Zero is <c>"0s"</c>.</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration is negative.");

        var total = (long)duration.TotalSeconds;
        if (total == 0)
            return "0s";

        var builder = new StringBuilder();
        Append(total / 3600, 'h');
        Append(total / 60 % 60, 'm');
        Append(total % 60, 's');
        return builder.ToString();

        void Append(long value, char unit)
        {
            if (value > 0)
                builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append(unit);
        }
    }

    private static ApiParseResult Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return Fail(ApiErrorCode.Invalid, "The message must be a JSON object.");

        var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, property.Value))
                return Fail(ApiErrorCode.Invalid, $"The field {Quote(property.Name)} appears more than once.");
        }

        if (!values.TryGetValue("op", out var opValue) || opValue.ValueKind == JsonValueKind.Null)
            return Fail(ApiErrorCode.Invalid, "The field 'op' is required.");
        if (opValue.ValueKind != JsonValueKind.String)
            return Fail(ApiErrorCode.Invalid, "The field 'op' must be text.");
        if (!ApiNames.TryParse(opValue.GetString(), out ApiOp op))
            return Fail(ApiErrorCode.UnknownOp, UnknownOpMessage);

        var allowed = op switch
        {
            ApiOp.Ping => PingFields,
            ApiOp.Push => PushFields,
            ApiOp.End => EndFields,
            _ => AlertFields,
        };
        foreach (var property in root.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return Fail(ApiErrorCode.Invalid, $"The field {Quote(property.Name)} is not allowed with {ApiNames.Of(op)}.");
        }

        var fields = new FieldReader(values);
        ApiRequest request = op switch
        {
            ApiOp.Ping => new PingRequest(),
            ApiOp.End => new EndRequest(fields.Id()),
            ApiOp.Push => new PushRequest(fields.Id(), fields.RequiredText("title"))
            {
                Subtitle = fields.OptionalText("subtitle"),
                Progress = fields.Progress(),
                State = fields.Choice<PushState>("state", ApiNames.States) ?? PushState.Running,
                Icon = fields.Choice<PushIcon>("icon", ApiNames.Icons),
                Accent = fields.Choice<PushAccent>("accent", ApiNames.Accents),
                Ttl = fields.Ttl(),
            },
            _ => new AlertRequest(fields.RequiredText("title"))
            {
                Subtitle = fields.OptionalText("subtitle"),
                Icon = fields.Choice<PushIcon>("icon", ApiNames.Icons),
            },
        };
        if (fields.Error is not null)
            return ApiParseResult.Failure(fields.Error);

        var error = Validate(request);
        return error is null ? ApiParseResult.Success(request) : ApiParseResult.Failure(error);
    }

    private static bool IsTooLarge(string json) =>
        json.Length > MaxMessageBytes || Encoding.UTF8.GetByteCount(json) > MaxMessageBytes;

    private static ApiError? CheckId(string? id) =>
        IsValidId(id) ? null : new ApiError(ApiErrorCode.Invalid, IdMessage);

    private static ApiError? CheckText(string name, string? value, int maxLength, bool required)
    {
        if (value is null)
            return required ? new ApiError(ApiErrorCode.Invalid, $"The field '{name}' is required.") : null;
        if (value.Length == 0)
        {
            // Parse never gives empty text, so an empty value would not survive Format and Parse.
            return new ApiError(ApiErrorCode.Invalid, required ? $"The field '{name}' is required." : $"The field '{name}' is empty. Leave it out instead.");
        }
        if (TextSanitizer.Sanitize(value) != value)
            return new ApiError(ApiErrorCode.Invalid, $"The field '{name}' has characters that are not allowed.");
        if (TextSanitizer.CountCharacters(value) > maxLength)
            return new ApiError(ApiErrorCode.Invalid, $"The field '{name}' is longer than {maxLength} characters.");
        return null;
    }

    private static ApiError? CheckProgress(double? progress) =>
        progress is not { } value || (double.IsFinite(value) && value is >= MinProgress and <= MaxProgress)
            ? null
            : new ApiError(ApiErrorCode.Invalid, ProgressMessage);

    private static ApiError? CheckChoice(string name, bool isDefined, IReadOnlyList<string> names) =>
        isDefined ? null : new ApiError(ApiErrorCode.Invalid, OneOf(name, names));

    private static ApiError? CheckTtl(TimeSpan? ttl) =>
        ttl is not { } value || (value >= MinTtl && value <= MaxTtl && value.Ticks % TimeSpan.TicksPerSecond == 0)
            ? null
            : new ApiError(ApiErrorCode.Invalid, TtlMessage);

    private static string OneOf(string name, IReadOnlyList<string> names) =>
        $"The field '{name}' must be one of: {string.Join(", ", names)}.";

    private static ApiParseResult Fail(ApiErrorCode code, string message) => ApiParseResult.Failure(new ApiError(code, message));

    /// <summary>Quotes a field name for an error message. It came from the request, so it is cleaned and cut short.</summary>
    private static string Quote(string name)
    {
        var builder = new StringBuilder("'");
        var count = 0;
        foreach (var rune in TextSanitizer.Sanitize(name).EnumerateRunes())
        {
            if (++count > MaxIdLength)
            {
                builder.Append("...");
                break;
            }
            builder.Append(rune.ToString());
        }
        return builder.Append('\'').ToString();
    }

    private static HashSet<string> Fields(params string[] names) => new(names, StringComparer.OrdinalIgnoreCase);

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Reads typed fields and keeps the first error, so a request can be built in one expression.</summary>
    private sealed class FieldReader(Dictionary<string, JsonElement> values)
    {
        public ApiError? Error { get; private set; }

        /// <summary>The raw id. Its rule is checked by <see cref="Validate"/>.</summary>
        public string Id()
        {
            if (!TryGet("id", out var value))
                return Refuse("The field 'id' is required.", string.Empty);
            if (value.ValueKind != JsonValueKind.String)
                return Refuse("The field 'id' must be text.", string.Empty);
            return value.GetString() ?? string.Empty;
        }

        /// <summary>Cleaned text. Missing, or empty after cleaning, is an error.</summary>
        public string RequiredText(string name) =>
            OptionalText(name) ?? Refuse($"The field '{name}' is required.", string.Empty);

        /// <summary>Cleaned text, or null when left out or empty after cleaning.</summary>
        public string? OptionalText(string name)
        {
            if (!TryGet(name, out var value))
                return null;
            if (value.ValueKind != JsonValueKind.String)
                return Refuse<string?>($"The field '{name}' must be text.", null);

            var text = TextSanitizer.Sanitize(value.GetString());
            return text.Length == 0 ? null : text;
        }

        public double? Progress()
        {
            if (!TryGet("progress", out var value))
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
                return number;
            return Refuse<double?>(ProgressMessage, null);
        }

        public T? Choice<T>(string name, IReadOnlyList<string> names)
            where T : struct, Enum
        {
            if (!TryGet(name, out var value))
                return null;
            if (value.ValueKind == JsonValueKind.String && ApiNames.Find(names, value.GetString(), out T choice))
                return choice;
            return Refuse<T?>(OneOf(name, names), null);
        }

        public TimeSpan? Ttl()
        {
            if (!TryGet("ttl", out var value))
                return null;

            long seconds;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
                seconds = number;
            else if (value.ValueKind == JsonValueKind.String && TryParseDuration(value.GetString(), out var parsed))
                seconds = (long)parsed.TotalSeconds;
            else
                return Refuse<TimeSpan?>(TtlMessage, null);

            // Checked here as well as in Validate, so a huge number never reaches TimeSpan.
            if (seconds < (long)MinTtl.TotalSeconds || seconds > (long)MaxTtl.TotalSeconds)
                return Refuse<TimeSpan?>(TtlMessage, null);
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>True when the field is present and not null.</summary>
        private bool TryGet(string name, out JsonElement value) =>
            values.TryGetValue(name, out value) && value.ValueKind != JsonValueKind.Null;

        /// <summary>Keeps the first error and returns <paramref name="fallback"/> so building can go on.</summary>
        private T Refuse<T>(string message, T fallback)
        {
            Error ??= new ApiError(ApiErrorCode.Invalid, message);
            return fallback;
        }
    }
}
