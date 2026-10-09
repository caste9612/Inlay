using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quiver.Core;

/// <summary>
/// What folders alone cannot express: which subfolders are topics, tile placement, colors, workspaces.
/// Keys are paths relative to the archive root, so the file travels with the archive.
/// </summary>
public sealed class ArchiveMetadata
{
    public const string TopicKind = "topic";

    /// <summary>A folder of a topic shown as a row of its «Generale», not as a tile of its own.</summary>
    public const string ItemKind = "item";

    public int Version { get; set; } = 1;

    public Dictionary<string, FolderMetadata> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsTopic(string relativeKey) => HasKind(relativeKey, TopicKind);

    /// <summary>Whether a folder of a topic is a row of its «Generale», like a folder dropped on the topic, rather than a tile.</summary>
    public bool IsItem(string relativeKey) => HasKind(relativeKey, ItemKind);

    /// <returns>Whether anything changed.</returns>
    public bool SetTopic(string relativeKey, bool isTopic) => SetKind(relativeKey, TopicKind, isTopic);

    /// <returns>Whether anything changed.</returns>
    public bool SetItem(string relativeKey, bool isItem) => SetKind(relativeKey, ItemKind, isItem);

    private bool HasKind(string relativeKey, string kind) =>
        Folders.TryGetValue(relativeKey, out var folder) && string.Equals(folder.Kind, kind, StringComparison.OrdinalIgnoreCase);

    /// <summary>Gives a folder a kind, in the place of the one it had; taken away, it goes only if it is that one.</summary>
    private bool SetKind(string relativeKey, string kind, bool on) =>
        Change(relativeKey, folder =>
        {
            if (string.Equals(folder.Kind, kind, StringComparison.OrdinalIgnoreCase) == on)
                return false;
            folder.Kind = on ? kind : null;
            return true;
        });

    /// <summary>Position of a tile on its board; null for tiles never moved, which follow in name order.</summary>
    public int? OrderOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Order : null;

    /// <summary>
    /// The color of a tile or a topic, as "#RRGGBB"; <see cref="TopicColors.None"/> for a topic left without one on purpose;
    /// null when it has none.
    /// </summary>
    public string? ColorOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Color : null;

    /// <summary>The color given by Quiver to a topic of the root, not chosen.</summary>
    public bool IsColorAutomatic(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) && folder.AutoColor == true;

    /// <summary>The color chosen for a folder: a tile does not show the one Quiver gave it when it was a topic of the root.</summary>
    public string? ChosenColorOf(string relativeKey) => IsColorAutomatic(relativeKey) ? null : ColorOf(relativeKey);

    /// <summary>Gives a tile or a topic a color, or none.</summary>
    /// <param name="automatic">Given by Quiver, not chosen: a topic keeps it only while it is in the root.</param>
    /// <returns>Whether anything changed.</returns>
    public bool SetColor(string relativeKey, string? color, bool automatic = false)
    {
        bool? marked = automatic && color is not null ? true : null;
        if (!Folders.TryGetValue(relativeKey, out var folder))
        {
            if (color is null)
                return false;
            Folders[relativeKey] = folder = new FolderMetadata();
        }
        if (string.Equals(folder.Color, color, StringComparison.OrdinalIgnoreCase) && folder.AutoColor == marked)
            return false;
        folder.Color = color;
        folder.AutoColor = marked;
        if (folder.IsEmpty)
            Folders.Remove(relativeKey);
        return true;
    }

    /// <summary>Remembers the order of a board's tiles, or of the topics next to each other, after one of them was moved.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetOrder(IReadOnlyList<string> keys)
    {
        var changed = false;
        for (var i = 0; i < keys.Count; i++)
        {
            if (!Folders.TryGetValue(keys[i], out var folder))
                Folders[keys[i]] = folder = new FolderMetadata();
            if (folder.Order != i)
            {
                folder.Order = i;
                changed = true;
            }
        }
        return changed;
    }

    /// <summary>The size given to a tile; null for the usual one.</summary>
    public TileSize? SizeOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Size : null;

    /// <summary>Gives a tile a size, or the usual one back with null.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetSize(string relativeKey, TileSize? size)
    {
        var kept = size?.Clamped() is { IsUsual: false } clamped ? clamped : null;
        return Change(relativeKey, folder =>
        {
            if (folder.Size == kept)
                return false;
            folder.Size = kept;
            return true;
        });
    }

    /// <summary>How the items of a tile are sorted; null for by name, or when they were put in order by hand.</summary>
    public ItemSort? SortOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Sort : null;

    /// <summary>The order the items of a tile were given by hand, by name; null when they follow a sort.</summary>
    public IReadOnlyList<string>? ItemOrderOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Items : null;

    /// <summary>Sorts the items of a tile: the order given by hand goes. By name from A is the usual sort, and needs no entry.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetSort(string relativeKey, ItemSort sort) =>
        SetArrangement(relativeKey, sort is { Key: SortKey.Name, Descending: false } ? null : sort, null);

