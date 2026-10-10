using System.Windows;
using Inlay.App.ViewModels;

namespace Inlay.App.Tests;

/// <summary>The view of the canvas: zoom around the pointer, the canvas following the pointer, framing, levels.</summary>
public sealed class CanvasCameraTests
{
    private static readonly Size View = new(1600, 900);

    [Fact]
    public void ZoomingAtAPoint_KeepsWhatIsUnderThePointer()
    {
        var camera = new CanvasCamera(0.5, new Point(300, 200));
        var pointer = new Point(640, 410);
        var under = camera.ToCanvas(pointer);

        var zoomed = camera.ZoomAt(pointer, 1.15);

        Assert.Equal(0.575, zoomed.Zoom, precision: 10);
        Assert.Equal(under.X, zoomed.ToCanvas(pointer).X, precision: 8);
        Assert.Equal(under.Y, zoomed.ToCanvas(pointer).Y, precision: 8);
        Assert.Equal(pointer, zoomed.ToScreen(under));
    }

    [Fact]
    public void TheZoom_StaysBetweenTenAndTwoHundredPercent()
    {
        var camera = CanvasCamera.Default;

        Assert.Equal(CanvasCamera.MaxZoom, camera.ZoomAt(new Point(10, 10), 50).Zoom);
        Assert.Equal(CanvasCamera.MinZoom, camera.ZoomAt(new Point(10, 10), 0.001).Zoom);
    }

    [Fact]
    public void Panning_MovesTheCanvasWithThePointer()
    {
        var camera = new CanvasCamera(0.5, new Point(100, 100));
        var spot = new Point(500, 500);
        var before = camera.ToScreen(spot);

        var panned = camera.Pan(new Vector(40, -20));

        Assert.Equal(before + new Vector(40, -20), panned.ToScreen(spot));
    }

    [Fact]
    public void Fitting_ShowsTheAreaWholeAndCentered_WithoutBlowingUpASmallOne()
    {
        var all = new Rect(0, 0, 4000, 1500);
        var fit = CanvasCamera.Fit(all, View);
        var shown = fit.Visible(View);
        Assert.True(shown.Contains(all), "the area is not whole");
        Assert.Equal(all.X + all.Width / 2, shown.X + shown.Width / 2, precision: 8);
        Assert.Equal(all.Y + all.Height / 2, shown.Y + shown.Height / 2, precision: 8);

        var small = CanvasCamera.Fit(new Rect(100, 100, 300, 200), View);
        Assert.Equal(1, small.Zoom);
    }

    [Fact]
    public void AFlight_StartsHere_EndsThere_AndChangesTheZoomEvenly()
    {
        var from = new CanvasCamera(0.2, new Point(0, 0));
        var to = CanvasCamera.Fit(new Rect(3000, 1000, 800, 400), View);

        Assert.Equal(from, from.Toward(to, 0, View));
        var end = from.Toward(to, 1, View);
        Assert.Equal(to.Zoom, end.Zoom, precision: 10);
        Assert.Equal(to.Origin.X, end.Origin.X, precision: 6);
        Assert.Equal(to.Origin.Y, end.Origin.Y, precision: 6);
        Assert.Equal(Math.Sqrt(from.Zoom * to.Zoom), from.Toward(to, 0.5, View).Zoom, precision: 10);
    }

    [Fact]
    public void TheLevel_FollowsTheZoom_WithSomeRoomAtTheEdges()
    {
        Assert.Equal(CanvasLevel.Far, At(0.2).LevelFrom(CanvasLevel.Near));
        Assert.Equal(CanvasLevel.Middle, At(0.5).LevelFrom(CanvasLevel.Far));
        Assert.Equal(CanvasLevel.Near, At(1).LevelFrom(CanvasLevel.Far));
        // Just past the edge, the level stays: a slow zoom does not flicker.
        Assert.Equal(CanvasLevel.Near, At(0.69).LevelFrom(CanvasLevel.Near));
        Assert.Equal(CanvasLevel.Middle, At(0.71).LevelFrom(CanvasLevel.Middle));
        Assert.Equal(CanvasLevel.Middle, At(0.34).LevelFrom(CanvasLevel.Middle));
        Assert.Equal(CanvasLevel.Far, At(0.36).LevelFrom(CanvasLevel.Far));
    }

    private static CanvasCamera At(double zoom) => new(zoom, new Point(0, 0));
}
