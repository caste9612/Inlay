using System.Collections.ObjectModel;
using System.Windows.Media;
using Quiver.App.Views;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A box on the board.</summary>
public sealed partial class TileViewModel : RenameableViewModel
{
    private readonly IReadOnlySet<string> _cutPaths;

    public TileViewModel(Tile tile, string root, IReadOnlySet<string> cutPaths)
        : this(tile.Title, tile.FolderPath, tile.Items, cutPaths)
    {
        IsGeneral = tile.IsGeneral;
        RelativePath = tile.IsGeneral ? null : ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        Color = tile.Color;
    }

    /// <summary>A tile that is not a subfolder of a topic, like the inbox.</summary>
    public TileViewModel(string title, string folderPath, IReadOnlyList<ArchiveItem> items, IReadOnlySet<string> cutPaths)
    {
        _cutPaths = cutPaths;
        Title = title;
        FolderPath = folderPath;
        Items = new ObservableCollection<ItemViewModel>(items.Select(NewItem));
    }

    public string Title { get; private set; }

    /// <summary>Where files dropped or pasted on the tile go.</summary>
    public string FolderPath { get; }

    public bool IsGeneral { get; }

    /// <summary>Metadata key of the tile's own folder; null for tiles that are not a subfolder.</summary>
    public string? RelativePath { get; }

    public ObservableCollection<ItemViewModel> Items { get; }

    /// <summary>The color given to the tile, as "#RRGGBB"; null when it has none.</summary>
    public string? Color { get; private set; }

    /// <summary>The stripe on the title, in the tile's color; null when it has none.</summary>
    public Brush? ColorBrush => TileColors.BrushFor(Color);

    public bool HasColor => ColorBrush is not null;

    public int Count => Items.Count;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Only tiles that are a folder of their own can be moved, renamed, deleted, colored or promoted.</summary>
    public bool IsOwnFolder => RelativePath is not null;

    /// <summary>What tells a tile from the others on its board: its folder, and whether it is the topic's own.</summary>
    public string Key => KeyOf(FolderPath, IsGeneral);

    public static string KeyOf(string folderPath, bool isGeneral) => (isGeneral ? "general:" : "folder:") + folderPath;

    public override string RenamePath => FolderPath;

    protected override bool RenamesFolder => true;

    public void Update(Tile tile)
    {
        Update(tile.Title, tile.Items);
        if (string.Equals(Color, tile.Color, StringComparison.OrdinalIgnoreCase))
            return;
        Color = tile.Color;
        OnPropertyChanged(nameof(Color));
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(HasColor));
    }

    /// <summary>The tile read again: the items still there keep their view models, selected or being renamed as they were.</summary>
    public void Update(string title, IReadOnlyList<ArchiveItem> items)
    {
        if (!string.Equals(Title, title, StringComparison.Ordinal))
        {
            Title = title;
            OnPropertyChanged(nameof(Title));
        }
        Reconciler.Reconcile(Items, items, KeyOf, item => KeyOf(item.Item), NewItem, (viewModel, item) =>
        {
            viewModel.Update(item);
            viewModel.IsCut = _cutPaths.Contains(item.FullPath);
        });
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private ItemViewModel NewItem(ArchiveItem item) => new(item) { IsCut = _cutPaths.Contains(item.FullPath) };

    /// <summary>A file and a folder with the same name are two different things.</summary>
    private static string KeyOf(ArchiveItem item) => (item.IsFolder ? "folder:" : "file:") + item.FullPath;
}
