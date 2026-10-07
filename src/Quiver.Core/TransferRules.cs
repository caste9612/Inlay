namespace Quiver.Core;

public enum TransferKind
{
    None,
    Move,
    Copy,
}

/// <summary>What dropping files onto a folder does, following the same rules as Explorer.</summary>
public static class TransferRules
{
    /// <param name="forceCopy">Ctrl held down.</param>
    /// <param name="forceMove">Shift held down.</param>
    public static TransferKind Choose(IReadOnlyList<string> sources, string destination, bool forceCopy, bool forceMove)
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

        if (forceCopy)
            return TransferKind.Copy;
        if (forceMove)
            return TransferKind.Move;
        return normalizedSources.All(source => IsSameVolume(source, destination)) ? TransferKind.Move : TransferKind.Copy;
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

    private static bool IsSameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
