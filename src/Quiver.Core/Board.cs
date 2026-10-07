namespace Quiver.Core;

/// <summary>A folder shown in the sidebar. Topics can contain other topics.</summary>
public sealed record Topic(string FullPath, string RelativePath, IReadOnlyList<Topic> Children)
{
    public string Name => Path.GetFileName(FullPath);
}

/// <summary>A box on a topic's board: one subfolder, or the topic's loose files ("Generale").</summary>
public sealed record Tile(string Title, string FolderPath, bool IsGeneral, IReadOnlyList<ArchiveItem> Items);

/// <summary>What the board shows for a topic.</summary>
public sealed record Board(Topic Topic, IReadOnlyList<Tile> Tiles);
