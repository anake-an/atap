using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Rules;

/// <summary>
/// One entry of <c>rules</c> in settings.json. When every condition in <see cref="When"/> matches,
/// the actions in <see cref="Then"/> apply. Rules are evaluated in list order by <see cref="RuleResolver"/>.
/// Property names are fixed so the file format does not depend on serializer options.
/// </summary>
public sealed class Rule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Missing in the file means enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Conditions that must all match. A rule with none never applies.</summary>
    [JsonPropertyName("when")]
    public List<RuleCondition> When { get; set; } = [];

    [JsonPropertyName("then")]
    public RuleActions Then { get; set; } = new();

    /// <summary>Keys this version does not know. They are written back unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>What a matching rule does. Profiles change what is shown, flags change how.</summary>
public sealed class RuleActions
{
    /// <summary>Profile id to switch to, or null to leave the profile alone.</summary>
    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; set; }

    /// <summary>Hide the notch.</summary>
    [JsonPropertyName("hide")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Hide { get; set; }

    /// <summary>Show the slim line instead of Compact and Idle.</summary>
    [JsonPropertyName("slim")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Slim { get; set; }

    /// <summary>Send Normal and High alerts to the Missed list.</summary>
    [JsonPropertyName("quietAlerts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool QuietAlerts { get; set; }

    /// <summary>Keys this version does not know. They are written back unchanged.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>True when the rule would change nothing: no profile and no flag.</summary>
    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrWhiteSpace(Profile) && !Hide && !Slim && !QuietAlerts;
}
