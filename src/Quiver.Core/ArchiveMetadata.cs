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

    private List<string> KeysAtOrBelow(string key) =>
        Folders.Keys
            .Where(k => k.Equals(key, StringComparison.OrdinalIgnoreCase) ||
                        k.StartsWith(key + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();
}

public sealed class FolderMetadata
{
    public string? Kind { get; set; }

    /// <summary>GlazeWM workspace bound to the topic.</summary>
    public string? Workspace { get; set; }

    public string? Color { get; set; }

    public TilePlacement? Tile { get; set; }

    [JsonIgnore]
    public bool IsEmpty => Kind is null && Workspace is null && Color is null && Tile is null;
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
