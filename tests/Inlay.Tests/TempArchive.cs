using Inlay.Core;

namespace Inlay.Tests;

/// <summary>A throwaway archive folder on disk, deleted at the end of the test.</summary>
internal sealed class TempArchive : IDisposable
{
    public TempArchive()
    {
        Root = Path.Combine(Path.GetTempPath(), "inlay-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string AddFolder(string relativePath) =>
        Directory.CreateDirectory(Path.Combine(Root, relativePath)).FullName;

    public string AddFile(string relativePath, string content = "")
    {
        var path = Path.GetFullPath(Path.Combine(Root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public ArchiveReader Reader(ArchiveMetadata? metadata = null) => new(Root, metadata ?? new ArchiveMetadata());

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
