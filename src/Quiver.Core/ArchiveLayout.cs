using System.IO.Enumeration;

namespace Quiver.Core;

/// <summary>
/// Names and visibility rules shared by everything that reads the archive.
/// </summary>
public static class ArchiveLayout
{
    /// <summary>Hidden folder in the archive root that holds Quiver's own data.</summary>
    public const string MetadataFolderName = ".quiver";

    public const string MetadataFileName = "archive.json";

    internal static readonly EnumerationOptions TopLevelOnly = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    /// <summary>Dot-prefixed names (like .quiver or .git) are internal and never shown.</summary>
    public static bool IsHiddenName(ReadOnlySpan<char> name) => name.Length > 0 && name[0] == '.';

    /// <summary>A junction or a symbolic link: what it holds is somewhere else, and going in could go round in circles.</summary>
    internal static bool IsLinkToElsewhere(ref FileSystemEntry entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0 &&
        new DirectoryInfo(entry.ToFullPath()).LinkTarget is not null;

    /// <summary>Metadata key for a path: relative to the root, with forward slashes.</summary>
    public static string ToRelativeKey(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    /// <summary>Whether the metadata key <paramref name="key"/> is <paramref name="folderKey"/> itself or something inside it.</summary>
    public static bool IsKeyAtOrBelow(string key, string folderKey) =>
        key.Equals(folderKey, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(folderKey + "/", StringComparison.OrdinalIgnoreCase);
}
