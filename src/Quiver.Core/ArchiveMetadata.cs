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

    public int Version { get; set; } = 1;

    public Dictionary<string, FolderMetadata> Folders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsTopic(string relativeKey) =>
        Folders.TryGetValue(relativeKey, out var folder) &&
        string.Equals(folder.Kind, TopicKind, StringComparison.OrdinalIgnoreCase);

    /// <returns>Whether anything changed.</returns>
    public bool SetTopic(string relativeKey, bool isTopic)
    {
        if (isTopic)
        {
            if (IsTopic(relativeKey))
                return false;
            if (!Folders.TryGetValue(relativeKey, out var folder))
                Folders[relativeKey] = folder = new FolderMetadata();
            folder.Kind = TopicKind;
            return true;
        }

        if (!Folders.TryGetValue(relativeKey, out var existing) || existing.Kind is null)
            return false;
        existing.Kind = null;
        if (existing.IsEmpty)
            Folders.Remove(relativeKey);
        return true;
    }

    /// <summary>Position of a tile on its board; null for tiles never moved, which follow in name order.</summary>
    public int? OrderOf(string relativeKey) => Folders.TryGetValue(relativeKey, out var folder) ? folder.Order : null;

    /// <summary>Remembers the order of a board's tiles after one of them was moved.</summary>
    /// <returns>Whether anything changed.</returns>
    public bool SetTileOrder(IReadOnlyList<string> tileKeys)
    {
        var changed = false;
        for (var i = 0; i < tileKeys.Count; i++)
        {
            if (!Folders.TryGetValue(tileKeys[i], out var folder))
                Folders[tileKeys[i]] = folder = new FolderMetadata();
            if (folder.Order != i)
            {
                folder.Order = i;
                changed = true;
            }
        }
        return changed;
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
    public string? Kind { get; set; }

    /// <summary>GlazeWM workspace bound to the topic.</summary>
    public string? Workspace { get; set; }

    public string? Color { get; set; }

    public TilePlacement? Tile { get; set; }

    /// <summary>Position of the tile on its board.</summary>
    public int? Order { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Kind is null && Workspace is null && Color is null && Tile is null && Order is null;

    // Every property is a string, a record or a number: a shallow copy is a full one.
    public FolderMetadata Clone() => (FolderMetadata)MemberwiseClone();

    public bool SameAs(FolderMetadata other) =>
        Kind == other.Kind && Workspace == other.Workspace && Color == other.Color && Tile == other.Tile && Order == other.Order;
}

/// <summary>Position and size of a tile on its board, in grid cells.</summary>
public sealed record TilePlacement(int X, int Y, int W, int H);

/// <summary>Reads and writes &lt;root&gt;/.quiver/archive.json.</summary>
public static class MetadataStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        WriteIndented = true,
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
