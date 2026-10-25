using System.Diagnostics.CodeAnalysis;

namespace Atap.Core.LocalApi;

/// <summary>Why a request was refused. Wire names are in <see cref="ApiNames.ErrorCodes"/>.</summary>
public enum ApiErrorCode
{
    /// <summary>Not JSON, a wrong field or type, or a value outside its limits.</summary>
    Invalid,

    /// <summary>Over <see cref="PushProtocol.MaxMessageBytes"/>.</summary>
    TooLarge,

    /// <summary>Too many requests. Sent by the server.</summary>
    RateLimited,

    /// <summary>The op is not one of <see cref="ApiNames.Ops"/>.</summary>
    UnknownOp,

    /// <summary>The push widget is off. Sent by the server.</summary>
    WidgetDisabled,
}

/// <param name="Message">Short English detail for the CLI and logs. Never contains message text from the request.</param>
public sealed record ApiError(ApiErrorCode Code, string Message);

/// <summary>The outcome of <see cref="PushProtocol.Parse(string?)"/>. Exactly one of the two values is set.</summary>
public sealed class ApiParseResult
{
    private ApiParseResult(ApiRequest? request, ApiError? error)
    {
        Request = request;
        Error = error;
    }

    public ApiRequest? Request { get; }

    public ApiError? Error { get; }

    [MemberNotNullWhen(true, nameof(Request))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Request is not null;

    internal static ApiParseResult Success(ApiRequest request) => new(request, null);

    internal static ApiParseResult Failure(ApiError error) => new(null, error);
}

/// <summary>
/// The server's answer. On the wire: <c>{"ok":true}</c>, <c>{"ok":true,"version":"0.2.0"}</c> for ping, or
/// <c>{"ok":false,"error":"invalid","message":"..."}</c>.
/// </summary>
public sealed record ApiResponse
{
    public bool Ok => Error is null;

    public ApiErrorCode? Error { get; init; }

    public string? Message { get; init; }

    /// <summary>The app version, sent in reply to ping.</summary>
    public string? Version { get; init; }

    public static ApiResponse Success { get; } = new();

    public static ApiResponse Pong(string? version) => new() { Version = version };

    public static ApiResponse Failure(ApiErrorCode code, string? message = null) => new() { Error = code, Message = message };

    public static ApiResponse Failure(ApiError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return Failure(error.Code, error.Message);
    }
}
