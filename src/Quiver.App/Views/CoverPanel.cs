using System.Windows;
using System.Windows.Controls;

namespace Quiver.App.Views;

/// <summary>
/// The first child decides the size; the others lie over it, as big, and never make it grow. On the canvas the card of a
/// tile seen from afar covers the tile, which keeps its size at every zoom.
/// </summary>
public sealed class CoverPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count == 0)
            return default;
        InternalChildren[0].Measure(availableSize);
        var size = InternalChildren[0].DesiredSize;
        // As wide as there is room, which is what they get: a title cut at the width the first one needs would stay cut.
        var cover = new Size(double.IsInfinity(availableSize.Width) ? size.Width : availableSize.Width, size.Height);
        for (var i = 1; i < InternalChildren.Count; i++)
            InternalChildren[i].Measure(cover);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Arrange(new Rect(finalSize));
        return finalSize;
    }
}
