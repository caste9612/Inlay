using System.IO.Enumeration;

namespace Inlay.Core;

/// <param name="RelativeDirectory">Folder containing the item, relative to the root; empty for the root itself.</param>
public sealed record SearchHit(ArchiveItem Item, string RelativeDirectory, int Score);

/// <summary>In-memory list of every file and folder in the archive, searchable by name, by kind and by place.</summary>
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

    /// <summary>The files and folders of the archive, the hidden ones left out.</summary>
    public int ArchiveCount { get; }

    /// <summary>What the files of the archive take, the hidden ones left out.</summary>
    public long ArchiveBytes { get; }

    public static SearchIndex Build(string root, CancellationToken cancellationToken = default)
    {
        var entries = IndexArchive(Path.GetFullPath(root), cancellationToken);
        return new SearchIndex([.. entries], entries.Count, entries.Sum(entry => entry.Item.Size ?? 0));
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
            entries.Add(new Entry(item, relativeDirectory, SearchText.Normalize(item.DisplayName), SearchText.Normalize(relativeDirectory),
                FileKinds.Of(item)));
        }
        return entries;
    }

    /// <inheritdoc cref="Search(SearchQuery, int)"/>
    public IReadOnlyList<SearchHit> Search(string query, int maxResults = 50) => Search(SearchQuery.Parse(query), maxResults);

    /// <summary>
    /// Every word of the query must appear in the name or in the folder path, and the item must pass its filters (see
    /// <see cref="SearchQuery"/>). Matches at the start of the name, or of a word in it, rank higher; among equal matches,
    /// shorter names come first. With filters and no words, what passes them comes the newest first.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(SearchQuery query, int maxResults = 50)
    {
        if (query.IsEmpty)
            return [];

        var words = query.Words.ToArray();
        var hits = new List<SearchHit>();
        foreach (var entry in _entries)
        {
            if (query.HasFilters && !query.Admits(entry.Item, entry.Kind, entry.PathKey))
                continue;
            var score = words.Length == 0 ? 1 : SearchText.Score(entry.NameKey, entry.PathKey, words);
            if (score > 0)
                hits.Add(new SearchHit(entry.Item, entry.RelativeDirectory, score));
        }
        if (words.Length == 0)
            hits.Sort((a, b) => Nullable.Compare(b.Item.Modified, a.Item.Modified) is var newer and not 0
                ? newer
                : NaturalStringComparer.Instance.Compare(a.Item.DisplayName, b.Item.DisplayName));
        else
            hits.Sort((a, b) => a.Score != b.Score
                ? b.Score.CompareTo(a.Score)
                : NaturalStringComparer.Instance.Compare(a.Item.DisplayName, b.Item.DisplayName));
        return hits.Count > maxResults ? hits.GetRange(0, maxResults) : hits;
    }

    /// <summary>
    /// Symbolic links and junctions can lead outside the archive or back into it, so they are not followed.
    /// Other reparse points, like OneDrive placeholders, are ordinary folders and are searched.
    /// </summary>
    /// <param name="Kind">For the filters by kind: worked out once, not at every search.</param>
    private sealed record Entry(ArchiveItem Item, string RelativeDirectory, string NameKey, string PathKey, FileKind Kind);
}
