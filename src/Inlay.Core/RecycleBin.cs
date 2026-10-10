using System.IO.Enumeration;
using System.Text;

namespace Inlay.Core;

/// <summary>
/// Puts back items from the Windows Recycle Bin, as its "Ripristina" command does. The Recycle Bin of each drive keeps
/// every deleted item as two files: "$R…" is the item itself, "$I…" records where it was and when it was deleted.
/// </summary>
/// <param name="folderOf">The current user's Recycle Bin folder on the drive of a path, like C:\$Recycle.Bin\&lt;SID&gt;.</param>
public sealed class RecycleBin(Func<string, string?> folderOf)
{
    private static readonly EnumerationOptions IndexFiles = new() { AttributesToSkip = 0, IgnoreInaccessible = true };

    /// <summary>
    /// Whether deleting on the drive of <paramref name="path"/> goes to the Recycle Bin. On network and removable drives
    /// Windows deletes for good, so nothing is deleted there to undo something else.
    /// </summary>
    public static bool IsAvailableFor(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(path)) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Right after a deletion, finds where each path went: the newest item deleted from it after <paramref name="sinceUtc"/>.
    /// Paths without one were deleted for good, as on a drive without a Recycle Bin.
    /// </summary>
    public IReadOnlyList<RecycledItem> FindDeleted(IEnumerable<string> originalPaths, DateTime sinceUtc)
    {
        var wanted = originalPaths
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, RecycledItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in wanted.Select(folderOf).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(folder))
                continue;
            foreach (var indexPath in RecentIndexFiles(folder, sinceUtc))
            {
                if (ReadIndex(indexPath) is { } item && item.DeletedUtc >= sinceUtc && wanted.Contains(item.OriginalPath) &&
                    (!found.TryGetValue(item.OriginalPath, out var other) || item.DeletedUtc > other.DeletedUtc))
                    found[item.OriginalPath] = item;
            }
        }
        return [.. found.Values];
    }

    /// <summary>Puts an item back where it was.</summary>
    /// <returns>False when it is no longer in the Recycle Bin, or something else took its place.</returns>
    /// <exception cref="IOException">The item could not be moved back.</exception>
    /// <exception cref="UnauthorizedAccessException">The item could not be moved back.</exception>
    public static bool TryRestore(RecycledItem item)
    {
        if (Path.Exists(item.OriginalPath))
            return false;
        var isFolder = Directory.Exists(item.ContentPath);
        if (!isFolder && !File.Exists(item.ContentPath))
            return false;
        Directory.CreateDirectory(Path.GetDirectoryName(item.OriginalPath)!);
        if (isFolder)
            Directory.Move(item.ContentPath, item.OriginalPath);
        else
            File.Move(item.ContentPath, item.OriginalPath);
        try
        {
            File.Delete(item.IndexPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The item is back. The record left behind shows as an empty entry until the Recycle Bin is emptied.
        }
        return true;
    }

    /// <summary>Reads a "$I…" file: version 2 (Windows 10 and later) or version 1 (Vista to 8.1).</summary>
    /// <returns>Null when the file is not one, or cannot be read.</returns>
    public static RecycledItem? ReadIndex(string indexPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(indexPath);
            if (bytes.Length < 24)
                return null;
            var version = BitConverter.ToInt64(bytes, 0);
            var deletedUtc = DateTime.FromFileTimeUtc(BitConverter.ToInt64(bytes, 16));
            string path;
            if (version == 2 && bytes.Length >= 28)
            {
                // The length counts the characters of the path, ending null included.
                var length = BitConverter.ToInt32(bytes, 24);
                if (length <= 0 || 28 + length * 2L > bytes.Length)
                    return null;
                path = Encoding.Unicode.GetString(bytes, 28, length * 2);
            }
            else if (version == 1 && bytes.Length >= 24 + 520)
            {
                path = Encoding.Unicode.GetString(bytes, 24, 520);
            }
            else
            {
                return null;
            }
            var end = path.IndexOf('\0');
            path = end >= 0 ? path[..end] : path;
            return Path.IsPathFullyQualified(path) ? new RecycledItem(indexPath, path, deletedUtc) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The records written since a moment: only those are read, as the Recycle Bin can hold thousands.</summary>
    private static List<string> RecentIndexFiles(string folder, DateTime sinceUtc)
    {
        var writtenAfter = sinceUtc.AddMinutes(-1);
        try
        {
            return new FileSystemEnumerable<string>(folder, (ref FileSystemEntry entry) => entry.ToFullPath(), IndexFiles)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory && entry.FileName.StartsWith("$I", StringComparison.Ordinal) &&
                    entry.LastWriteTimeUtc >= writtenAfter,
            }.ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}

/// <param name="IndexPath">The "$I…" file.</param>
/// <param name="OriginalPath">Where the item was before it was deleted.</param>
public sealed record RecycledItem(string IndexPath, string OriginalPath, DateTime DeletedUtc)
{
    /// <summary>The "$R…" file or folder: the item itself.</summary>
    public string ContentPath => Path.Combine(Path.GetDirectoryName(IndexPath)!, "$R" + Path.GetFileName(IndexPath)[2..]);
}
