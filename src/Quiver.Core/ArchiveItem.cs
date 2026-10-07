namespace Quiver.Core;

/// <summary>A file or folder shown by Quiver.</summary>
/// <param name="Modified">Last change, in local time; null when unknown.</param>
public sealed record ArchiveItem(string FullPath, string Name, bool IsFolder, DateTime? Modified = null)
{
    /// <summary>Like Explorer, shortcuts (.lnk, .url) are shown without their extension.</summary>
    public string DisplayName => !IsFolder && IsShortcut(Name) ? Path.GetFileNameWithoutExtension(Name) : Name;

    public static bool IsShortcut(string name) =>
        name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    internal static ArchiveItem From(FileSystemInfo info) =>
        new(info.FullName, info.Name, info is DirectoryInfo, info.LastWriteTime);
}
