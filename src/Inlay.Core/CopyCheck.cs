namespace Inlay.Core;

/// <summary>
/// Compares a copy of a folder with the original, file by file and byte by byte: only a copy found identical lets the
/// original go, when an archive moves to another drive.
/// </summary>
public static class CopyCheck
{
    private const int BufferSize = 1 << 20;

    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    /// <param name="Done">Bytes compared so far.</param>
    /// <param name="Total">Bytes to compare in all.</param>
    public readonly record struct Progress(long Done, long Total);

    /// <returns>The files, relative to the folders, that are missing in the copy or differ from the original.</returns>
    /// <exception cref="OperationCanceledException">The comparison was stopped.</exception>
    public static IReadOnlyList<string> Differences(string original, string copy, IProgress<Progress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var files = new DirectoryInfo(original).EnumerateFiles("*", Everything)
            // Links to other places are not copies of anything here.
            .Where(file => (file.Attributes & FileAttributes.ReparsePoint) == 0)
            .ToList();
        var total = files.Sum(file => file.Length);
        long done = 0;
        var differences = new List<string>();
        var left = new byte[BufferSize];
        var right = new byte[BufferSize];
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(original, file.FullName);
            var other = new FileInfo(Path.Combine(copy, relative));
            if (!other.Exists || other.Length != file.Length || !SameContent(file, other, left, right, cancellationToken))
                differences.Add(relative);
            done += file.Length;
            progress?.Report(new Progress(done, total));
        }
        return differences;
    }

    private static bool SameContent(FileInfo a, FileInfo b, byte[] left, byte[] right, CancellationToken cancellationToken)
    {
        try
        {
            using var first = new FileStream(a.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            using var second = new FileStream(b.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Same length, same position: both fill the buffer, or both stop at the same end.
                var read = first.ReadAtLeast(left, left.Length, throwOnEndOfStream: false);
                var readOther = second.ReadAtLeast(right, right.Length, throwOnEndOfStream: false);
                if (read != readOther || !left.AsSpan(0, read).SequenceEqual(right.AsSpan(0, readOther)))
                    return false;
                if (read == 0)
                    return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read is not known to be the same.
            return false;
        }
    }
}
