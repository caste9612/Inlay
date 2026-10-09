namespace Quiver.Core;

public enum TransferKind
{
    None,
    Move,
    Copy,

    /// <summary>A shortcut to the item, which stays where it is.</summary>
    Link,
}

/// <summary>What dropping files onto a folder does, following the same rules as Explorer.</summary>
public static class TransferRules
{
    /// <param name="forceCopy">Ctrl held down.</param>
    /// <param name="forceMove">Shift held down.</param>
    /// <param name="forceLink">Alt held down. Ctrl and Shift together ask for a shortcut too, as in Explorer.</param>
    /// <param name="preferLink">Without keys, a shortcut: the items have a place of their own (see <see cref="HasItsOwnPlace"/>).</param>
    public static TransferKind Choose(IReadOnlyList<string> sources, string destination, bool forceCopy, bool forceMove,
        bool forceLink = false, bool preferLink = false)
    {
        if (sources.Count == 0)
            return TransferKind.None;

        destination = Normalize(destination);
        var normalizedSources = sources.Select(Normalize).ToList();
        // A folder cannot go inside itself or inside one of its subfolders.
        if (normalizedSources.Any(source => IsSameOrInside(destination, source)))
            return TransferKind.None;
        // Dropping items onto the folder they are already in does nothing.
        if (normalizedSources.All(source => string.Equals(Path.GetDirectoryName(source), destination, StringComparison.OrdinalIgnoreCase)))
            return TransferKind.None;

        if (forceLink || (forceCopy && forceMove))
            return TransferKind.Link;
        if (forceCopy)
            return TransferKind.Copy;
        if (forceMove)
            return TransferKind.Move;
        if (preferLink)
            return TransferKind.Link;
        return normalizedSources.All(source => IsSameVolume(source, destination)) ? TransferKind.Move : TransferKind.Copy;
    }

    /// <summary>
    /// What already has its place outside the archive and enters it as a shortcut: installed programs, which do not work
    /// away from their folder (Explorer makes a shortcut when one is dragged), and what is on the network, like the folder
    /// of a job on the server.
    /// </summary>
    public static bool HasItsOwnPlace(string path, string archiveRoot)
    {
        path = Normalize(path);
        if (IsSameOrInside(path, Normalize(archiveRoot)))
            return false;
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || IsOnNetwork(path);
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> itself or something inside it.</summary>
    public static bool IsSameOrInside(string path, string folder)
    {
        if (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase))
            return true;
        // A drive root like D:\ already ends with a separator.
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOnNetwork(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        try
        {
            return Path.GetPathRoot(path) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
