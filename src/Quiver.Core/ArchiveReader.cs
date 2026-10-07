namespace Quiver.Core;

/// <summary>A folder outside the archive whose files also wait to be sorted, like the Desktop.</summary>
/// <param name="Folders">
/// Where its files are, the first one receiving those dropped on its tile. The Desktop shows both the user's own folder
/// and the one shared by all users, where installers often put their shortcuts.
/// </param>
public sealed record InboxSource(string Title, IReadOnlyList<string> Folders);

/// <summary>
/// Reads the archive following Quiver's rules: folders in the root are topics; deeper folders are topics
/// only when marked in the metadata, otherwise they are tiles on their parent topic's board.
/// </summary>
/// <param name="inboxSources">Folders outside the archive shown among the things to sort, like the Desktop.</param>
public sealed class ArchiveReader(string root, ArchiveMetadata metadata, IReadOnlyList<InboxSource>? inboxSources = null)
{
    public const string GeneralTileTitle = "Generale";

    /// <summary>Title of the inbox tile with the archive's own files to sort.</summary>
    public const string ArchiveInboxTitle = "Nell'archivio";

    public string Root { get; } = Path.GetFullPath(root);

    public string InboxPath => Path.Combine(Root, ArchiveLayout.InboxFolderName);

    /// <summary>The sources outside the archive; a folder inside it is already shown there, and is left out.</summary>
    public IReadOnlyList<InboxSource> InboxSources { get; } = (inboxSources ?? [])
        .Select(source => source with
        {
            Folders = source.Folders
                .Select(folder => Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)))
                .Where(folder => !TransferRules.IsSameOrInside(folder, Path.GetFullPath(root)))
                .ToList(),
        })
        .Where(source => source.Folders.Count > 0)
        .ToList();

    public IReadOnlyList<Topic> ReadTopics() =>
        Directories(new DirectoryInfo(Root))
            .Where(folder => !folder.Name.Equals(ArchiveLayout.InboxFolderName, StringComparison.OrdinalIgnoreCase))
            .Select(ReadTopic)
            .ToList();

    public Board ReadBoard(Topic topic)
    {
        var folder = new DirectoryInfo(topic.FullPath);
        var tiles = new List<Tile>();

        var looseFiles = Files(folder).Select(ArchiveItem.From).ToList();
        if (looseFiles.Count > 0)
            tiles.Add(new Tile(GeneralTileTitle, folder.FullName, IsGeneral: true, looseFiles));

        // Tiles moved by the user keep their place; the others follow in name order (the sort is stable).
        var subfolders = Directories(folder)
            .Where(sub => !IsMarkedTopic(sub))
            .OrderBy(sub => metadata.OrderOf(ArchiveLayout.ToRelativeKey(Root, sub.FullName)) ?? int.MaxValue);
        foreach (var subfolder in subfolders)
            tiles.Add(new Tile(subfolder.Name, subfolder.FullName, IsGeneral: false, ReadItems(subfolder)));

        return new Board(topic, tiles);
    }

    /// <summary>
    /// What waits to be sorted, one tile per place, empty ones left out: first the archive's own, that is the inbox
    /// folder and the files left loose in the root, then the other sources, like the Desktop.
    /// </summary>
    public IReadOnlyList<Tile> ReadInbox()
    {
        var tiles = new List<Tile>();
        var own = Files(new DirectoryInfo(Root)).Select(ArchiveItem.From).ToList();
        own.AddRange(ReadItems(new DirectoryInfo(InboxPath)));
        if (own.Count > 0)
            tiles.Add(new Tile(ArchiveInboxTitle, InboxPath, IsGeneral: false, own));

        foreach (var source in InboxSources)
        {
            var folders = source.Folders.Select(folder => new DirectoryInfo(folder)).ToList();
            // The folder that holds the archive is not something to sort.
            List<ArchiveItem> items =
            [
                .. folders.SelectMany(Directories).Where(folder => !TransferRules.IsSameOrInside(Root, folder.FullName))
                    .OrderBy(folder => folder.Name, NaturalStringComparer.Instance).Select(ArchiveItem.From),
                .. folders.SelectMany(Files).OrderBy(file => file.Name, NaturalStringComparer.Instance).Select(ArchiveItem.From),
            ];
            if (items.Count > 0)
                tiles.Add(new Tile(source.Title, source.Folders[0], IsGeneral: false, items));
        }
        return tiles;
    }

    /// <summary>Whether a file or folder sits among the things to sort: in the inbox, loose in the root, or in a source.</summary>
    public bool IsInInbox(string path)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        return parent is not null &&
               (SameFolder(parent, Root) || SameFolder(parent, InboxPath) ||
                InboxSources.Any(source => source.Folders.Any(folder => SameFolder(parent, folder))));
    }

    private Topic ReadTopic(DirectoryInfo folder)
    {
        var children = Directories(folder).Where(IsMarkedTopic).Select(ReadTopic).ToList();
        return new Topic(folder.FullName, ArchiveLayout.ToRelativeKey(Root, folder.FullName), children);
    }

    private bool IsMarkedTopic(DirectoryInfo folder) =>
        metadata.IsTopic(ArchiveLayout.ToRelativeKey(Root, folder.FullName));

    private static bool SameFolder(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Folders first, then files, like Explorer.</summary>
    private static List<ArchiveItem> ReadItems(DirectoryInfo folder) =>
        [.. Directories(folder).Select(ArchiveItem.From), .. Files(folder).Select(ArchiveItem.From)];

    private static List<DirectoryInfo> Directories(DirectoryInfo folder) =>
        Visible(folder, f => f.EnumerateDirectories("*", ArchiveLayout.TopLevelOnly));

    private static List<FileInfo> Files(DirectoryInfo folder) =>
        Visible(folder, f => f.EnumerateFiles("*", ArchiveLayout.TopLevelOnly));

    private static List<T> Visible<T>(DirectoryInfo folder, Func<DirectoryInfo, IEnumerable<T>> enumerate)
        where T : FileSystemInfo
    {
        List<T> entries;
        try
        {
            entries = enumerate(folder).Where(entry => !ArchiveLayout.IsHiddenName(entry.Name)).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The folder is missing or became unreadable while we were reading it: show it as empty.
            return [];
        }
        entries.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
        return entries;
    }
}
