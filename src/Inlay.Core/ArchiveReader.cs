namespace Inlay.Core;

/// <summary>A place where files can be sent: a tile, or a topic for the files it keeps loose.</summary>
/// <param name="RelativeKey">The metadata key of its folder.</param>
/// <param name="Location">
/// Where it sits, like "Lavoro › Clienti"; for a tile, its topic is the last part. Empty for a topic in the root.
/// </param>
public sealed record Destination(string Title, string FullPath, string RelativeKey, bool IsTopic, string Location);

/// <summary>
/// Reads the archive following Inlay's rules: folders in the root are topics; deeper folders are topics
/// only when marked in the metadata, otherwise they are tiles on their parent topic's board, or rows of its
/// «Generale» when marked so.
/// </summary>
public sealed class ArchiveReader(string root, ArchiveMetadata metadata)
{
    public const string GeneralTileTitle = "Generale";

    /// <summary>Between the names of a path shown to the user, as in "Lavoro › Clienti".</summary>
    public const string PathSeparator = " › ";

    public string Root { get; } = Path.GetFullPath(root);

    /// <summary>The topics in the root, and inside them those marked, in the order given by dragging them, the others by name.</summary>
    public IReadOnlyList<Topic> ReadTopics() =>
        InOrder(Directories(new DirectoryInfo(Root)))
            .Select(ReadTopic)
            .ToList();

    /// <summary>The files left loose in the root: no topic shows them, so the status bar tells how many there are.</summary>
    public IReadOnlyList<ArchiveItem> ReadRootFiles() => Files(new DirectoryInfo(Root)).Select(ArchiveItem.From).ToList();

    public Board ReadBoard(Topic topic)
    {
        var folder = new DirectoryInfo(topic.FullPath);
        var subfolders = Directories(folder);
        var tiles = new List<Tile>();

        // "Generale" goes by the topic's own key: it holds the files of the topic's folder, and the folders shown as its
        // rows, like one dropped on the topic. Folders first, as in every tile.
        List<ArchiveItem> general = [.. subfolders.Where(IsMarkedItem).Select(ArchiveItem.From), .. Files(folder).Select(ArchiveItem.From)];
        if (general.Count > 0)
            tiles.Add(ReadTile(GeneralTileTitle, folder.FullName, isGeneral: true, general, color: null));

        foreach (var subfolder in TilesAmong(subfolders))
        {
            var color = metadata.ChosenColorOf(ArchiveLayout.ToRelativeKey(Root, subfolder.FullName));
            tiles.Add(ReadTile(subfolder.Name, subfolder.FullName, isGeneral: false, ReadItems(subfolder), color));
        }

        return new Board(topic, tiles);
    }

    /// <summary>A tile with what its metadata says: its size, and the order of its items.</summary>
    private Tile ReadTile(string title, string folder, bool isGeneral, IReadOnlyList<ArchiveItem> items, string? color)
    {
        var key = ArchiveLayout.ToRelativeKey(Root, folder);
        var sort = metadata.SortOf(key);
        var byHand = metadata.ItemOrderOf(key);
        return new Tile(title, folder, isGeneral, ItemArrangement.Arrange(items, sort, byHand), color, metadata.SizeOf(key),
            byHand is { Count: > 0 } ? null : sort, ByHand: byHand is { Count: > 0 });
    }

    /// <summary>
    /// Where files can be sent, in the order of the sidebar: each topic, for its loose files, then its tiles, then
    /// the topics inside it.
    /// </summary>
    public IReadOnlyList<Destination> ReadDestinations()
    {
        var destinations = new List<Destination>();
        foreach (var topic in ReadTopics())
            AddDestinations(topic, "", destinations);
        return destinations;
    }

    private Topic ReadTopic(DirectoryInfo folder)
    {
        var children = InOrder(Directories(folder).Where(IsMarkedTopic)).Select(ReadTopic).ToList();
        var key = ArchiveLayout.ToRelativeKey(Root, folder.FullName);
        return new Topic(folder.FullName, key, children, metadata.ColorOf(key), metadata.IsColorAutomatic(key));
    }

    /// <summary>Those moved by the user keep their place, the others follow in name order (the sort is stable).</summary>
    private IEnumerable<DirectoryInfo> InOrder(IEnumerable<DirectoryInfo> folders) =>
        folders.OrderBy(folder => metadata.OrderOf(ArchiveLayout.ToRelativeKey(Root, folder.FullName)) ?? int.MaxValue);

    private void AddDestinations(Topic topic, string location, List<Destination> destinations)
    {
        destinations.Add(new Destination(topic.Name, topic.FullPath, topic.RelativePath, IsTopic: true, location));
        var inside = location.Length == 0 ? topic.Name : location + PathSeparator + topic.Name;
        foreach (var folder in TileFolders(new DirectoryInfo(topic.FullPath)))
            destinations.Add(new Destination(folder.Name, folder.FullName, ArchiveLayout.ToRelativeKey(Root, folder.FullName), IsTopic: false, inside));
        foreach (var child in topic.Children)
            AddDestinations(child, inside, destinations);
    }

    /// <summary>The subfolders of a topic that are its tiles, in the order of its board.</summary>
    private IEnumerable<DirectoryInfo> TileFolders(DirectoryInfo topicFolder) => TilesAmong(Directories(topicFolder));

    /// <summary>The subfolders of a topic that are neither topics nor rows of «Generale», in the order of its board.</summary>
    private IEnumerable<DirectoryInfo> TilesAmong(IEnumerable<DirectoryInfo> subfolders) =>
        InOrder(subfolders.Where(sub => !IsMarkedTopic(sub) && !IsMarkedItem(sub)));

    private bool IsMarkedTopic(DirectoryInfo folder) =>
        metadata.IsTopic(ArchiveLayout.ToRelativeKey(Root, folder.FullName));

    private bool IsMarkedItem(DirectoryInfo folder) =>
        metadata.IsItem(ArchiveLayout.ToRelativeKey(Root, folder.FullName));

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
