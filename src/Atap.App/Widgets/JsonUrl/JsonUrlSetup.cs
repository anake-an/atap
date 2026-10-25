using Atap.Core.Custom;
using Atap.Core.Net;
using Atap.Core.Storage;
using Atap.Core.Widgets;

namespace Atap.Widgets.JsonUrl;

/// <summary>
/// What one JSON URL instance fetches and how it shows the value, read from its settings and checked once.
/// The widget and the Test button in its settings both use it, so a test runs exactly what the notch will run.
/// </summary>
/// <param name="Url">An absolute http or https link.</param>
/// <param name="Template">The checked mapping from the reply to the value.</param>
/// <param name="Headers">The sign-in header from the secret store, or null when there is none.</param>
/// <param name="Interval">How often to fetch. Never below <see cref="MinInterval"/>.</param>
/// <param name="Title">The name on the card: the user's own, or the site's host.</param>
internal sealed record JsonUrlSetup(Uri Url, ValueTemplate Template, IReadOnlyDictionary<string, string>? Headers, TimeSpan Interval, string Title)
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(60);

    /// <summary>Reads and checks the settings. Null with the string key of a message when something needs fixing.</summary>
    public static JsonUrlSetup? Read(SettingValues settings, ISecretStore secrets, out string? problemKey)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(secrets);

        if (Link(settings.GetString(JsonUrlDescriptor.UrlKey)) is not { } url)
        {
            problemKey = "json-url.noLink";
            return null;
        }
        if (!TryReadHeaders(settings, secrets, out var headers, out problemKey))
            return null;
        if (!ValueTemplate.TryCreate(Spec(settings), out var template, out var error))
        {
            problemKey = ProblemKey(error);
            return null;
        }

        // The definition clamps to 60 already. This keeps the floor if a stored value ever skips it.
        var seconds = Math.Max(MinInterval.TotalSeconds, settings.GetInt(JsonUrlDescriptor.IntervalKey));
        var title = settings.GetString(JsonUrlDescriptor.TitleKey).Trim();
        problemKey = null;
        return new JsonUrlSetup(url, template, headers, TimeSpan.FromSeconds(seconds), title.Length > 0 ? title : url.Host);
    }

    /// <summary>
    /// One fetch of the link, capped at <see cref="PayloadReader.JsonUrlMaxBytes"/>.
    /// A request with a sign-in header is never written to the disk cache.
    /// </summary>
    public FetchRequest Request(TimeSpan maxAge) => new(Url, maxAge)
    {
        Headers = Headers,
        DiskCache = Headers is null,
        MaxBytes = PayloadReader.JsonUrlMaxBytes,
    };

    /// <summary>The message for a fetch that brought nothing back.</summary>
    public static string FailureKey(FetchError error) => error switch
    {
        FetchError.Offline or FetchError.OfflineMode => "json-url.offline",
        FetchError.HostNotAllowed or FetchError.InsecureHttp or FetchError.RedirectRefused => "json-url.blocked",
        FetchError.TooLarge => "json-url.tooLarge",
        FetchError.HttpStatus => "json-url.refused",
        _ => "json-url.failed",
    };

    /// <summary>An absolute http or https link, or null.</summary>
    internal static Uri? Link(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) ? uri : null;

    /// <summary>
    /// Lets people type "data.temperature" or "[0].name" without the leading "$" the path reader needs.
    /// Empty means the whole reply.
    /// </summary>
    internal static string Path(string? text)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return "$";
        if (text[0] == '$')
            return text;
        return text[0] == '[' ? "$" + text : "$." + text;
    }

    /// <summary>"Name: value" from the secret. Header names follow the HTTP token rules and values have no line breaks.</summary>
    internal static bool TryParseHeader(string? text, out string name, out string value)
    {
        name = value = string.Empty;
        var colon = text?.IndexOf(':') ?? -1;
        if (text is null || colon <= 0)
            return false;
        name = text[..colon].Trim();
        value = text[(colon + 1)..].Trim();
        return name.Length > 0 && value.Length > 0 && name.All(IsTokenChar) && !value.Any(char.IsControl);
    }

    private static bool TryReadHeaders(SettingValues settings, ISecretStore secrets, out IReadOnlyDictionary<string, string>? headers, out string? problemKey)
    {
        headers = null;
        problemKey = null;
        if (settings.GetSecretRef(JsonUrlDescriptor.HeaderKey) is not { } reference)
            return true;
        if (secrets.Get(reference) is not { } text)
        {
            problemKey = "json-url.problem.headerMissing";
            return false;
        }
        if (!TryParseHeader(text, out var name, out var value))
        {
            problemKey = "json-url.problem.header";
            return false;
        }
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = value };
        return true;
    }

    private static ValueTemplateSpec Spec(SettingValues settings)
    {
        var kind = settings.GetString(JsonUrlDescriptor.ShowKey) switch
        {
            "text" => ValueTemplateKind.Text,
            "progress" => ValueTemplateKind.Progress,
            "list" => ValueTemplateKind.List,
            "status" => ValueTemplateKind.StatusDot,
            _ => ValueTemplateKind.Number,
        };
        var unit = settings.GetString(JsonUrlDescriptor.UnitKey).Trim();
        return new ValueTemplateSpec(kind, Path(settings.GetString(JsonUrlDescriptor.PathKey)))
        {
            Unit = unit.Length == 0 ? null : unit[..Math.Min(unit.Length, ValueTemplateSpec.MaxUnitLength)],
            Max = kind == ValueTemplateKind.Progress ? settings.GetDouble(JsonUrlDescriptor.MaxKey) : null,
            ItemLabelPath = OptionalPath(settings.GetString(JsonUrlDescriptor.ItemLabelKey)),
            ItemValuePath = OptionalPath(settings.GetString(JsonUrlDescriptor.ItemValueKey)),
            Thresholds = settings.GetBool(JsonUrlDescriptor.AlertsKey) ? Thresholds(settings) : null,
            NoValueWords = StatusWords.DefaultNoValue,
        };
    }

    private static ThresholdSpec Thresholds(SettingValues settings)
    {
        var warning = settings.GetDouble(JsonUrlDescriptor.WarnAtKey);
        var critical = settings.GetDouble(JsonUrlDescriptor.CriticalAtKey);
        var direction = settings.GetString(JsonUrlDescriptor.BadSideKey) == "below" ? ThresholdDirection.Below : ThresholdDirection.Above;
        // A threshold at 0 has no band of its own, so it needs one set. 0 keeps the level exact.
        return new ThresholdSpec(warning, critical, direction) { Hysteresis = warning == 0 || critical == 0 ? 0 : null };
    }

    private static string? OptionalPath(string? text) => string.IsNullOrWhiteSpace(text) ? null : Path(text);

    private static string ProblemKey(ValueTemplateError error) => error.Field switch
    {
        ValueTemplateField.ValuePath => "json-url.problem.path",
        ValueTemplateField.ItemLabelPath or ValueTemplateField.ItemValuePath => "json-url.problem.itemPath",
        ValueTemplateField.Range => "json-url.problem.range",
        ValueTemplateField.Thresholds => "json-url.problem.thresholds",
        _ => "json-url.problem.path",
    };

    private static bool IsTokenChar(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);
}
