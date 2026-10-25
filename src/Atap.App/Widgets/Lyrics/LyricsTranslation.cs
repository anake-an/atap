using Atap.Core.Custom;
using Atap.Core.Net;
using Atap.Core.Widgets;
using Atap.Widgets.JsonUrl;

namespace Atap.Widgets.Lyrics;

/// <summary>
/// Translation through the link the user enters, never a built-in service. The line goes in place of
/// <see cref="TextPlaceholder"/> and the language in place of <see cref="LanguagePlaceholder"/>, and the reply is read
/// with <see cref="JsonPathLite"/>. The line may sit in the path or query only, never in the host. Requests stay out of
/// the disk cache, and neither the line nor the filled link is ever logged.
/// </summary>
/// <param name="Template">The link as the user typed it, with <see cref="TextPlaceholder"/>.</param>
/// <param name="Language">The language code put in place of <see cref="LanguagePlaceholder"/>.</param>
/// <param name="Path">Where the translated text is in the reply.</param>
internal sealed record LyricsTranslation(string Template, string Language, JsonPathLite Path)
{
    public const string TextPlaceholder = "{text}";
    public const string LanguagePlaceholder = "{lang}";

    /// <summary>Lyrics lines are short. A longer one is not sent.</summary>
    public const int MaxLineLength = 500;

    /// <summary>A translation reply is small.</summary>
    public const int MaxReplyBytes = 64 * 1024;

    /// <summary>The longest translation shown.</summary>
    public const int MaxTranslationLength = 1000;

    /// <summary>Null when translation is off: no link, a link without <see cref="TextPlaceholder"/>, or a path that does not read.</summary>
    public static LyricsTranslation? Read(SettingValues settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var template = settings.GetString(LyricsDescriptor.TranslateUrlKey).Trim();
        if (!template.Contains(TextPlaceholder, StringComparison.Ordinal))
            return null;
        var language = settings.GetString(LyricsDescriptor.TranslateToKey);
        if (!JsonPathLite.TryParse(JsonUrlSetup.Path(settings.GetString(LyricsDescriptor.TranslatePathKey)), out var path, out _))
            return null;
        var translation = new LyricsTranslation(template, language, path);
        // A sample line shows whether the line would change the host.
        return translation.Fill("Atap") is null ? null : translation;
    }

    /// <summary>One request for <paramref name="line"/>, or null when it cannot be sent.</summary>
    public FetchRequest? Request(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxLineLength || Fill(line) is not { } url)
            return null;
        return new FetchRequest(url, TimeSpan.FromDays(1)) { DiskCache = false, MaxBytes = MaxReplyBytes };
    }

    /// <summary>The translated text in the reply, or null when there is none.</summary>
    public string? ReadReply(FetchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Body.IsEmpty || result.Status is not (FetchStatus.Fresh or FetchStatus.Cached or FetchStatus.Stale))
            return null;
        var payload = PayloadReader.Read(result.Body.Span, PayloadFormat.Auto, MaxReplyBytes);
        if (!payload.IsOk || !Path.Evaluate(payload.Root).TryGetFirst(out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        var text = value.GetString()?.Trim() ?? string.Empty;
        if (text.Length > MaxTranslationLength)
            text = text[..MaxTranslationLength].TrimEnd() + "…";
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// The link with both placeholders filled. Null unless it is http or https and its host is the same as with
    /// no line at all, so the line can never pick where it goes.
    /// </summary>
    private Uri? Fill(string line)
    {
        var language = Uri.EscapeDataString(Language);
        var empty = Link(Template.Replace(TextPlaceholder, string.Empty, StringComparison.Ordinal).Replace(LanguagePlaceholder, language, StringComparison.Ordinal));
        var filled = Link(Template.Replace(TextPlaceholder, Uri.EscapeDataString(line), StringComparison.Ordinal).Replace(LanguagePlaceholder, language, StringComparison.Ordinal));
        if (empty is null || filled is null)
            return null;
        return filled.Scheme == empty.Scheme
            && string.Equals(filled.IdnHost, empty.IdnHost, StringComparison.OrdinalIgnoreCase)
            && filled.Port == empty.Port
            && string.IsNullOrEmpty(filled.UserInfo)
            ? filled
            : null;
    }

    private static Uri? Link(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && uri.Host.Length > 0
            ? uri
            : null;
}
