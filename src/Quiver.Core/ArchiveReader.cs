namespace Quiver.Core;

/// <summary>
/// Reads the archive following Quiver's rules: folders in the root are topics; deeper folders are topics
/// only when marked in the metadata, otherwise they are tiles on their parent topic's board.
/// </summary>
public sealed class ArchiveReader(string root, ArchiveMetadata metadata)
{
    public const string GeneralTileTitle = "Generale";

    public string Root { get; } = Path.GetFullPath(root);

    public string InboxPath => Path.Combine(Root, ArchiveLayout.InboxFolderName);

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

        foreach (var subfolder in Directories(folder).Where(sub => !IsMarkedTopic(sub)))
            tiles.Add(new Tile(subfolder.Name, subfolder.FullName, IsGeneral: false, ReadItems(subfolder)));

        return new Board(topic, tiles);
    }

    /// <summary>Files waiting to be sorted: the inbox folder plus anything left loose in the root.</summary>
    public IReadOnlyList<ArchiveItem> ReadInbox()
    {
        var items = Files(new DirectoryInfo(Root)).Select(ArchiveItem.From).ToList();
        items.AddRange(ReadItems(new DirectoryInfo(InboxPath)));
        return items;
    }

    private Topic ReadTopic(DirectoryInfo folder)
    {
        var children = Directories(folder).Where(IsMarkedTopic).Select(ReadTopic).ToList();
        return new Topic(folder.FullName, ArchiveLayout.ToRelativeKey(Root, folder.FullName), children);
    }

    private bool IsMarkedTopic(DirectoryInfo folder) =>
        metadata.IsTopic(ArchiveLayout.ToRelativeKey(Root, folder.FullName));

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
