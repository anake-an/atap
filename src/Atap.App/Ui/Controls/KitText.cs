using Atap.Core.Custom;
using Atap.Core.Localization;

namespace Atap.Ui.Controls;

/// <summary>The UI kit's own words, from Strings/controls.*.json, for screen reader names.</summary>
internal static class KitText
{
    public const string Progress = "controls.progress";
    public const string Stale = "controls.stale";
    public const string Items = "controls.items";
    public const string More = "controls.more";
    public const string Page = "controls.page";

    public static string Get(string key) => Localizer.Instance[key];

    /// <summary>"Warning", "Critical" and so on.</summary>
    public static string Level(ValueLevel level, ILocalizer? text = null) => (text ?? Localizer.Instance)[$"controls.level.{level}"];

    /// <summary>A plain sentence for why a value is missing, such as "This value is not a number".</summary>
    public static string Problem(ValueProblem problem, ILocalizer? text = null) =>
        problem == ValueProblem.None ? string.Empty : (text ?? Localizer.Instance)[$"controls.problem.{problem}"];

    /// <summary>The level word worth saying out loud: only levels that differ from a plain, good value.</summary>
    public static string? Status(ValueLevel level, ILocalizer? text = null) =>
        level is ValueLevel.Warning or ValueLevel.Critical ? Level(level, text) : null;
}
