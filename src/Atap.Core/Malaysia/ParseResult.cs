using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.Malaysia;

public enum ParseError
{
    None,

    /// <summary>The text was empty, or the document held no usable entries.</summary>
    Empty,

    /// <summary>The text is not JSON, for example an HTML error page.</summary>
    NotJson,

    /// <summary>The source reported a failure, for example an e-Solat status other than "OK!".</summary>
    BadStatus,

    /// <summary>A required field is missing or has the wrong JSON type.</summary>
    MissingField,

    /// <summary>A field is present but its value cannot be read.</summary>
    BadValue,

    /// <summary>The data is for a different zone or period than the one asked for.</summary>
    Mismatch,

    /// <summary>The document uses a schema version this build does not know.</summary>
    UnsupportedSchema,
}

/// <summary>
/// Outcome of parsing a source document. Parsers never throw on bad input. They return
/// an error and a short detail (a field name or the offending value) for the App to log.
/// </summary>
public sealed record ParseResult<T>(T? Value, ParseError Error, string? Detail)
    where T : class
{
    [MemberNotNullWhen(true, nameof(Value))]
    public bool Succeeded => Error == ParseError.None && Value is not null;

    public static ParseResult<T> Ok(T value) => new(value, ParseError.None, null);

    public static ParseResult<T> Fail(ParseError error, string? detail = null) => new(null, error, detail);
}
