namespace Atap.Core.Productivity;

/// <summary>Ids for items in the productivity lists (alarms, to-dos, notes, classes, assignments).</summary>
public static class ItemIds
{
    public const int MaxLength = 40;

    /// <summary>A new random id of 12 lowercase hex characters.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>True for 1 to 40 ASCII letters, digits, '-' or '_'.</summary>
    public static bool IsValid(string? id) =>
        !string.IsNullOrEmpty(id)
        && id.Length <= MaxLength
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>List helpers shared by the productivity documents.</summary>
internal static class ItemLists
{
    /// <summary>
    /// Drops null items and items <paramref name="clean"/> rejects, gives a missing, invalid or
    /// repeated id a new one, and keeps the first <paramref name="maxItems"/>.
    /// </summary>
    public static IReadOnlyList<T> Normalize<T>(
        IReadOnlyList<T?>? items, int maxItems, Func<T, string?> idOf, Func<T, string, T?> clean)
        where T : class
    {
        var result = new List<T>();
        if (items is null)
            return result;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (result.Count >= maxItems)
                break;
            if (item is null)
                continue;

            var id = idOf(item);
            if (!ItemIds.IsValid(id) || !seen.Add(id!))
            {
                id = ItemIds.New();
                seen.Add(id);
            }

            var cleaned = clean(item, id!);
            if (cleaned is not null)
                result.Add(cleaned);
        }
        return result;
    }

    /// <summary>Replaces the item with the same id, or appends it while the list is under <paramref name="maxItems"/>.</summary>
    public static IReadOnlyList<T> Upsert<T>(IReadOnlyList<T>? items, T item, Func<T, string> idOf, int maxItems)
        where T : class
    {
        var list = items?.Where(i => i is not null).ToList() ?? [];
        var id = idOf(item);
        var index = list.FindIndex(i => string.Equals(idOf(i), id, StringComparison.Ordinal));
        if (index >= 0)
            list[index] = item;
        else if (list.Count < maxItems)
            list.Add(item);
        return list;
    }

    public static IReadOnlyList<T> Remove<T>(IReadOnlyList<T>? items, string id, Func<T, string> idOf)
        where T : class =>
        items?.Where(i => i is not null && !string.Equals(idOf(i), id, StringComparison.Ordinal)).ToList() ?? [];

    /// <summary>The non-null items, for documents that were not normalized.</summary>
    public static IEnumerable<T> Present<T>(IReadOnlyList<T>? items) where T : class =>
        items?.Where(i => i is not null) ?? [];
}
