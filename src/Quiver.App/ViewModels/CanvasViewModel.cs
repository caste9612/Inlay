using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Quiver.App.ViewModels;

/// <summary>
/// The canvas: the whole archive at once, a zone for each topic of the root and one for «Da smistare», looked at from
/// a camera that zooms and moves. What it shows depends on how close it is looked at.
/// </summary>
public sealed partial class CanvasViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ZoomText))]
    private CanvasCamera _camera = CanvasCamera.Default;

    /// <summary>How close it is looked at: the canvas gives it to all that is on it (CanvasSurface.Level).</summary>
    [ObservableProperty]
    private CanvasLevel _level = CanvasLevel.Near;

    /// <summary>«Da smistare» first, then the topics of the root in the order of the sidebar.</summary>
    public ObservableCollection<ZoneViewModel> Zones { get; } = [];

    /// <summary>Like "45 %".</summary>
    public string ZoomText => $"{Camera.Zoom * 100:N0} %";

    public IEnumerable<ZoneViewModel> AllZones() => Zones.SelectMany(zone => zone.SelfAndDescendants());

    public IEnumerable<TileViewModel> AllTiles() => AllZones().SelectMany(zone => zone.Tiles);

    public ZoneViewModel? ZoneOf(TopicViewModel topic) =>
        AllZones().FirstOrDefault(zone => string.Equals(zone.Key, topic.Topic.RelativePath, StringComparison.OrdinalIgnoreCase));

    public ZoneViewModel? Inbox => Zones.FirstOrDefault(zone => zone.IsInbox);

    public ZoneViewModel? ZoneWith(TileViewModel tile) => AllZones().FirstOrDefault(zone => zone.Tiles.Contains(tile));

    public TileViewModel? TileOf(string folder) =>
        AllTiles().FirstOrDefault(tile => string.Equals(tile.FolderPath, folder, StringComparison.OrdinalIgnoreCase));

    partial void OnCameraChanged(CanvasCamera value) => Level = value.LevelFrom(Level);
}
