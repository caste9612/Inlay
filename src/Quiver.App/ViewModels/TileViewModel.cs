using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A box on the board.</summary>
public sealed partial class TileViewModel : RenameableViewModel
{
    public TileViewModel(Tile tile, string root, IReadOnlySet<string> cutPaths)
    {
        Title = tile.Title;
        FolderPath = tile.FolderPath;
        IsGeneral = tile.IsGeneral;
        RelativePath = tile.IsGeneral ? null : ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        Items = [.. tile.Items.Select(item => new ItemViewModel(item) { IsCut = cutPaths.Contains(item.FullPath) })];
    }

    /// <summary>A tile that is not a subfolder of a topic, like the inbox.</summary>
    public TileViewModel(string title, string folderPath, IEnumerable<ArchiveItem> items, IReadOnlySet<string> cutPaths)
    {
        Title = title;
        FolderPath = folderPath;
        Items = [.. items.Select(item => new ItemViewModel(item) { IsCut = cutPaths.Contains(item.FullPath) })];
    }

    public string Title { get; }

    /// <summary>Where files dropped or pasted on the tile go.</summary>
    public string FolderPath { get; }

    public bool IsGeneral { get; }

    /// <summary>Metadata key of the tile's own folder; null for tiles that are not a subfolder.</summary>
    public string? RelativePath { get; }

    public IReadOnlyList<ItemViewModel> Items { get; }

    public int Count => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Only tiles that are a folder of their own can be moved, renamed, deleted or promoted.</summary>
    public bool IsOwnFolder => RelativePath is not null;

    public override string RenamePath => FolderPath;

    protected override bool RenamesFolder => true;
}
