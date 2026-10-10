using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Inlay.App.Views;

/// <summary>
/// The list of a tile: as tall as its items up to its MaxHeight, or exactly <see cref="Rows"/> rows when the tile was given a
/// height. The rows are measured, not assumed: on a screen scaled to 125% Windows rounds a row of 22 to 28 pixels, that is
/// 22.4, and a height worked out from 22 would leave the last row out.
/// On the canvas a list starts asleep: no rows yet, but as tall as with them, so that its tile keeps its size. The canvas
/// wakes the lists that come into view close up; the rows of every tile of a big archive at once would take seconds.
/// </summary>
public sealed class TileList : ListBox
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(TileList), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The items of the tile: in place of ItemsSource, which stays empty while the list sleeps.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IEnumerable), typeof(TileList),
        new FrameworkPropertyMetadata(null, (list, e) => ((TileList)list).OnSourceChanged((IEnumerable?)e.OldValue, (IEnumerable?)e.NewValue)));

    /// <summary>Set where the lists start asleep, the canvas, and inherited by them.</summary>
    public static readonly DependencyProperty StartsAsleepProperty = DependencyProperty.RegisterAttached(
        "StartsAsleep", typeof(bool), typeof(TileList),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits, (element, _) => (element as TileList)?.InvalidateMeasure()));

    public static bool GetStartsAsleep(DependencyObject element) => (bool)element.GetValue(StartsAsleepProperty);

    public static void SetStartsAsleep(DependencyObject element, bool value) => element.SetValue(StartsAsleepProperty, value);

    private bool _woken;
    private CanvasSurface? _surface;

    public TileList()
    {
        Unloaded += (_, _) =>
        {
            _surface?.Gone(this);
            _surface = null;
        };
    }

    /// <summary>The rows it shows before scrolling; 0 to follow its items.</summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public IEnumerable? Source
    {
        get => (IEnumerable?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>No rows yet: the canvas has not needed them.</summary>
    public bool IsAsleep => GetStartsAsleep(this) && !_woken;

    /// <summary>The height of one row as laid out on this screen, from the last measure: the step of the tile's corner.</summary>
    public double RowPitch { get; private set; } = 22;

    /// <summary>The space of the list around its rows: its padding and border.</summary>
    public double Chrome => Padding.Top + Padding.Bottom + BorderThickness.Top + BorderThickness.Bottom;

    /// <summary>The rows that fit in a list this tall, as the corner of the tile is dragged.</summary>
    public int RowsFor(double height) => (int)Math.Round((height - Chrome) / RowPitch);

    /// <summary>The rows from now on, made at the next layout.</summary>
    public void Wake()
    {
        if (_woken)
            return;
        _woken = true;
        _surface?.Gone(this);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        // The rows are given here, not as soon as the items or the canvas say so: at the first measure both are known,
        // and a list that starts asleep never makes its rows for nothing.
        var shown = IsAsleep ? null : Source;
        if (!ReferenceEquals(ItemsSource, shown))
            ItemsSource = shown;
        // The canvas it is on wakes it when it comes into view. Here and not when loaded: a preview has no window, and
        // nothing in it is ever loaded.
        if (IsAsleep)
        {
            _surface ??= FindSurface();
            _surface?.Sleeping(this);
        }
        var desired = base.MeasureOverride(constraint);
        RowPitch = MeasuredRow();
        if (Rows > 0)
            return new Size(desired.Width, Math.Min(constraint.Height, Rows * RowPitch + Chrome));
        // Asleep, the space around the rows and a pitch for each: what they will take once there, row by row.
        if (IsAsleep)
            return new Size(desired.Width, Math.Min(constraint.Height, desired.Height + CountOf(Source) * RowPitch));
        return desired;
    }

    private void OnSourceChanged(IEnumerable? before, IEnumerable? after)
    {
        // Asleep, the list follows how many items there are; awake, ItemsSource does.
        if (before is INotifyCollectionChanged oldItems)
            CollectionChangedEventManager.RemoveHandler(oldItems, OnItemsChanged);
        if (after is INotifyCollectionChanged newItems)
            CollectionChangedEventManager.AddHandler(newItems, OnItemsChanged);
        InvalidateMeasure();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsAsleep)
            InvalidateMeasure();
    }

    private static int CountOf(IEnumerable? items) => items switch
    {
        null => 0,
        ICollection collection => collection.Count,
        _ => items.Cast<object>().Count(),
    };

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

    private CanvasSurface? FindSurface()
    {
        for (DependencyObject? node = this; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is CanvasSurface surface)
                return surface;
        }
        return null;
    }
}
