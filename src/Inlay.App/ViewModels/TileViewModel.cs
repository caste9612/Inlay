using System.Collections.ObjectModel;
using System.Windows.Media;
using Inlay.App.Views;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>A box on the board.</summary>
public sealed partial class TileViewModel : RenameableViewModel
{
    /// <summary>How tall the list of a tile grows when nobody gave it a height: a little under twelve rows, then it scrolls.</summary>
    public const double UsualListHeight = 268;

    private readonly IReadOnlySet<string> _cutPaths;
    private TileSize? _size;

    public TileViewModel(Tile tile, string root, IReadOnlySet<string> cutPaths)
    {
        _cutPaths = cutPaths;
        Title = tile.Title;
        FolderPath = tile.FolderPath;
        IsGeneral = tile.IsGeneral;
        Items = new ObservableCollection<ItemViewModel>(tile.Items.Select(NewItem));
        RelativePath = tile.IsGeneral ? null : ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        // "General" keeps its size and order under its topic's key: it holds the files of the topic's folder.
        MetadataKey = ArchiveLayout.ToRelativeKey(root, tile.FolderPath);
        Color = tile.Color;
        _size = tile.Size;
        Sort = tile.Sort;
        ByHand = tile.ByHand;
    }

    public string Title { get; private set; }

    /// <summary>Where files dropped or pasted on the tile go.</summary>
    public string FolderPath { get; }

    public bool IsGeneral { get; }

    /// <summary>Metadata key of the tile's own folder; null for «General», whose folder is that of its topic.</summary>
    public string? RelativePath { get; }

    /// <summary>Where its size and the order of its items are kept: the key of its folder, that of its topic for «General».</summary>
    public string MetadataKey { get; }

    public ObservableCollection<ItemViewModel> Items { get; }

    /// <summary>The color given to the tile, as "#RRGGBB"; null when it has none.</summary>
    public string? Color { get; private set; }

    /// <summary>The stripe on the title, in the tile's color; null when it has none.</summary>
    public Brush? ColorBrush => TileColors.BrushFor(Color);

    public bool HasColor => ColorBrush is not null;

    public int Count => Items.Count;

    // A card from afar shows only the first names: as many as fit in the place of the tile, never more than these.
    private const int PreviewNames = 12;

    /// <summary>The names of the first items, one per line: the card that stands for the tile on the canvas from afar.</summary>
    public string Preview => string.Join(Environment.NewLine, Items.Take(PreviewNames).Select(item => item.Name));

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Only tiles that are a folder of their own can be moved, renamed, deleted, colored or promoted.</summary>
    public bool IsOwnFolder => RelativePath is not null;

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

    /// <summary>It was given a size of its own, which «Auto size» takes away.</summary>
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

    /// <summary>"ascending" or "descending", for the check in «Sort by»; empty when the items are in order by hand.</summary>
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
        OnPropertyChanged(nameof(Preview));
    }

    // The folders of «General» are those shown as its rows: the others of the topic's folder are tiles or topics.
    private ItemViewModel NewItem(ArchiveItem item) =>
        new(item) { IsCut = _cutPaths.Contains(item.FullPath), CanBecomeTile = IsGeneral && item.IsFolder };

    /// <summary>A file and a folder with the same name are two different things.</summary>
    private static string KeyOf(ArchiveItem item) => (item.IsFolder ? "folder:" : "file:") + item.FullPath;
}
