using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inlay.App.ViewModels;
using Inlay.App.Views;

namespace Inlay.App.Tests;

public sealed class TileListTests
{
    // At the scale of this PC: a row of 22 is 22 points at 100 %, 22.4 at 125 % (28 pixels). A scale set by hand on a test
    // tree does not reach the rows made later, which keep that of the PC; in a window list and rows share the screen.
    [Fact]
    public void AsleepOrAwake_ATileIsAsTall_SoWakingItMovesNothing() => StaThread.Run(() =>
    {
        // Items, the most rows before scrolling, and the rows given with the corner of the tile.
        (int Items, double MaxHeight, int Rows)[] cases =
        [
            (0, double.PositiveInfinity, 0),
            (1, double.PositiveInfinity, 0),
            (7, double.PositiveInfinity, 0),
            (25, 232, 0),
            (25, double.PositiveInfinity, 4),
            (3, double.PositiveInfinity, 6),
        ];
        foreach (var (items, maxHeight, rows) in cases)
        {
            var asleep = Laid(NewList(items, maxHeight, rows), wake: false);
            var awake = Laid(NewList(items, maxHeight, rows), wake: true);

            Assert.True(asleep.IsAsleep);
            Assert.Empty(asleep.Items);
            Assert.False(awake.IsAsleep);
            Assert.Equal(items, awake.Items.Count);
            Assert.Equal(awake.RowPitch, asleep.RowPitch, 6);
            Assert.Equal(awake.DesiredSize.Height, asleep.DesiredSize.Height, 6);
        }
    });

    [Fact]
    public void Asleep_AListFollowsHowManyItemsItHas() => StaThread.Run(() =>
    {
        var list = Laid(NewList(2, double.PositiveInfinity, 0), wake: false);
        var before = list.DesiredSize.Height;

        ((ObservableCollection<string>)list.Source!).Add("Terzo.txt");
        list.UpdateLayout();

        Assert.Equal(before + list.RowPitch, list.DesiredSize.Height, 6);
        Assert.Empty(list.Items);
    });

    [Fact]
    public void OnTheBoard_AListHasItsRowsAtOnce() => StaThread.Run(() =>
    {
        var list = NewList(3, double.PositiveInfinity, 0);
        list.Measure(new Size(280, double.PositiveInfinity));

        Assert.False(list.IsAsleep);
        Assert.Equal(3, list.Items.Count);
    });

    [Fact]
    public void CloseUp_TheCanvasWakesTheListsInView_FromAfarNone() => StaThread.Run(() =>
    {
        var inView = NewList(5, double.PositiveInfinity, 0);
        var farAway = NewList(5, double.PositiveInfinity, 0);
        var world = new Canvas { Width = 6000, Height = 6000 };
        Canvas.SetLeft(inView, 100);
        Canvas.SetTop(inView, 100);
        Canvas.SetLeft(farAway, 5000);
        Canvas.SetTop(farAway, 5000);
        world.Children.Add(inView);
        world.Children.Add(farAway);
        var surface = new CanvasSurface { Child = world, Camera = new CanvasCamera(1, new Point(0, 0)) };
        TileList.SetStartsAsleep(surface, true);
        CanvasSurface.SetLevel(surface, CanvasLevel.Middle);
        surface.Measure(new Size(800, 600));
        surface.Arrange(new Rect(0, 0, 800, 600));
        Settle();

        Assert.True(inView.IsAsleep);
        Assert.True(farAway.IsAsleep);

        CanvasSurface.SetLevel(surface, CanvasLevel.Near);
        Settle();

        Assert.False(inView.IsAsleep);
        Assert.Equal(5, inView.Items.Count);
        Assert.True(farAway.IsAsleep);

        // Moved there, the other one wakes too.
        surface.Camera = new CanvasCamera(1, new Point(4800, 4800));
        Settle();

        Assert.False(farAway.IsAsleep);
    });

    /// <summary>A list as in a tile: rows of 22, as the style of the rows of Inlay makes them.</summary>
    private static TileList NewList(int items, double maxHeight, int rows)
    {
        var rowStyle = new Style(typeof(ListBoxItem));
        rowStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 22.0));
        return new TileList
        {
            Width = 280,
            MaxHeight = maxHeight,
            Rows = rows,
            ItemContainerStyle = rowStyle,
            Source = new ObservableCollection<string>(Enumerable.Range(1, items).Select(i => $"Documento {i}.txt")),
        };
    }

    /// <summary>Laid out inside something whose lists start asleep, as the canvas, rounded to the pixels as Inlay is.</summary>
    private static TileList Laid(TileList list, bool wake)
    {
        var host = new Border { Child = list, UseLayoutRounding = true };
        TileList.SetStartsAsleep(host, true);
        if (wake)
            list.Wake();
        host.Measure(new Size(400, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        // Woken, the rows come at the next layout; measured again they hold the pitch they really have.
        host.UpdateLayout();
        return list;
    }

    /// <summary>Lets the dispatcher run for a while, as that of the window would: the rows come once the camera is still.</summary>
    private static void Settle()
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
