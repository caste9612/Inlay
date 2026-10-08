using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Quiver.App.Views;

/// <summary>
/// The list of a tile: as tall as its items up to its MaxHeight, or exactly <see cref="Rows"/> rows when the tile was given a
/// height. The rows are measured, not assumed: on a screen scaled to 125% Windows rounds a row of 22 to 28 pixels, that is
/// 22.4, and a height worked out from 22 would leave the last row out.
/// </summary>
public sealed class TileList : ListBox
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(TileList), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The rows it shows before scrolling; 0 to follow its items.</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>The height of one row as laid out on this screen, from the last measure: the step of the tile's corner.</summary>
    public double RowPitch { get; private set; } = 22;

    /// <summary>The space of the list around its rows: its padding and border.</summary>
    public double Chrome => Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;

    /// <summary>The rows that fit in a list this tall, as the corner of the tile is dragged.</summary>
    public int RowsFor(double height) => (int)Math.Round((height - Chrome) / RowPitch);

    protected override Size MeasureOverride(Size constraint)
    {
        var desired = base.MeasureOverride(constraint);
        RowPitch = MeasuredRow();
        if (Rows <= 0)
            return desired;
        return new Size(desired.Width, Math.Min(constraint.Height, Rows * RowPitch + Chrome));
    }

    /// <summary>The first row as measured; without rows, the height a row would take, rounded as the screen rounds it.</summary>
    private double MeasuredRow()
    {
        // DesiredSize already holds the margins of the row.
        if (ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement { DesiredSize.Height: > 0 } first)
            return first.DesiredSize.Height;
        var nominal = ItemContainerStyle?.Setters.OfType<Setter>().FirstOrDefault(setter => setter.Property == HeightProperty)?.Value as double? ?? 22;
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        return UseLayoutRounding && scale > 0 ? Math.Round(nominal * scale) / scale : nominal;
    }
}
