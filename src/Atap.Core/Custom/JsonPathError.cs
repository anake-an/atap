namespace Atap.Core.Custom;

/// <summary>Why a path did not parse. The settings UI turns each kind into its own message.</summary>
public enum JsonPathErrorKind
{
    /// <summary>The path is empty or only spaces.</summary>
    Empty,

    /// <summary>The path does not start with <c>$</c>.</summary>
    MissingRoot,

    /// <summary>The path is longer than <see cref="JsonPathLite.MaxLength"/> characters.</summary>
    TooLong,

    /// <summary>The path has more than <see cref="JsonPathLite.MaxSegments"/> steps.</summary>
    TooManySegments,

    /// <summary>A character that cannot appear here, such as a space or <c>#</c> in a dot name.</summary>
    UnexpectedCharacter,

    /// <summary>The path stops in the middle of a step, such as <c>$.</c> or <c>$[0</c>.</summary>
    UnexpectedEnd,

    /// <summary>A quoted name has no closing quote.</summary>
    UnterminatedName,

    /// <summary>A backslash in a quoted name is not followed by a quote or a backslash.</summary>
    InvalidEscape,

    /// <summary>An index that is not a whole number in range, such as <c>[1.5]</c>, <c>[-0]</c> or <c>[99999999999]</c>.</summary>
    InvalidIndex,

    /// <summary>Recursive descent (<c>..</c>) is not supported.</summary>
    RecursiveDescent,

    /// <summary>Filters and expressions, such as <c>[?(@.up)]</c> or <c>[(@.length-1)]</c>, are not supported.</summary>
    Filter,

    /// <summary>Slices, such as <c>[0:2]</c>, are not supported.</summary>
    Slice,

    /// <summary>Lists of selectors, such as <c>[0,1]</c> or <c>['a','b']</c>, are not supported.</summary>
    Union,

    /// <summary>A function other than <c>length()</c>, such as <c>max()</c>.</summary>
    UnknownFunction,

    /// <summary><c>length()</c> is followed by more steps. It must be the last step.</summary>
    LengthNotLast,
}

/// <summary>A path that did not parse.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Position">Zero-based index in the text as given, so the UI can point at it.</param>
/// <param name="Text">The part that is wrong, such as the character or the function name. Null when there is nothing to show.</param>
public sealed record JsonPathError(JsonPathErrorKind Kind, int Position, string? Text = null)
{
    /// <summary>A short English description for tests and diagnostics. The UI uses <see cref="Kind"/> instead.</summary>
    public override string ToString() =>
        Text is null ? $"{Kind} at {Position}" : $"{Kind} at {Position}: '{Text}'";
}

/// <summary>Thrown by <see cref="JsonPathLite.Parse"/> for a path that does not parse.</summary>
public sealed class JsonPathException(JsonPathError error)
    : FormatException($"The path is not valid ({error}).")
{
    public JsonPathError Error { get; } = error;
}
