using System.IO.Enumeration;

namespace Inlay.Core;

/// <summary>How many bytes files and folders hold: what a copy of them needs on the disk it goes to.</summary>
public static class FolderSize
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// The bytes of the files at these paths, those inside the folders too, hidden ones included. A link to another place
    /// counts as itself, not as what it points to; what cannot be read counts nothing.
    /// </summary>
    /// <exception cref="OperationCanceledException">The count was stopped.</exception>
    public static long Of(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        long total = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(path))
                    total += new FileInfo(path).Length;
                else if (Directory.Exists(path))
                    total += Inside(path, cancellationToken);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return total;
    }

    private static long Inside(string folder, CancellationToken cancellationToken)
    {
        var lengths = new FileSystemEnumerable<long>(folder, (ref FileSystemEntry entry) => entry.Length, Everything)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory,
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !ArchiveLayout.IsLinkToElsewhere(ref entry),
        };
        long total = 0;
        foreach (var length in lengths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += length;
        }
        return total;
    }
}
