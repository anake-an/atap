using System.Text.Json;

namespace Atap.Core.Rules;

/// <summary>
/// A condition this version cannot read: an unknown <c>kind</c>, for example from a newer Atap on
/// another PC, or a known kind with values that do not parse. It never matches, so its rule never
/// applies, and the original JSON is written back unchanged.
/// </summary>
public sealed class UnsupportedCondition : RuleCondition
{
    internal UnsupportedCondition(JsonElement raw, string? kindName, bool isKnownKind)
    {
        Raw = raw;
        KindName = kindName;
        IsKnownKind = isKnownKind;
    }

    public override ConditionKind Kind => ConditionKind.Unsupported;

    /// <summary>The JSON value exactly as read.</summary>
    public JsonElement Raw { get; }

    /// <summary>The <c>kind</c> text, or null when it was missing or not a string.</summary>
    public string? KindName { get; }

    /// <summary>True when the kind is known but its values are invalid.</summary>
    public bool IsKnownKind { get; }

    public override bool IsValid => false;

    public override bool Matches(RuleContext context) => false;
}
