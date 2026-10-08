using System.Collections.ObjectModel;
using System.Windows.Media;
using Quiver.App.Views;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A box on the board.</summary>
public sealed partial class TileViewModel : RenameableViewModel
{
    /// <summary>The rows a tile shows before scrolling, when nobody gave it a height: about ten.</summary>
    public const double UsualListHeight = 268;

    private readonly IReadOnlySet<string> _cutPaths;
    private TileSize? _size;

    public TileViewModel(Tile tile, string root, IReadOnlySet<string> cutPaths)
        : this(tile.Title, tile.FolderPath, tile.Items, cutPaths)
    {
        IsGeneral = tile.IsGeneral;
        RelativePath = tile.IsGeneral ? null : ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        // "Generale" keeps its size and order under its topic's key: it holds the files of the topic's folder.
        MetadataKey = ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        Color = tile.Color;
        _size = tile.Size;
        Sort = tile.Sort;
        ByHand = tile.ByHand;
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

    /// <summary>Where its size and the order of its items are kept; null for the tiles of the inbox, outside the archive.</summary>
    public string? MetadataKey { get; }

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

    /// <summary>Tiles of the archive, "Generale" included, can be resized and put in order; those of the inbox cannot.</summary>
    public bool CanArrange => MetadataKey is not null;

    /// <summary>The size given to the tile, also while its corner is being dragged; null for the usual one.</summary>
    public TileSize? Size
    {
        get => _size;
        set
        {
            if (_size == value)
                return;
            _size = value;
            OnPropertyChanged(nameof(Size));
            OnPropertyChanged(nameof(HasSize));
            OnPropertyChanged(nameof(Columns));
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(ListMaxHeight));
        }
    }

    /// <summary>It was given a size of its own, which «Misura automatica» takes away.</summary>
    public bool HasSize => Size is not null;

    /// <summary>The columns of the board it takes.</summary>
    public int Columns => Size?.Columns ?? 1;

    /// <summary>The rows its list shows before scrolling, when it was given a height; 0 to follow its items.</summary>
    public int Rows => Size?.Rows ?? 0;

    /// <summary>The usual limit, or none when the tile has a height of its own.</summary>
    public double ListMaxHeight => Size?.Rows is null ? UsualListHeight : double.PositiveInfinity;

    /// <summary>How its items are sorted; null for by name, or when they were put in order by hand.</summary>
    public ItemSort? Sort { get; private set; }

    /// <summary>Its items are in the order given by dragging them.</summary>
    public bool ByHand { get; private set; }

    /// <summary>What a key in "Ordina per" shows checked: the one in use, by name when nothing else is.</summary>
    public SortKey? SortedBy => ByHand ? null : Sort?.Key ?? SortKey.Name;

    public bool IsDescending => !ByHand && (Sort?.Descending ?? false);

    /// <summary>"ascending" or "descending", for the check in «Ordina per»; empty when the items are in order by hand.</summary>
    public string Direction => ByHand ? "" : IsDescending ? "descending" : "ascending";

    /// <summary>What tells a tile from the others on its board: its folder, and whether it is the topic's own.</summary>
    public string Key => KeyOf(FolderPath, IsGeneral);

    public static string KeyOf(string folderPath, bool isGeneral) => (isGeneral ? "general:" : "folder:") + folderPath;

    public override string RenamePath => FolderPath;

    protected override bool RenamesFolder => true;

    public void Update(Tile tile)
    {
        Update(tile.Title, tile.Items);
        Size = tile.Size;
        if (Sort != tile.Sort || ByHand != tile.ByHand)
        {
            Sort = tile.Sort;
            ByHand = tile.ByHand;
            OnPropertyChanged(nameof(Sort));
            OnPropertyChanged(nameof(ByHand));
            OnPropertyChanged(nameof(SortedBy));
            OnPropertyChanged(nameof(IsDescending));
            OnPropertyChanged(nameof(Direction));
        }
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
