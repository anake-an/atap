using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atap.Core.Rules;

/// <summary>The input a condition reads. The App subscribes to an input only while an enabled rule uses it.</summary>
public enum ConditionKind
{
    /// <summary>The process name of the foreground window.</summary>
    ForegroundApp,

    /// <summary>Local time and day of the week.</summary>
    TimeWindow,

    /// <summary>The connected Wi-Fi network name (SSID).</summary>
    Wifi,

    /// <summary>Battery, power supply and Windows energy saver.</summary>
    Power,

    /// <summary>A condition this version cannot read. It never matches and is never subscribed.</summary>
    Unsupported,
}

/// <summary>
/// One condition of a <see cref="Rule"/>. Stored as a JSON object with a <c>kind</c> field.
/// <see cref="RuleConditionJsonConverter"/> reads and writes every kind, keeps unknown fields, and turns
/// anything it cannot read into an <see cref="UnsupportedCondition"/> instead of failing the whole file.
/// </summary>
[JsonConverter(typeof(RuleConditionJsonConverter))]
public abstract class RuleCondition
{
    // Only the kinds below exist: the converter has to know every one of them.
    private protected RuleCondition()
    {
    }

    public abstract ConditionKind Kind { get; }

    /// <summary>False when the condition is incomplete, for example an app condition with no apps.</summary>
    public abstract bool IsValid { get; }

    /// <summary>Fields this version does not know, kept by the converter and written back unchanged.</summary>
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Whether the condition holds for <paramref name="context"/>. An invalid condition never matches.</summary>
    public abstract bool Matches(RuleContext context);
}
