namespace Atap.Core.Custom;

/// <summary>The ways a custom widget can show a value.</summary>
public enum ValueTemplateKind
{
    /// <summary>One number with an optional unit, such as "72.5%" or "1,204".</summary>
    Number,

    /// <summary>One line of text.</summary>
    Text,

    /// <summary>A bar from a minimum to a maximum, shown as a percentage.</summary>
    Progress,

    /// <summary>The items of an array or object, each with a label and an optional value.</summary>
    List,

    /// <summary>A coloured dot from a status word, a true or false value, or a number with thresholds.</summary>
    StatusDot,
}

/// <summary>
/// What a custom widget shows and where each field comes from. Paths use <see cref="JsonPathLite"/>.
/// Settings that do not apply to <see cref="Kind"/> are ignored.
/// </summary>
/// <param name="Kind">The template.</param>
/// <param name="ValuePath">
/// The value. For <see cref="ValueTemplateKind.List"/> it points at the array or object to list,
/// or uses <c>[*]</c> to list every match. A path that finds several values uses the first one for the other templates.
/// </param>
public sealed record ValueTemplateSpec(ValueTemplateKind Kind, string ValuePath = "$")
{
    public const int MaxDecimals = 6;
    public const int MaxUnitLength = 12;
    public const int DefaultMaxItems = 5;
    public const int MaxItemsLimit = 20;

    /// <summary>Shown after numbers, such as "%", "°C" or "GB". Letters get a space before them, symbols do not.</summary>
    public string? Unit { get; init; }

    /// <summary>Fixed decimal places from 0 to <see cref="MaxDecimals"/>. Null shows up to 2 and drops trailing zeros.</summary>
    public int? Decimals { get; init; }

    /// <summary>The value at an empty bar. Progress only.</summary>
    public double Min { get; init; }

    /// <summary>The value at a full bar when <see cref="MaxPath"/> is not set. Null means 100. Progress only.</summary>
    public double? Max { get; init; }

    /// <summary>Reads the full-bar value from the payload instead, such as a disk's total size. Progress only.</summary>
    public string? MaxPath { get; init; }

    /// <summary>
    /// Path inside each item for its label, where <c>$</c> is the item. Null uses the member name for object members
    /// and the item itself otherwise. List only.
    /// </summary>
    public string? ItemLabelPath { get; init; }

    /// <summary>
    /// Path inside each item for its value, which gets a level from the thresholds or status words.
    /// Null uses the item itself for object members and no value otherwise. List only.
    /// </summary>
    public string? ItemValuePath { get; init; }

    /// <summary>Most items kept, from 1 to <see cref="MaxItemsLimit"/>. List only.</summary>
    public int MaxItems { get; init; } = DefaultMaxItems;

    /// <summary>
    /// Thresholds for the number. Progress compares the percentage from 0 to 100.
    /// List compares each item value. Text and StatusDot use them when the value is a number.
    /// </summary>
    public ThresholdSpec? Thresholds { get; init; }

    /// <summary>Status words for Normal. Null uses <see cref="StatusWords.DefaultOk"/>.</summary>
    public IReadOnlyList<string>? OkValues { get; init; }

    /// <summary>Status words for Warning. Null uses <see cref="StatusWords.DefaultWarning"/>.</summary>
    public IReadOnlyList<string>? WarningValues { get; init; }

    /// <summary>Status words for Critical. Null uses <see cref="StatusWords.DefaultCritical"/>.</summary>
    public IReadOnlyList<string>? CriticalValues { get; init; }

    /// <summary>
    /// Text values that mean there is no value, matched without regard to case or surrounding spaces.
    /// A selected value or list item value that matches gives no value and keeps the hysteresis.
    /// Use <see cref="StatusWords.DefaultNoValue"/> for Home Assistant, whose REST API gives <c>{"state": "unavailable"}</c>.
    /// Null matches nothing. Numbers and true or false never match.
    /// </summary>
    public IReadOnlyList<string>? NoValueWords { get; init; }
}

/// <summary>Which part of a <see cref="ValueTemplateSpec"/> is wrong.</summary>
public enum ValueTemplateField
{
    ValuePath,
    MaxPath,
    ItemLabelPath,
    ItemValuePath,

    /// <summary><see cref="ValueTemplateSpec.Min"/> or <see cref="ValueTemplateSpec.Max"/> is not finite, or Max is not above Min.</summary>
    Range,

    Thresholds,
}

/// <summary>Why a <see cref="ValueTemplateSpec"/> cannot be used.</summary>
/// <param name="Field">The setting that is wrong.</param>
/// <param name="Path">The parse error, for a path field.</param>
/// <param name="Threshold">The problem, for <see cref="ValueTemplateField.Thresholds"/>.</param>
public sealed record ValueTemplateError(ValueTemplateField Field, JsonPathError? Path = null, ThresholdProblem Threshold = ThresholdProblem.None);

/// <summary>
/// Default status words, matched without regard to case or surrounding spaces. A word in more than one list
/// takes the worst level. Numbers match a numeric word by value, so 1.0 matches "1".
/// The defaults suit health checks, where off, false, no, 0 and disabled mean a fault.
/// Widgets where off is a normal state, such as switches, lights and door sensors, should pass their own lists.
/// </summary>
public static class StatusWords
{
    /// <summary>The states Home Assistant gives when a sensor cannot be read. For <see cref="ValueTemplateSpec.NoValueWords"/>.</summary>
    public static IReadOnlyList<string> DefaultNoValue { get; } = ["unavailable", "unknown"];

    public static IReadOnlyList<string> DefaultOk { get; } =
    [
        "ok", "up", "online", "healthy", "running", "active", "connected", "enabled",
        "on", "true", "yes", "pass", "passing", "success", "1",
    ];

    public static IReadOnlyList<string> DefaultWarning { get; } =
    [
        "warning", "warn", "degraded", "pending", "maintenance", "starting", "restarting", "paused", "unstable",
    ];

    public static IReadOnlyList<string> DefaultCritical { get; } =
    [
        "critical", "error", "down", "offline", "unhealthy", "failed", "failure", "fail", "stopped", "exited",
        "dead", "disconnected", "disabled", "off", "false", "no", "0",
    ];
}