    /// <summary>Puts the items of a tile in the order given by hand, which replaces any sort.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetItemOrder(string relativeKey, IReadOnlyList<string> names) =>
        SetArrangement(relativeKey, null, names.Count > 0 ? names.ToList() : null);

    /// <summary>Gives a tile back the sort, or the order by hand, it had: what an undo does.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetArrangement(string relativeKey, ItemSort? sort, IReadOnlyList<string>? names) =>
        Change(relativeKey, folder =>
        {
            if (folder.Sort == sort && FolderMetadata.SameNames(folder.Items, names))
                return false;
            folder.Sort = sort;
            folder.Items = names;
            return true;
        });

    /// <summary>An item of a tile was renamed: it keeps its place in the order given by hand.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool RenameItem(string relativeKey, string oldName, string newName)
    {
        if (ItemOrderOf(relativeKey) is not { } names || !names.Contains(oldName, StringComparer.OrdinalIgnoreCase))
            return false;
        var renamed = names.Select(name => string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase) ? newName : name).ToList();
        return Change(relativeKey, folder =>
        {
            folder.Items = renamed;
            return true;
        });
    }

    /// <summary>Applies a change to the entry of a folder, made if missing and dropped if left empty.</summary>
    private bool Change(string relativeKey, Func<FolderMetadata, bool> change)
    {
        if (!Folders.TryGetValue(relativeKey, out var folder))
            folder = new FolderMetadata();
        if (!change(folder))
            return false;
        if (folder.IsEmpty)
            Folders.Remove(relativeKey);
        else
            Folders[relativeKey] = folder;
        return true;
    }

    /// <summary>Gives a tile back a place it had: a position, or none to follow in name order.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetOrder(string relativeKey, int? order)
    {
        if (!Folders.TryGetValue(relativeKey, out var folder))
        {
            if (order is null)
                return false;
            Folders[relativeKey] = folder = new FolderMetadata();
        }
        if (folder.Order == order)
            return false;
        folder.Order = order;
        if (folder.IsEmpty)
            Folders.Remove(relativeKey);
        return true;
    }

    /// <summary>Follows a renamed or moved folder: its entry and those below it take the new path.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool MoveFolder(string oldKey, string newKey)
    {
        var keys = KeysAtOrBelow(oldKey);
        foreach (var key in keys)
        {
            var folder = Folders[key];
            Folders.Remove(key);
            Folders[newKey + key[oldKey.Length..]] = folder;
        }
        return keys.Count > 0;
    }

    /// <summary>Forgets a deleted folder and everything below it.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool RemoveFolder(string key)
    {
        var keys = KeysAtOrBelow(key);
        foreach (var removed in keys)
            Folders.Remove(removed);
        return keys.Count > 0;
    }

    /// <summary>Copies the entries of these folders and of everything below them, for <see cref="Restore"/>.</summary>
    public MetadataSnapshot Snapshot(IEnumerable<string> keys)
    {
        var roots = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var entries = new Dictionary<string, FolderMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in roots.SelectMany(KeysAtOrBelow))
            entries[key] = Folders[key].Clone();
        return new MetadataSnapshot(roots, entries);
    }

    /// <summary>
    /// Gives the folders of a snapshot back the entries they had then, dropping those added below them since:
    /// this is how a folder moved back takes its metadata along.
    /// </summary>
    /// <returns>Whether anything changed.</returns>
    public bool Restore(MetadataSnapshot snapshot)
    {
        var current = snapshot.Roots.SelectMany(KeysAtOrBelow).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unchanged = current.Count == snapshot.Entries.Count &&
                        current.All(key => snapshot.Entries.TryGetValue(key, out var saved) && saved.SameAs(Folders[key]));
        if (unchanged)
            return false;
        foreach (var key in current)
            Folders.Remove(key);
        foreach (var (key, folder) in snapshot.Entries)
            Folders[key] = folder.Clone();
        return true;
    }

    private List<string> KeysAtOrBelow(string key) =>
        Folders.Keys.Where(k => ArchiveLayout.IsKeyAtOrBelow(k, key)).ToList();
}

/// <summary>The metadata of some folders, and of everything below them, at one moment.</summary>
/// <param name="Roots">The folders, as metadata keys.</param>
/// <param name="Entries">Their entries and those below them, by key.</param>
public sealed record MetadataSnapshot(IReadOnlyList<string> Roots, IReadOnlyDictionary<string, FolderMetadata> Entries)
{
    public static readonly MetadataSnapshot Empty = new([], new Dictionary<string, FolderMetadata>());

    /// <summary>The part of the snapshot about these folders only.</summary>
    public MetadataSnapshot Restrict(IEnumerable<string> roots)
    {
        var kept = Roots.Intersect(roots, StringComparer.OrdinalIgnoreCase).ToList();
        var entries = Entries
            .Where(entry => kept.Any(root => ArchiveLayout.IsKeyAtOrBelow(entry.Key, root)))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        return new MetadataSnapshot(kept, entries);
    }
}

