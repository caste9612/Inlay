using System.IO.Enumeration;

namespace Quiver.Core;

/// <param name="RelativeDirectory">
/// Folder containing the item, relative to the root; empty for the root itself. For the items of an inbox source,
/// like the Desktop, the title of the source.
/// </param>
public sealed record SearchHit(ArchiveItem Item, string RelativeDirectory, int Score);

/// <summary>In-memory list of every file and folder in the archive, searchable by name.</summary>
public sealed class SearchIndex
{
    private readonly Entry[] _entries;

    private SearchIndex(Entry[] entries, int archiveCount, long archiveBytes)
    {
        _entries = entries;
        ArchiveCount = archiveCount;
        ArchiveBytes = archiveBytes;
    }

    public int Count => _entries.Length;

    /// <summary>The files and folders of the archive, without those of «Da smistare» that are on the Desktop.</summary>
    public int ArchiveCount { get; }

    /// <summary>What the files of the archive take, the hidden ones and «Da smistare» on the Desktop left out.</summary>
    public long ArchiveBytes { get; }

    public static SearchIndex Build(string root, CancellationToken cancellationToken = default) =>
        Build(root, [], cancellationToken);

    /// <param name="inboxSources">
    /// Also indexed, but only what is shown on their tiles: the Desktop can hold anything, even big folders.
    /// </param>
    public static SearchIndex Build(string root, IReadOnlyList<InboxSource> inboxSources, CancellationToken cancellationToken = default)
    {
        var entries = IndexArchive(Path.GetFullPath(root), cancellationToken);
        var archiveCount = entries.Count;
        var archiveBytes = entries.Sum(entry => entry.Item.Size ?? 0);
        foreach (var source in inboxSources)
        {
            foreach (var item in source.Folders.SelectMany(TopLevelItems))
            {
                // The folder holding the archive does not count, as on the inbox tiles.
                if (item.IsFolder && TransferRules.IsSameOrInside(Path.GetFullPath(root), item.FullPath))
                    continue;
                entries.Add(new Entry(item, source.Title, SearchText.Normalize(item.DisplayName), SearchText.Normalize(source.Title)));
            }
        }
        return new SearchIndex([.. entries], archiveCount, archiveBytes);
    }

    private static List<Entry> IndexArchive(string root, CancellationToken cancellationToken)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var items = new FileSystemEnumerable<ArchiveItem>(
            root,
            (ref FileSystemEntry entry) => new ArchiveItem(
                entry.ToFullPath(), entry.FileName.ToString(), entry.IsDirectory, entry.LastWriteTimeUtc.LocalDateTime,
                entry.IsDirectory ? null : entry.Length),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !ArchiveLayout.IsHiddenName(entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                !ArchiveLayout.IsHiddenName(entry.FileName) && !ArchiveLayout.IsLinkToElsewhere(ref entry),
        };

        var entries = new List<Entry>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetRelativePath(root, Path.GetDirectoryName(item.FullPath) ?? root);
            if (relativeDirectory == ".")
                relativeDirectory = "";
            entries.Add(new Entry(item, relativeDirectory, SearchText.Normalize(item.DisplayName), SearchText.Normalize(relativeDirectory)));
        }
        return entries;
    }

    private static IEnumerable<ArchiveItem> TopLevelItems(string folder)
    {
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        try
        {
            return new FileSystemEnumerable<ArchiveItem>(
                folder,
                (ref FileSystemEntry entry) => new ArchiveItem(
                    entry.ToFullPath(), entry.FileName.ToString(), entry.IsDirectory, entry.LastWriteTimeUtc.LocalDateTime),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) => !ArchiveLayout.IsHiddenName(entry.FileName),
            }.ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Every word of the query must appear in the name or in the folder path. Matches at the start of the name,
    /// or of a word in it, rank higher; among equal matches, shorter names come first.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, int maxResults = 50)
    {
        var words = SearchText.Words(query);
        if (words.Length == 0)
            return [];

        var hits = new List<SearchHit>();
        foreach (var entry in _entries)
        {
            var score = SearchText.Score(entry.NameKey, entry.PathKey, words);
            if (score > 0)
                hits.Add(new SearchHit(entry.Item, entry.RelativeDirectory, score));
        }
        hits.Sort((a, b) => a.Score != b.Score
            ? b.Score.CompareTo(a.Score)
            : NaturalStringComparer.Instance.Compare(a.Item.DisplayName, b.Item.DisplayName));
        return hits.Count > maxResults ? hits.GetRange(0, maxResults) : hits;
    }

    /// <summary>
    /// Symbolic links and junctions can lead outside the archive or back into it, so they are not followed.
    /// Other reparse points, like OneDrive placeholders, are ordinary folders and are searched.
    /// </summary>
    private sealed record Entry(ArchiveItem Item, string RelativeDirectory, string NameKey, string PathKey);
}
