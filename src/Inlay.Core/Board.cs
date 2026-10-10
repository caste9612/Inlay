namespace Inlay.Core;

/// <summary>A folder shown in the sidebar. Topics can contain other topics.</summary>
/// <param name="Color">
/// The color kept for it, as "#RRGGBB", or <see cref="TopicColors.None"/>; null when it has none yet, as a topic inside
/// another one, which shows the color of its topic.
/// </param>
/// <param name="ColorIsAutomatic">The color was given by Inlay when it was a topic of the root, and not chosen.</param>
public sealed record Topic(string FullPath, string RelativePath, IReadOnlyList<Topic> Children, string? Color = null,
    bool ColorIsAutomatic = false)
{
    public string Name => Path.GetFileName(FullPath);
}

/// <summary>A box on a topic's board: one subfolder, or the topic's loose files and the folders shown as rows ("Generale").</summary>
/// <param name="Items">In the order the tile shows them: sorted, or as given by hand.</param>
/// <param name="Color">The color given to the tile, as "#RRGGBB"; null when it has none.</param>
/// <param name="Size">The size given to the tile; null for the usual one.</param>
/// <param name="Sort">How its items are sorted; null for by name, or when put in order by hand.</param>
/// <param name="ByHand">Its items are in the order given by dragging them.</param>
public sealed record Tile(string Title, string FolderPath, bool IsGeneral, IReadOnlyList<ArchiveItem> Items, string? Color = null,
    TileSize? Size = null, ItemSort? Sort = null, bool ByHand = false);

/// <summary>What the board shows for a topic.</summary>
public sealed record Board(Topic Topic, IReadOnlyList<Tile> Tiles);
