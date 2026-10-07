using System.Globalization;
using System.IO.Enumeration;
using System.Text;

namespace Quiver.Core;

/// <param name="RelativeDirectory">Folder containing the item, relative to the root; empty for the root itself.</param>
public sealed record SearchHit(ArchiveItem Item, string RelativeDirectory, int Score);

/// <summary>In-memory list of every file and folder in the archive, searchable by name.</summary>
public sealed class SearchIndex
{
    private readonly Entry[] _entries;

    private SearchIndex(Entry[] entries) => _entries = entries;

    public int Count => _entries.Length;

    public static SearchIndex Build(string root, CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var items = new FileSystemEnumerable<ArchiveItem>(
            root,
            (ref FileSystemEntry entry) => new ArchiveItem(entry.ToFullPath(), entry.FileName.ToString(), entry.IsDirectory),
            options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !ArchiveLayout.IsHiddenName(entry.FileName),
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                !ArchiveLayout.IsHiddenName(entry.FileName) && !IsLinkToElsewhere(ref entry),
        };

        var entries = new List<Entry>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeDirectory = Path.GetRelativePath(root, Path.GetDirectoryName(item.FullPath) ?? root);
            if (relativeDirectory == ".")
                relativeDirectory = "";
            entries.Add(new Entry(item, relativeDirectory, Normalize(item.DisplayName), Normalize(relativeDirectory)));
        }
        return new SearchIndex([.. entries]);
    }

    /// <summary>
    /// Every word of the query must appear in the name or in the folder path. Matches at the start of the name,
    /// or of a word in it, rank higher; among equal matches, shorter names come first.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, int maxResults = 50)
    {
        var words = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return [];

        var hits = new List<SearchHit>();
        foreach (var entry in _entries)
        {
            var score = Score(entry, words);
            if (score > 0)
                hits.Add(new SearchHit(entry.Item, entry.RelativeDirectory, score));
        }
        hits.Sort((a, b) => a.Score != b.Score
            ? b.Score.CompareTo(a.Score)
            : NaturalStringComparer.Instance.Compare(a.Item.DisplayName, b.Item.DisplayName));
        return hits.Count > maxResults ? hits.GetRange(0, maxResults) : hits;
    }

    private static int Score(Entry entry, string[] words)
    {
        var score = 0;
        foreach (var word in words)
        {
            var index = entry.NameKey.IndexOf(word, StringComparison.Ordinal);
            if (index == 0) score += 100;
            else if (index > 0) score += IsWordStart(entry.NameKey, index) ? 70 : 40;
            else if (entry.PathKey.Contains(word, StringComparison.Ordinal)) score += 10;
            else return 0;
        }
        return Math.Max(1, score - entry.NameKey.Length / 4);
    }

    private static bool IsWordStart(string text, int index) =>
        !char.IsLetterOrDigit(text[index - 1]) || char.IsDigit(text[index]) != char.IsDigit(text[index - 1]);

    /// <summary>Lower case without accents, so "perche" finds "Perché".</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// Symbolic links and junctions can lead outside the archive or back into it, so they are not followed.
    /// Other reparse points, like OneDrive placeholders, are ordinary folders and are searched.
    /// </summary>
    private static bool IsLinkToElsewhere(ref FileSystemEntry entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0 &&
        new DirectoryInfo(entry.ToFullPath()).LinkTarget is not null;

    private sealed record Entry(ArchiveItem Item, string RelativeDirectory, string NameKey, string PathKey);
}
