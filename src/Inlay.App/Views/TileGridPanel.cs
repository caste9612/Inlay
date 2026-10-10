using System.Windows;
using System.Windows.Controls;

namespace Inlay.App.Views;

/// <summary>
/// Lays tiles out in as many equal columns as fit, row by row. A tile can take more than one column; when it does not fit
/// in what is left of a row it starts the next one. Each tile keeps its own height, aligned to the top of its row, so a
/// short tile next to a long one does not stretch.
/// </summary>
public sealed class TileGridPanel : Panel
{
    /// <summary>The columns a tile takes, set on the container of each tile; more than the board has means all of them.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.RegisterAttached(
        "Columns", typeof(int), typeof(TileGridPanel),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static int GetColumns(DependencyObject element) => (int)element.GetValue(ColumnsProperty);

    public static void SetColumns(DependencyObject element, int value) => element.SetValue(ColumnsProperty, value);

    /// <summary>A fixed number of columns, each <see cref="MinColumnWidth"/> wide when nothing limits the width: a zone of the canvas. 0 for as many as fit, as on the board.</summary>
    public static readonly DependencyProperty FixedColumnsProperty = DependencyProperty.Register(
        nameof(FixedColumns), typeof(int), typeof(TileGridPanel), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public int FixedColumns
    {
        get => (int)GetValue(FixedColumnsProperty);
        set => SetValue(FixedColumnsProperty, value);
    }

    public double MinColumnWidth { get; set; } = 280;

    public double Spacing { get; set; } = 16;

    /// <summary>How many columns the board has now, and how wide each one is: what the corner of a tile snaps to.</summary>
    public int ColumnCount { get; private set; } = 1;

    public double ColumnWidth { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = FixedColumns > 0 ? FixedColumns : 3;
        var width = double.IsInfinity(availableSize.Width) ? MinColumnWidth * unbounded + Spacing * (unbounded - 1) : availableSize.Width;
        var columns = ColumnsFor(width);
        var columnWidth = WidthOfColumn(width, columns);
        var height = 0.0;
        foreach (var row in Rows(columns))
        {
            var rowHeight = 0.0;
            foreach (var (child, span, _) in row)
            {
                child.Measure(new Size(WidthOf(span, columnWidth), double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }
            height += rowHeight + Spacing;
        }
        return new Size(width, Math.Max(0, height - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var columns = ColumnCount = ColumnsFor(finalSize.Width);
        var columnWidth = ColumnWidth = WidthOfColumn(finalSize.Width, columns);
        var y = 0.0;
        foreach (var row in Rows(columns))
        {
            var rowHeight = row.Max(cell => cell.Child.DesiredSize.Height);
            foreach (var (child, span, column) in row)
                child.Arrange(new Rect(column * (columnWidth + Spacing), y, WidthOf(span, columnWidth), child.DesiredSize.Height));
            y += rowHeight + Spacing;
        }
        return finalSize;
    }

    /// <summary>The tiles row by row, each with the columns it takes and the one it starts at.</summary>
    private List<List<(UIElement Child, int Span, int Column)>> Rows(int columns)
    {
        var rows = new List<List<(UIElement Child, int Span, int Column)>>();
        List<(UIElement Child, int Span, int Column)>? row = null;
        var used = 0;
        foreach (UIElement child in InternalChildren)
        {
            var span = Math.Clamp(GetColumns(child), 1, columns);
            if (row is null || used + span > columns)
            {
                rows.Add(row = []);
                used = 0;
            }
            row.Add((child, span, used));
            used += span;
        }
        return rows;
    }

    private double WidthOf(int span, double columnWidth) => span * columnWidth + (span - 1) * Spacing;

    private int ColumnsFor(double width) =>
        FixedColumns > 0 ? FixedColumns : Math.Max(1, (int)((width + Spacing) / (MinColumnWidth + Spacing)));

    private double WidthOfColumn(double width, int columns) => Math.Max(0, (width - Spacing * (columns - 1)) / columns);
}
