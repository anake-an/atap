using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Atap.Core.Widgets;

/// <summary>
/// An instance id: the type id for a single instance, or "&lt;typeId&gt;#&lt;name&gt;", for example "json-url#nas".
/// The id names folders under data\ and state\, so it holds only characters that are legal in file names.
/// </summary>
public readonly partial record struct WidgetInstanceId(string TypeId, string? Name)
{
    public const char Separator = '#';

    public static bool IsValidTypeId([NotNullWhen(true)] string? typeId) =>
        typeId is not null && TypeIdPattern().IsMatch(typeId);

    public static bool IsValidName([NotNullWhen(true)] string? name) =>
        name is not null && NamePattern().IsMatch(name);

    public static bool TryParse([NotNullWhen(true)] string? value, out WidgetInstanceId id)
    {
        id = default;
        if (string.IsNullOrEmpty(value))
            return false;

        var separator = value.IndexOf(Separator, StringComparison.Ordinal);
        var typeId = separator < 0 ? value : value[..separator];
        var name = separator < 0 ? null : value[(separator + 1)..];
        if (!IsValidTypeId(typeId) || (name is not null && !IsValidName(name)))
            return false;

        id = new WidgetInstanceId(typeId, name);
        return true;
    }

    public override string ToString() => Name is null ? TypeId : $"{TypeId}{Separator}{Name}";

    // \z, not $: "$" also matches before a final newline.
    [GeneratedRegex(@"^[a-z][a-z0-9-]{1,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypeIdPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
