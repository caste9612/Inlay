using System.Windows;
using System.Windows.Controls;

namespace Quiver.App.Views;

/// <summary>
/// Lays tiles out in as many equal columns as fit, row by row. Each tile keeps its own height,
/// aligned to the top of its row, so a short tile next to a long one does not stretch.
/// </summary>
public sealed class TileGridPanel : Panel
{
    public double MinColumnWidth { get; set; } = 280;

    public double Spacing { get; set; } = 16;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth * 3 + Spacing * 2 : availableSize.Width;
        var columns = ColumnCount(width);
        var columnWidth = ColumnWidth(width, columns);
        var height = 0.0;
        var rowHeight = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            var lastInRow = i % columns == columns - 1 || i == InternalChildren.Count - 1;
            if (lastInRow)
            {
                height += rowHeight + (i == InternalChildren.Count - 1 ? 0 : Spacing);
                rowHeight = 0;
            }
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount(finalSize.Width);
        var columnWidth = ColumnWidth(finalSize.Width, columns);
        var y = 0.0;
        for (var start = 0; start < InternalChildren.Count; start += columns)
        {
            var end = Math.Min(start + columns, InternalChildren.Count);
            var rowHeight = 0.0;
            for (var i = start; i < end; i++)
                rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
            for (var i = start; i < end; i++)
            {
                var x = (i - start) * (columnWidth + Spacing);
                InternalChildren[i].Arrange(new Rect(x, y, columnWidth, InternalChildren[i].DesiredSize.Height));
            }
            y += rowHeight + Spacing;
        }
        return finalSize;
    }

    private int ColumnCount(double width) => Math.Max(1, (int)((width + Spacing) / (MinColumnWidth + Spacing)));

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);
}
