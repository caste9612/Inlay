using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A box on the board.</summary>
public sealed class TileViewModel
{
    public TileViewModel(Tile tile, string root)
    {
        Title = tile.Title;
        FolderPath = tile.FolderPath;
        RelativePath = tile.IsGeneral ? null : ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        Items = [.. tile.Items.Select(item => new ItemViewModel(item))];
    }

    /// <summary>A tile that is not a subfolder of a topic, like the inbox.</summary>
    public TileViewModel(string title, string folderPath, IEnumerable<ArchiveItem> items)
    {
        Title = title;
        FolderPath = folderPath;
        Items = [.. items.Select(item => new ItemViewModel(item))];
    }

    public string Title { get; }

    /// <summary>The folder opened by "Apri cartella".</summary>
    public string FolderPath { get; }

    /// <summary>Metadata key of the tile's own folder; null for tiles that are not a subfolder.</summary>
    public string? RelativePath { get; }

    public IReadOnlyList<ItemViewModel> Items { get; }

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Only tiles that are a folder of their own can be renamed, deleted or promoted.</summary>
    public bool IsOwnFolder => RelativePath is not null;
}
