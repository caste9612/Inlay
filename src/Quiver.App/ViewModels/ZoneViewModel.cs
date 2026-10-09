using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Quiver.App.ViewModels;

/// <summary>
/// A zone of the canvas: a topic with its tiles and, inside, the zones of the topics inside it.
/// </summary>
public sealed partial class ZoneViewModel : ObservableObject
{
    /// <summary>The columns of tiles a zone has when nobody gave it a width.</summary>
    public const int UsualColumns = 3;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Where the zone is on the canvas, as laid out last; the zones of the root only.</summary>
    [ObservableProperty]
    private Rect? _bounds;

    /// <summary>The color of its topic: on the border and the name of the zone, and from afar a light tint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColor))]
    private Brush? _colorBrush;

    public ZoneViewModel(string key, string title, TopicViewModel topic)
    {
        Key = key;
        _title = title;
        Topic = topic;
    }

    /// <summary>What tells a zone from the others: the key of its topic.</summary>
    public string Key { get; }

    /// <summary>The topic shown, the same as in the sidebar.</summary>
    public TopicViewModel Topic { get; private set; }

    public bool HasColor => ColorBrush is not null;

    /// <summary>Inside the zone of another topic.</summary>
    public bool IsNested { get; init; }

    public ObservableCollection<TileViewModel> Tiles { get; } = [];

    public ObservableCollection<ZoneViewModel> Children { get; } = [];

    /// <summary>The columns of tiles it has: as many as it needs, up to three, so a small zone stays small.</summary>
    public int Columns { get; private set; } = 1;

    /// <summary>The columns it takes inside the zone of its topic: no more than that one has.</summary>
    public int Span(int parentColumns) => Math.Min(Columns, parentColumns);

    public IEnumerable<ZoneViewModel> SelfAndDescendants() => Children.SelectMany(child => child.SelfAndDescendants()).Prepend(this);

    /// <summary>The zone read again: its topic view model (the same key, maybe a new instance) and how big it is.</summary>
    public void Update(string title, TopicViewModel topic)
    {
        Title = title;
        Topic = topic;
        ColorBrush = topic.ColorBrush;
        var tileColumns = Tiles.Sum(tile => tile.Columns);
        var childColumns = Children.Sum(child => child.Columns);
        var columns = Math.Clamp(Math.Max(tileColumns, childColumns), 1, UsualColumns);
        if (columns != Columns)
        {
            Columns = columns;
            OnPropertyChanged(nameof(Columns));
        }
        var items = SelfAndDescendants().SelectMany(zone => zone.Tiles).Sum(tile => tile.Count);
        var tiles = SelfAndDescendants().Sum(zone => zone.Tiles.Count);
        Summary = $"{Count(tiles, "riquadro", "riquadri")} · {Count(items, "elemento", "elementi")}";
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count:N0} {many}";
}
