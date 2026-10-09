namespace Quiver.Core;

/// <summary>
/// What a file operation changes, to tell which operations can run at the same time: the <see cref="Items"/> it moves,
/// copies or deletes, and the places they land, each with everything inside it; and the <see cref="Folders"/> where
/// things arrive, whose list of names it changes. Two operations that change nothing of each other run together; the
/// others one after the other, each starting from the files as the previous one left them.
/// </summary>
public sealed class FileTouches
{
    public FileTouches(IEnumerable<string> items, IEnumerable<string>? folders = null)
    {
        Items = items.Select(Normalize).ToList();
        Folders = (folders ?? []).Select(Normalize).ToList();
    }

    /// <summary>Full paths, without a separator at the end.</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>Full paths, without a separator at the end.</summary>
    public IReadOnlyList<string> Folders { get; }

    /// <summary>
    /// A move, copy or shortcut: the items, where each lands with its name, and the folder they land in, whose new names
    /// tell afterwards which ones are the copies.
    /// </summary>
    public static FileTouches Transfer(IEnumerable<string> paths, string destinationFolder)
    {
        var sources = paths.ToList();
        return new FileTouches([.. sources, .. sources.Select(path => Path.Combine(destinationFolder, Path.GetFileName(Normalize(path))))],
            [destinationFolder]);
    }

    /// <summary>
    /// Whether the two would get in each other's way: an item of one is, or holds, or is inside an item of the other; both
    /// put things in the same folder; or one moves or deletes the folder where the other puts things. Taking an item out
    /// of a folder where the other puts things does not count: the names that arrive there are told apart all the same.
    /// </summary>
    public bool Overlaps(FileTouches other) =>
        Items.Any(mine => other.Items.Any(theirs => TransferRules.IsSameOrInside(mine, theirs) || TransferRules.IsSameOrInside(theirs, mine))) ||
        Folders.Any(folder => other.Folders.Contains(folder, StringComparer.OrdinalIgnoreCase) || other.Takes(folder)) ||
        other.Folders.Any(Takes);

    /// <summary>Whether it moves or deletes this folder, or one around it.</summary>
    private bool Takes(string folder) => Items.Any(item => TransferRules.IsSameOrInside(folder, item));

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
