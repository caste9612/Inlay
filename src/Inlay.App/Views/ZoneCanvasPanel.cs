using System.Windows;
using System.Windows.Controls;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App.Views;

/// <summary>
/// Lays the zones of the root out on the canvas, in the order of the sidebar, each as high as there is room
/// (<see cref="CanvasPlacement"/>), in rows as wide as shows them biggest on a screen: seen whole, the canvas fills the
/// view. Tells each zone where it went.
/// </summary>
public sealed class ZoneCanvasPanel : Panel
{
    /// <summary>The space between zones, in points of the canvas: from afar, the name of a zone sits in it.</summary>
    public double Gap { get; set; } = 96;

    /// <summary>Width compared with height of the view the canvas is meant to fill: that of a screen.</summary>
    public double AspectRatio { get; set; } = 16.0 / 9;

    private IReadOnlyList<CanvasRect> _places = [];
    private List<ZoneBox> _boxes = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var boxes = new List<ZoneBox>();
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            boxes.Add(new ZoneBox(child.DesiredSize.Width, child.DesiredSize.Height));
        }
        // Measured again with the zones as they were, the places stay: no need to look for them once more.
        if (!boxes.SequenceEqual(_boxes))
        {
            _places = CanvasPlacement.Place(boxes, CanvasPlacement.BestRowWidth(boxes, Gap, AspectRatio), Gap);
            _boxes = boxes;
        }
        if (_places.Count == 0)
            return new Size(0, 0);
        return new Size(_places.Max(place => place.Right), _places.Max(place => place.Bottom));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < InternalChildren.Count && i < _places.Count; i++)
        {
            var place = _places[i];
            var bounds = new Rect(place.X, place.Y, place.Width, place.Height);
            InternalChildren[i].Arrange(bounds);
            if (InternalChildren[i] is FrameworkElement { DataContext: ZoneViewModel zone })
                zone.Bounds = bounds;
        }
        return finalSize;
    }
}
