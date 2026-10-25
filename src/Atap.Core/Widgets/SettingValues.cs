using System.Globalization;
using System.Text.Json;

namespace Atap.Core.Widgets;

/// <summary>
/// Typed read access to stored settings. Missing or invalid values fall back
/// to the default in the matching <see cref="SettingDefinition"/>.
/// </summary>
public sealed class SettingValues
{
    /// <summary>Prefix of a stored secret reference. The value itself is never in the settings files.</summary>
    public const string SecretPrefix = "secret:";

    private const int DefaultMaxItems = 50;

    private readonly Dictionary<string, SettingDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, JsonElement> _values;

    public SettingValues(
        IEnumerable<SettingDefinition> definitions,
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        _definitions = new Dictionary<string, SettingDefinition>(StringComparer.Ordinal);
        // Later definitions of the same key are ignored, so one bad descriptor cannot break the rest.
        foreach (var definition in definitions)
            _definitions.TryAdd(definition.Key, definition);
        _values = values ?? new Dictionary<string, JsonElement>();
    }

    public static SettingValues Empty { get; } = new([], null);

    /// <summary>The stored values, including keys without a definition.</summary>
    public IReadOnlyDictionary<string, JsonElement> Raw => _values;

    public bool GetBool(string key)
    {
        if (_values.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        return DefaultOf<bool>(key);
    }

    /// <summary>The stored number, clamped to the definition's Min and Max.</summary>
    public int GetInt(string key)
    {
        var number = _values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var stored)
            ? stored
            : DefaultOf<int>(key);
        return (int)Clamp(key, number);
    }

    /// <summary>The stored number, clamped to the definition's Min and Max.</summary>
    public double GetDouble(string key)
    {
        double number;
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var stored) && double.IsFinite(stored))
            number = stored;
        else
            number = DefaultOf(key) switch { double d => d, int i => i, _ => 0 };
        return Clamp(key, number);
    }

    public string GetString(string key)
    {
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString() ?? string.Empty;
            if (IsAllowed(key, text))
                return text;
        }
        return DefaultOf<string>(key) ?? string.Empty;
    }

    /// <summary>A time stored as "HH:mm".</summary>
    public TimeOnly GetTime(string key)
    {
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            && TryParseTime(value.GetString(), out var stored))
        {
            return stored;
        }
        return DefaultOf(key) switch
        {
            TimeOnly time => time,
            string text when TryParseTime(text, out var parsed) => parsed,
            _ => default,
        };
    }

    /// <summary>A duration stored as whole seconds.</summary>
    public TimeSpan GetDuration(string key)
    {
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var seconds) && seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
        {
            return TimeSpan.FromSeconds(Clamp(key, seconds));
        }
        return DefaultOf(key) switch
        {
            TimeSpan span => span,
            int number => TimeSpan.FromSeconds(number),
            _ => TimeSpan.Zero,
        };
    }

    /// <summary>A list of strings, at most the definition's MaxItems (50 when not set). Non-string items are skipped.</summary>
    public IReadOnlyList<string> GetStringList(string key)
    {
        var maxItems = _definitions.TryGetValue(key, out var definition) ? definition.MaxItems ?? DefaultMaxItems : DefaultMaxItems;
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .Take(maxItems)
                .ToList();
        }
        return DefaultOf(key) is IEnumerable<string> fallback ? fallback.Take(maxItems).ToList() : [];
    }

    /// <summary>The stored "secret:&lt;id&gt;" reference, or null when the value is missing or not a reference.</summary>
    public string? GetSecretRef(string key)
    {
        if (_values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (text is not null && text.StartsWith(SecretPrefix, StringComparison.Ordinal) && text.Length > SecretPrefix.Length)
                return text;
        }
        return null;
    }

    private static bool TryParseTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private double Clamp(string key, double number)
    {
        if (!_definitions.TryGetValue(key, out var definition))
            return number;
        if (definition.Min is { } min && number < min)
            number = min;
        if (definition.Max is { } max && number > max)
            number = max;
        return number;
    }

    private bool IsAllowed(string key, string text) =>
        !_definitions.TryGetValue(key, out var definition)
        || definition.Type != SettingType.Choice
        || definition.Choices is null
        || definition.Choices.Contains(text);

    private object? DefaultOf(string key) =>
        _definitions.TryGetValue(key, out var definition) ? definition.DefaultValue : null;

    private T? DefaultOf<T>(string key) => DefaultOf(key) is T typed ? typed : default;
}