public sealed class FolderMetadata
{
    /// <summary>
    /// <see cref="ArchiveMetadata.TopicKind"/> for a topic below the root, <see cref="ArchiveMetadata.ItemKind"/> for a row of
    /// «Generale»; null for a tile.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>GlazeWM workspace bound to the topic.</summary>
    public string? Workspace { get; set; }

    public string? Color { get; set; }

    /// <summary>
    /// The color was given by Quiver, to a topic of the root, and not chosen. A version of Quiver without it forgets the
    /// mark and keeps the color, as if chosen.
    /// </summary>
    public bool? AutoColor { get; set; }

    /// <summary>The size given to the tile; null for the usual one.</summary>
    public TileSize? Size { get; set; }

    /// <summary>Position of the tile on its board, or of the topic among the topics next to it.</summary>
    public int? Order { get; set; }

    /// <summary>How the items of the tile are sorted; null for by name.</summary>
    public ItemSort? Sort { get; set; }

    /// <summary>
    /// The order the items of the tile were given by dragging them, by name. Those not in it, arrived since, follow.
    /// Replaced as a whole, never changed in place.
    /// </summary>
    public IReadOnlyList<string>? Items { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        Kind is null && Workspace is null && Color is null && AutoColor is null && Size is null && Order is null && Sort is null &&
        Items is null;

    // Every property is a string, a number, a record or a list that is replaced, never changed: a shallow copy is a full one.
    public FolderMetadata Clone() => (FolderMetadata)MemberwiseClone();

    public bool SameAs(FolderMetadata other) =>
        Kind == other.Kind && Workspace == other.Workspace && Color == other.Color && AutoColor == other.AutoColor &&
        Size == other.Size && Order == other.Order && Sort == other.Sort && SameNames(Items, other.Items);

    internal static bool SameNames(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        a is null || b is null ? a is null && b is null : a.SequenceEqual(b, StringComparer.Ordinal);
}

/// <summary>The size of a tile: the columns of the board it takes, and the rows of items it shows before scrolling.</summary>
/// <param name="Rows">Null for as many as it has, up to the usual limit.</param>
public sealed record TileSize(int Columns, int? Rows)
{
    public const int MaxColumns = 3;
    public const int MinRows = 2;
    public const int MaxRows = 40;

    /// <summary>One column and the automatic height: the size of a tile nobody resized.</summary>
    public bool IsUsual => Columns <= 1 && Rows is null;

    /// <summary>Within what a board can show: one to three columns, two to forty rows.</summary>
    public TileSize Clamped() =>
        new(Math.Clamp(Columns, 1, MaxColumns), Rows is { } rows ? Math.Clamp(rows, MinRows, MaxRows) : null);
}

/// <summary>What the items of a tile can be sorted by.</summary>
public enum SortKey
{
    Name,
    Modified,
    Type,
    Size,
}

/// <param name="Descending">From the last: Z to A, the newest, the largest.</param>
public sealed record ItemSort(SortKey Key, bool Descending)
{
    /// <summary>The direction a sort starts in when chosen: names from A, the newest first, types from A, the largest first.</summary>
    public static ItemSort For(SortKey key) => new(key, key is SortKey.Modified or SortKey.Size);
}

/// <summary>Reads and writes &lt;root&gt;/.quiver/archive.json.</summary>
public static class MetadataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        WriteIndented = true,
        // "modified" rather than 1: the file stays readable, and a new kind of sort never shifts the old ones.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string GetPath(string root) =>
        Path.Combine(root, ArchiveLayout.MetadataFolderName, ArchiveLayout.MetadataFileName);

    public static ArchiveMetadata Load(string root)
    {
        var path = GetPath(root);
        if (!File.Exists(path))
            return new ArchiveMetadata();

        ArchiveMetadata? loaded;
        try
        {
            using var stream = File.OpenRead(path);
            loaded = JsonSerializer.Deserialize<ArchiveMetadata>(stream, Options);
        }
        catch (JsonException)
        {
            // Keep the unreadable file aside instead of silently overwriting it on the next save.
            File.Copy(path, path + ".corrupt", overwrite: true);
            return new ArchiveMetadata();
        }

        var metadata = loaded ?? new ArchiveMetadata();
        // The deserializer builds a case-sensitive dictionary, but the keys are Windows paths.
        var folders = new Dictionary<string, FolderMetadata>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in metadata.Folders)
        {
            if (value is not null)
                folders[key] = value;
        }
        metadata.Folders = folders;
        return metadata;
    }

    public static void Save(string root, ArchiveMetadata metadata)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, ArchiveLayout.MetadataFolderName));
        folder.Attributes |= FileAttributes.Hidden;

        // Write to a temporary file first, so a crash never leaves a half-written archive.json.
        var path = GetPath(root);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(metadata, Options));
        File.Move(temporary, path, overwrite: true);
    }
}
