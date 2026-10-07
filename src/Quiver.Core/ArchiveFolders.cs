namespace Quiver.Core;

/// <summary>
/// What makes a folder an archive, as a vault in Obsidian: any folder can be one, and the hidden folder .quiver inside it
/// marks it and keeps its metadata. Which archives this PC knows is in its settings; here are the rules about the folders.
/// </summary>
public static class ArchiveFolders
{
    /// <summary>Whether the folder is already an archive.</summary>
    public static bool IsArchive(string folder) =>
        Directory.Exists(Path.Combine(folder, ArchiveLayout.MetadataFolderName));

    /// <summary>Why a folder cannot be an archive, or null when it can.</summary>
    /// <param name="reserved">
    /// Folders of Windows that cannot be an archive, nor contain one: the Desktop, which is among the things to sort,
    /// the user's own folder, the system folders. The first that applies gives the reason.
    /// </param>
    /// <param name="otherArchives">The archives already known: an archive cannot be inside another, nor contain one.</param>
    public static string? Check(string folder, IReadOnlyList<ReservedFolder> reserved, IEnumerable<string> otherArchives)
    {
        folder = Normalize(folder);
        if (string.Equals(Path.GetPathRoot(folder)?.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return "Un disco intero non può fare da archivio: crea una cartella dentro, per esempio «Archivio».";
        foreach (var place in reserved)
        {
            var path = Normalize(place.Path);
            var inside = TransferRules.IsSameOrInside(folder, path) && !(place.InsideAllowed && !string.Equals(folder, path, StringComparison.OrdinalIgnoreCase));
            if (inside || TransferRules.IsSameOrInside(path, folder))
                return place.Reason;
        }
        foreach (var other in otherArchives.Select(Normalize))
        {
            if (string.Equals(other, folder, StringComparison.OrdinalIgnoreCase))
                continue;
            if (TransferRules.IsSameOrInside(folder, other))
                return $"Questa cartella è dentro l'archivio «{Path.GetFileName(other)}»: un archivio non sta dentro un altro.";
            if (TransferRules.IsSameOrInside(other, folder))
                return $"Questa cartella contiene l'archivio «{Path.GetFileName(other)}»: un archivio non ne contiene un altro.";
        }
        return null;
    }

    /// <summary>Makes a folder an archive: its .quiver, with empty metadata. What is in it already stays as it is.</summary>
    public static void Adopt(string folder)
    {
        Directory.CreateDirectory(folder);
        if (!File.Exists(MetadataStore.GetPath(folder)))
            MetadataStore.Save(folder, new ArchiveMetadata());
    }

    /// <summary>A new, empty archive named <paramref name="name"/> in <paramref name="parent"/>.</summary>
    /// <returns>Its folder.</returns>
    /// <exception cref="IOException">The name is taken, or the folder cannot be made.</exception>
    public static string Create(string parent, string name)
    {
        var folder = Path.Combine(parent, name);
        if (Path.Exists(folder))
            throw new IOException($"In «{parent}» c'è già «{name}».");
        Adopt(folder);
        return folder;
    }

    /// <summary>Whether moving the archive into <paramref name="destinationParent"/> is a mere rename on the same drive.</summary>
    public static bool IsSameDrive(string archive, string destinationParent) =>
        string.Equals(Path.GetPathRoot(Normalize(archive)), Path.GetPathRoot(Normalize(destinationParent)), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

/// <summary>A folder of Windows that cannot be an archive, nor contain one.</summary>
/// <param name="InsideAllowed">An archive can be inside it, like in the user's own folder.</param>
public sealed record ReservedFolder(string Path, string Reason, bool InsideAllowed = false);
