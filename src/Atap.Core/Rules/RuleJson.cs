using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Rules;

/// <summary>
/// Reads and writes a rules array on its own, for example the <c>settings:rules</c> sync unit or a
/// copied rule. The settings store reads <c>rules</c> as part of settings.json with its own context.
/// </summary>
public static class RuleJson
{
    /// <summary>
    /// The rules in <paramref name="json"/>, or null when it is not a JSON array of rule objects or is
    /// nested deeper than 32 levels. Null entries are dropped. Never throws.
    /// </summary>
    public static List<Rule>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var rules = JsonSerializer.Deserialize(json, RulesJsonContext.Default.ListRule);
            return rules?.Where(rule => rule is not null).ToList();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            // ArgumentException: text that is not valid UTF-16, such as a lone surrogate.
            return null;
        }
    }

    /// <summary>The rules as an indented JSON array, in the settings.json format.</summary>
    public static string Serialize(IEnumerable<Rule> rules) =>
        JsonSerializer.Serialize(rules.ToList(), RulesJsonContext.Default.ListRule);
}

[JsonSourceGenerationOptions(WriteIndented = true, MaxDepth = 32)]
[JsonSerializable(typeof(List<Rule>))]
internal sealed partial class RulesJsonContext : JsonSerializerContext
{
}
