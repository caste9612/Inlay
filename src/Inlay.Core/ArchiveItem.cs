namespace Inlay.Core;

/// <summary>A file or folder shown by Inlay.</summary>
/// <param name="Modified">Last change, in local time; null when unknown.</param>
/// <param name="Size">The length of a file, in bytes; null for folders and when unknown.</param>
public sealed record ArchiveItem(string FullPath, string Name, bool IsFolder, DateTime? Modified = null, long? Size = null)
{
    /// <summary>Like Explorer, shortcuts (.lnk, .url) are shown without their extension.</summary>
    public string DisplayName => !IsFolder && IsShortcut(Name) ? Path.GetFileNameWithoutExtension(Name) : Name;

    public static bool IsShortcut(string name) =>
        name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    // The length comes with the entry of the folder listing: reading it costs nothing more.
    internal static ArchiveItem From(FileSystemInfo info) =>
        new(info.FullName, info.Name, info is DirectoryInfo, info.LastWriteTime, info is FileInfo file ? file.Length : null);
}
