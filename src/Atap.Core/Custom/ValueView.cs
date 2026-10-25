namespace Atap.Core.Custom;

/// <summary>Why a <see cref="ValueView"/> has no usable value.</summary>
public enum ValueProblem
{
    None,

    /// <summary>The value is null, empty, or a "no value" state such as Home Assistant's "unavailable".</summary>
    NoValue,

    /// <summary>The value path found nothing.</summary>
    NotFound,

    /// <summary>The template needs a number and the value is not one.</summary>
    NotANumber,

    /// <summary>The template needs text, a number or true or false, and the value is an object or array.</summary>
    NotText,

    /// <summary>The List template found one plain value instead of an array or object.</summary>
    NotAList,

    /// <summary>The Progress maximum is missing, not a number, or not above the minimum.</summary>
    BadMaximum,

    /// <summary>The payload should be JSON and does not parse.</summary>
    InvalidJson,

    /// <summary>The payload bytes are not valid UTF-8.</summary>
    InvalidText,

    /// <summary>The payload is over its size cap.</summary>
    TooLarge,
}

/// <summary>One row of a List view.</summary>
/// <param name="Label">The label, cleaned for display. Empty when the label is not plain text.</param>
/// <param name="Value">The value text, or null when the item has no value.</param>
/// <param name="Level">
/// From the thresholds or status words. Normal when the list has no item values, as with labels only.
/// Unknown when the item value path finds nothing, the value is null or a no-value word, or it matches no status word.
/// </param>
public sealed record ValueListItem(string Label, string? Value, ValueLevel Level);

/// <summary>
/// What a custom widget shows, made by <see cref="ValueTemplate.Render(System.Text.Json.JsonElement, ValueView?, IFormatProvider?)"/>.
/// All text is cleaned of control and direction characters and capped in length.
/// </summary>
public sealed record ValueView
{
    public required ValueTemplateKind Kind { get; init; }

    public ValueLevel Level { get; init; }

    /// <summary>
    /// The main text without the unit. A formatted number for Number, a percentage for Progress,
    /// the item count for List, and the value itself for Text and StatusDot. Empty when there is a problem.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>The unit to show after <see cref="Text"/>, or null.</summary>
    public string? Unit { get; init; }

    /// <summary>Progress only: the value and maximum, such as "36.2 / 50 GB".</summary>
    public string? Detail { get; init; }

    /// <summary>The number behind <see cref="Text"/>, when there is one. Progress gives the raw value, List the item count.</summary>
    public double? Number { get; init; }

    /// <summary>Progress only: how full the bar is, from 0 to 1.</summary>
    public double? Fraction { get; init; }

    /// <summary>List only: the first items, up to <see cref="ValueTemplateSpec.MaxItems"/>.</summary>
    public IReadOnlyList<ValueListItem> Items { get; init; } = [];

    /// <summary>List only: how many items there are in total.</summary>
    public int ItemCount { get; init; }

    public ValueProblem Problem { get; init; }

    /// <summary>
    /// List only: the level of every item, shown or not, for hysteresis on the next render. Keyed by member name,
    /// by label when ItemLabelPath is set, and by position otherwise, with a count for repeats.
    /// An item with no value keeps its level from before. A view with no value keeps the ones from the view before it.
    /// </summary>
    internal IReadOnlyDictionary<string, ValueLevel>? ItemLevels { get; init; }

    /// <summary>
    /// Set only on a view with no value: the level of the last view that had one.
    /// So a blip such as "unavailable" or bad JSON does not reset hysteresis.
    /// </summary>
    internal ValueLevel? HeldLevel { get; init; }

    /// <summary>The level the next render's hysteresis starts from.</summary>
    internal ValueLevel HysteresisLevel => HeldLevel ?? Level;

    /// <summary>
    /// Set by the widget when a refresh failed and this is the last good view.
    /// The template never sets it.
    /// </summary>
    public bool Stale { get; init; }

    public bool HasValue => Problem == ValueProblem.None;

    /// <summary>True at Warning or Critical. The JSON URL, script and MQTT widgets go live while this holds.</summary>
    public bool IsAlerting => Level >= ValueLevel.Warning;

    /// <summary><see cref="Text"/> and <see cref="Unit"/> together, such as "72.5%" or "12 GB".</summary>
    public string DisplayText => ValueText.JoinUnit(Text, Unit);

    /// <summary>A view with no value, for a payload that could not be read.</summary>
    public static ValueView Empty(ValueTemplateKind kind, ValueProblem problem) =>
        new() { Kind = kind, Level = ValueLevel.Unknown, Problem = problem };
}
