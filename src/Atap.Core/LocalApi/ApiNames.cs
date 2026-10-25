namespace Atap.Core.LocalApi;

/// <summary>
/// Wire names for the local API enums. Each list is in enum order. Parsing ignores case, and formatting
/// always writes these lower-case forms.
/// </summary>
public static class ApiNames
{
    public static IReadOnlyList<string> Ops { get; } = ["ping", "push", "end", "alert"];

    public static IReadOnlyList<string> States { get; } = ["running", "done", "failed"];

    public static IReadOnlyList<string> Icons { get; } =
    [
        "info", "check", "warning", "error", "download", "upload", "sync",
        "backup", "server", "build", "timer", "bell", "terminal",
    ];

    public static IReadOnlyList<string> Accents { get; } = ["teal", "amber", "coral", "blue", "violet"];

    public static IReadOnlyList<string> ErrorCodes { get; } = ["invalid", "too_large", "rate_limited", "unknown_op", "widget_disabled"];

    public static string Of(ApiOp value) => Name(Ops, (int)value);

    public static string Of(PushState value) => Name(States, (int)value);

    public static string Of(PushIcon value) => Name(Icons, (int)value);

    public static string Of(PushAccent value) => Name(Accents, (int)value);

    public static string Of(ApiErrorCode value) => Name(ErrorCodes, (int)value);

    public static bool TryParse(string? name, out ApiOp value) => Find(Ops, name, out value);

    public static bool TryParse(string? name, out PushState value) => Find(States, name, out value);

    public static bool TryParse(string? name, out PushIcon value) => Find(Icons, name, out value);

    public static bool TryParse(string? name, out PushAccent value) => Find(Accents, name, out value);

    public static bool TryParse(string? name, out ApiErrorCode value) => Find(ErrorCodes, name, out value);

    /// <summary>True when <paramref name="value"/> is a named member, not a cast from an arbitrary number.</summary>
    internal static bool IsDefined(PushState value) => (uint)value < (uint)States.Count;

    internal static bool IsDefined(PushIcon value) => (uint)value < (uint)Icons.Count;

    internal static bool IsDefined(PushAccent value) => (uint)value < (uint)Accents.Count;

    private static string Name(IReadOnlyList<string> names, int index)
    {
        if ((uint)index >= (uint)names.Count)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Not a defined value.");
        return names[index];
    }

    /// <summary>Looks <paramref name="name"/> up in <paramref name="names"/>, which must be in the order of <typeparamref name="T"/>.</summary>
    internal static bool Find<T>(IReadOnlyList<string> names, string? name, out T value)
        where T : struct, Enum
    {
        if (name is not null)
        {
            for (var i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    value = (T)Enum.ToObject(typeof(T), i);
                    return true;
                }
            }
        }
        value = default;
        return false;
    }
}
