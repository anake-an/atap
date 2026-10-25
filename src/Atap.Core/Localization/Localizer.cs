using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Atap.Core.Localization;

/// <summary>Where string files come from. In the app: embedded resources named "Atap.Strings.&lt;area&gt;.&lt;lang&gt;.json".</summary>
public interface IStringResources
{
    IEnumerable<string> Names { get; }

    Stream? Open(string name);
}

/// <summary>Embedded string files in one or more assemblies.</summary>
public sealed class AssemblyStringResources(params Assembly[] assemblies) : IStringResources
{
    public IEnumerable<string> Names => assemblies.SelectMany(a => a.GetManifestResourceNames());

    public Stream? Open(string name)
    {
        foreach (var assembly in assemblies)
        {
            if (assembly.GetManifestResourceStream(name) is { } stream)
                return stream;
        }
        return null;
    }
}

/// <summary>
/// Looks up UI text. It lists the string files once and loads an area the first time one of its
/// keys is used. Fallback order: the current language, English, then the key itself.
/// </summary>
/// <remarks>A minimal version: no file watching. Text is flat JSON, one file per area and language.</remarks>
public sealed class Localizer : ILocalizer
{
    public const string English = "en";
    public const string Malay = "ms";
    public const string ResourcePrefix = "Atap.Strings.";

    private const string IndexerName = "Item[]";

    private readonly object _gate = new();
    private readonly IStringResources _resources;
    private readonly Dictionary<(string Area, string Language), string> _files = [];
    private readonly Dictionary<(string Area, string Language), Dictionary<string, string>> _loaded = [];
    private readonly HashSet<string> _reportedMissing = new(StringComparer.Ordinal);

    public Localizer(IStringResources resources, string language)
    {
        _resources = resources;
        foreach (var name in resources.Names)
        {
            if (TryParseResourceName(name, out var area, out var lang))
                _files[(area, lang)] = name;
        }
        Language = Normalize(language);
        Culture = CultureFor(Language);
    }

    /// <summary>The localizer that XAML text bindings use. The app sets it at startup.</summary>
    public static Localizer Instance { get; set; } = new(new AssemblyStringResources(), English);

    public string Language { get; private set; }

    public CultureInfo Culture { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    /// <summary>Raised once per key that exists in no language. The app logs it.</summary>
    public event EventHandler<string>? MissingKey;

    public string this[string key] => Lookup(key);

    public string Format(string key, params (string Name, object? Value)[] args)
    {
        var text = Lookup(key);
        foreach (var (name, value) in args)
            text = text.Replace("{" + name + "}", Convert.ToString(value, Culture) ?? string.Empty, StringComparison.Ordinal);
        return text;
    }

    /// <summary>Switches the language. Bound text refreshes through one indexer change.</summary>
    public void SetLanguage(string language)
    {
        var normalized = Normalize(language);
        if (normalized == Language)
            return;
        Language = normalized;
        Culture = CultureFor(normalized);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The language for the "language.choice" setting: "en", "ms", or "system" (and anything else),
    /// which picks Bahasa Melayu when the Windows display language is any ms culture.
    /// </summary>
    public static string ResolveChoice(string? choice, CultureInfo windowsCulture) => choice switch
    {
        English => English,
        Malay => Malay,
        _ => IsMalay(windowsCulture) ? Malay : English,
    };

    /// <summary>en-MY or ms-MY. English text uses Malaysian formats too.</summary>
    public static CultureInfo CultureFor(string language) =>
        CultureInfo.GetCultureInfo(language == Malay ? "ms-MY" : "en-MY");

    /// <summary>Splits "Atap.Strings.clock.en.json" into ("clock", "en").</summary>
    public static bool TryParseResourceName(string name, out string area, out string language)
    {
        area = language = string.Empty;
        if (!name.StartsWith(ResourcePrefix, StringComparison.Ordinal) || !name.EndsWith(".json", StringComparison.Ordinal))
            return false;
        var parts = name[ResourcePrefix.Length..^".json".Length].Split('.');
        if (parts.Length != 2 || parts[0].Length == 0)
            return false;
        area = parts[0];
        language = parts[1];
        return true;
    }

    private static bool IsMalay(CultureInfo culture)
    {
        for (var current = culture; !string.IsNullOrEmpty(current.Name); current = current.Parent)
        {
            if (string.Equals(current.Name, Malay, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string Normalize(string language) => language == Malay ? Malay : English;

    private string Lookup(string key)
    {
        var dot = key.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var area = key[..dot];
            if (TryGet(area, Language, key, out var text) || (Language != English && TryGet(area, English, key, out text)))
                return text;
        }

        ReportMissing(key);
        return key;
    }

    private bool TryGet(string area, string language, string key, out string text)
    {
        lock (_gate)
        {
            if (!_loaded.TryGetValue((area, language), out var strings))
            {
                strings = Load(area, language);
                _loaded[(area, language)] = strings;
            }
            return strings.TryGetValue(key, out text!);
        }
    }

    private Dictionary<string, string> Load(string area, string language)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!_files.TryGetValue((area, language), out var name))
            return strings;
        try
        {
            using var stream = _resources.Open(name);
            if (stream is null)
                return strings;
            using var document = JsonDocument.Parse(stream);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                    strings[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // A broken file behaves like a missing one: English, then the key.
        }
        return strings;
    }

    private void ReportMissing(string key)
    {
        bool first;
        lock (_gate)
            first = _reportedMissing.Add(key);
        if (first)
            MissingKey?.Invoke(this, key);
    }
}
