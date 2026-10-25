namespace Atap.Core.Rules;

/// <summary>
/// The result of <see cref="RuleResolver.Resolve"/>.
/// </summary>
/// <param name="ProfileId">The profile to show.</param>
/// <param name="Hide">Some matching rule hides the notch.</param>
/// <param name="Slim">Some matching rule turns on slim mode.</param>
/// <param name="QuietAlerts">Some matching rule quiets alerts.</param>
/// <param name="NextTimeBoundary">
/// When a time window next opens or closes in a way that can change this outcome. The App arms one
/// scheduler <c>At</c> for it and resolves again. Null when no time window can change the outcome by itself.
/// </param>
/// <param name="InputsUsed">
/// Inputs read by enabled rules that can apply. The App subscribes only to these and resolves again
/// when one changes. With <see cref="ConditionKind.TimeWindow"/> it also resolves again after a clock change or resume.
/// </param>
public sealed record RuleOutcome(
    string ProfileId,
    bool Hide,
    bool Slim,
    bool QuietAlerts,
    DateTimeOffset? NextTimeBoundary,
    IReadOnlySet<ConditionKind> InputsUsed)
{
    /// <summary>Equal when every value matches, comparing <see cref="InputsUsed"/> by content.</summary>
    public bool Equals(RuleOutcome? other) =>
        other is not null
        && string.Equals(ProfileId, other.ProfileId, StringComparison.Ordinal)
        && Hide == other.Hide
        && Slim == other.Slim
        && QuietAlerts == other.QuietAlerts
        && NextTimeBoundary == other.NextTimeBoundary
        && InputsUsed.SetEquals(other.InputsUsed);

    public override int GetHashCode()
    {
        var inputs = 0;
        foreach (var kind in InputsUsed)
            inputs |= 1 << (int)kind;
        return HashCode.Combine(ProfileId, Hide, Slim, QuietAlerts, NextTimeBoundary, inputs);
    }
}

/// <summary>Decides the active profile and notch flags from the rules in settings.json.</summary>
public static class RuleResolver
{
    /// <summary>Id of the Default profile, the top level of layout.json. It always exists.</summary>
    public const string DefaultProfileId = "default";

    /// <summary>
    /// Checks the rules in order against <paramref name="context"/>. A rule applies when it is enabled,
    /// <see cref="RuleChecker.Check"/> finds no problem, and all of its conditions match.
    /// <list type="bullet">
    /// <item>The profile comes from the first applying rule that names one. Without one it is
    /// <paramref name="manualProfileId"/> if that profile exists, otherwise the Default profile.</item>
    /// <item>Hide, slim and quiet are ORed over every applying rule.</item>
    /// <item>A rule naming a profile that does not exist is skipped whole, flags included.</item>
    /// </list>
    /// </summary>
    /// <param name="manualProfileId">The user's pick (<c>profiles.manual</c>), or null.</param>
    /// <param name="profileIds">Ids of the profiles in layout.json. The Default profile need not be listed.</param>
    public static RuleOutcome Resolve(
        IReadOnlyList<Rule> rules,
        RuleContext context,
        string? manualProfileId,
        IReadOnlySet<string> profileIds)
    {
        string? ruleProfile = null;
        bool hide = false, slim = false, quiet = false;
        DateTimeOffset? boundary = null;
        var inputs = new HashSet<ConditionKind>();

        foreach (var rule in rules)
        {
            if (rule is null || !rule.Enabled || RuleChecker.Check(rule, profileIds) is not null)
                continue;

            var conditions = RuleChecker.ConditionsOf(rule);
            var met = new bool[conditions.Count];
            var allMet = true;
            var otherInputsMet = true;
            for (var i = 0; i < conditions.Count; i++)
            {
                inputs.Add(conditions[i].Kind);
                met[i] = conditions[i].Matches(context);
                allMet &= met[i];
                if (!met[i] && conditions[i] is not TimeWindowCondition)
                    otherInputsMet = false;
            }

            if (allMet)
            {
                var actions = rule.Then;
                if (ruleProfile is null && !string.IsNullOrWhiteSpace(actions.Profile))
                    ruleProfile = actions.Profile;
                hide |= actions.Hide;
                slim |= actions.Slim;
                quiet |= actions.QuietAlerts;
            }

            // A rule held back by an app, Wi-Fi or power condition changes only when that input changes,
            // and every input change resolves again. So only time windows of rules that time alone can
            // change need a boundary: all of them when the rule applies, the closed ones when it does not.
            if (!otherInputsMet)
                continue;
            for (var i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] is TimeWindowCondition window && (allMet || !met[i]))
                    boundary = Earliest(boundary, window.NextChange(context.Now, context.TimeZone));
            }
        }

        var profile = ruleProfile
            ?? (!string.IsNullOrWhiteSpace(manualProfileId) && RuleChecker.ProfileExists(manualProfileId, profileIds)
                ? manualProfileId
                : DefaultProfileId);

        return new RuleOutcome(profile, hide, slim, quiet, boundary, inputs);
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? current, DateTimeOffset? candidate) =>
        candidate is { } value && (current is null || value < current) ? value : current;
}
