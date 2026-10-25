namespace Atap.Core.Rules;

/// <summary>Why a rule can never apply. Settings shows it next to the rule.</summary>
public enum RuleIssue
{
    /// <summary>The rule has no conditions.</summary>
    NoConditions,

    /// <summary>A condition has a kind this version does not know, for example from a newer Atap.</summary>
    UnknownCondition,

    /// <summary>A condition is incomplete or has values that do not parse.</summary>
    InvalidCondition,

    /// <summary>The rule neither switches profile nor sets a flag.</summary>
    NoActions,

    /// <summary>The rule names a profile that does not exist.</summary>
    MissingProfile,
}

/// <summary>Finds rules that can never apply. <see cref="RuleResolver"/> skips them, enabled or not.</summary>
public static class RuleChecker
{
    /// <summary>
    /// The first problem with <paramref name="rule"/>, or null when it can apply. Whether the rule is
    /// enabled is not a problem and is not checked here.
    /// </summary>
    /// <param name="profileIds">Ids of the profiles that exist. <see cref="RuleResolver.DefaultProfileId"/> always exists.</param>
    public static RuleIssue? Check(Rule rule, IReadOnlySet<string> profileIds)
    {
        var conditions = ConditionsOf(rule);
        if (conditions.Count == 0)
            return RuleIssue.NoConditions;
        if (conditions.Exists(c => c is UnsupportedCondition { IsKnownKind: false }))
            return RuleIssue.UnknownCondition;
        if (conditions.Exists(c => !c.IsValid))
            return RuleIssue.InvalidCondition;

        var actions = rule.Then;
        if (actions is null || actions.IsEmpty)
            return RuleIssue.NoActions;
        if (!string.IsNullOrWhiteSpace(actions.Profile) && !ProfileExists(actions.Profile, profileIds))
            return RuleIssue.MissingProfile;

        return null;
    }

    internal static bool ProfileExists(string profileId, IReadOnlySet<string> profileIds) =>
        string.Equals(profileId, RuleResolver.DefaultProfileId, StringComparison.Ordinal) || profileIds.Contains(profileId);

    /// <summary>The rule's conditions without the nulls a hand-edited file can leave.</summary>
    internal static List<RuleCondition> ConditionsOf(Rule rule) =>
        rule.When?.Where(c => c is not null).ToList() ?? [];
}
