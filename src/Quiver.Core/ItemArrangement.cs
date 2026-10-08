namespace Quiver.Core;

/// <summary>
/// The order of the items of a tile. Sorted, folders come first and then files, as in Explorer; put in order by hand,
/// they keep the places they were given, and what arrived since follows by name.
/// </summary>
public static class ItemArrangement
{
    /// <param name="items">Folders first, then files, each by name: the order of the folder.</param>
    /// <param name="sort">Null for by name.</param>
    /// <param name="byHand">The names in the order given by hand; it wins over <paramref name="sort"/>.</param>
    public static IReadOnlyList<ArchiveItem> Arrange(IReadOnlyList<ArchiveItem> items, ItemSort? sort, IReadOnlyList<string>? byHand)
    {
        if (byHand is { Count: > 0 })
        {
            var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < byHand.Count; i++)
                places.TryAdd(byHand[i], i);
            // The sort is stable: those without a place keep the order of the folder, after the others.
            return items.OrderBy(item => places.TryGetValue(item.Name, out var place) ? place : int.MaxValue).ToList();
        }
        if (sort is null or { Key: SortKey.Name, Descending: false })
            return items;
        return [.. Sorted(items.Where(item => item.IsFolder), sort), .. Sorted(items.Where(item => !item.IsFolder), sort)];
    }

    /// <summary>By the key of the sort, in its direction; items with the same key by name, from A.</summary>
    private static IEnumerable<ArchiveItem> Sorted(IEnumerable<ArchiveItem> items, ItemSort sort)
    {
        var byName = NaturalStringComparer.Instance;
        return sort.Key switch
        {
            SortKey.Name => sort.Descending ? items.OrderByDescending(item => item.Name, byName) : items.OrderBy(item => item.Name, byName),
            SortKey.Modified => By(items, item => item.Modified ?? DateTime.MinValue, sort.Descending).ThenBy(item => item.Name, byName),
            SortKey.Type => By(items, TypeOf, sort.Descending, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Name, byName),
            SortKey.Size => By(items, item => item.Size ?? 0, sort.Descending).ThenBy(item => item.Name, byName),
            _ => items,
        };
    }

    private static IOrderedEnumerable<ArchiveItem> By<TKey>(IEnumerable<ArchiveItem> items, Func<ArchiveItem, TKey> key, bool descending,
        IComparer<TKey>? comparer = null) =>
        descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);

    /// <summary>The extension, which says the kind of file: ".docx", ".lnk"; nothing for folders and files without one.</summary>
    private static string TypeOf(ArchiveItem item) => item.IsFolder ? "" : Path.GetExtension(item.Name);
}
