using System.Windows;
using System.Windows.Media;

namespace Quiver.App.ViewModels;

/// <summary>How close the canvas is looked at: what it shows changes with it.</summary>
public enum CanvasLevel
{
    /// <summary>The zones with their names, the tiles as blocks.</summary>
    Far,

    /// <summary>The tiles with their titles.</summary>
    Middle,

    /// <summary>The tiles as on the board, to work with.</summary>
    Near,
}

/// <summary>
/// Where the canvas is looked at from: the zoom, and the point of the canvas at the top left corner of the view. A point
/// of the canvas at zoom 1 is a point of the screen, as big as on the board.
/// </summary>
public readonly record struct CanvasCamera(double Zoom, Point Origin)
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 2;

    // Where one level gives way to the next, with some room either side so that a slow zoom does not flicker.
    private const double MiddleFrom = 0.35;
    private const double NearFrom = 0.7;
    private const double Hysteresis = 0.03;

    public static readonly CanvasCamera Default = new(1, new Point(0, 0));

    /// <summary>What the content of the canvas needs to look like this: scaled, then moved.</summary>
    public Matrix Transform => new(Zoom, 0, 0, Zoom, -Origin.X * Zoom, -Origin.Y * Zoom);

    public Point ToScreen(Point canvas) => new((canvas.X - Origin.X) * Zoom, (canvas.Y - Origin.Y) * Zoom);

    public Point ToCanvas(Point screen) => new(screen.X / Zoom + Origin.X, screen.Y / Zoom + Origin.Y);

    /// <summary>The part of the canvas a view of this size shows.</summary>
    public Rect Visible(Size view) => new(Origin, new Size(view.Width / Zoom, view.Height / Zoom));

    /// <summary>Zoomed by <paramref name="factor"/>, keeping the point under <paramref name="screen"/> where it is.</summary>
    public CanvasCamera ZoomAt(Point screen, double factor)
    {
        var under = ToCanvas(screen);
        var zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        return new CanvasCamera(zoom, new Point(under.X - screen.X / zoom, under.Y - screen.Y / zoom));
    }

    /// <summary>Moved as the pointer moves on the screen: the canvas follows it.</summary>
    public CanvasCamera Pan(Vector screenDelta) => this with { Origin = Origin - screenDelta / Zoom };

    /// <summary>
    /// The camera that shows <paramref name="area"/> whole in a view of this size, centered, with a margin around it, and
    /// never closer than <paramref name="maxZoom"/>: a small zone is not blown up.
    /// </summary>
    public static CanvasCamera Fit(Rect area, Size view, double margin = 48, double maxZoom = 1)
    {
        if (area.IsEmpty || view.Width <= 0 || view.Height <= 0)
            return Default;
        var zoom = Math.Min((view.Width - 2 * margin) / Math.Max(1, area.Width), (view.Height - 2 * margin) / Math.Max(1, area.Height));
        zoom = Math.Clamp(zoom, MinZoom, Math.Min(MaxZoom, maxZoom));
        var center = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
        return new CanvasCamera(zoom, new Point(center.X - view.Width / 2 / zoom, center.Y - view.Height / 2 / zoom));
    }

    /// <summary>
    /// The way from this camera to <paramref name="target"/>, at <paramref name="progress"/> from 0 to 1: the zoom changes
    /// evenly in proportion, and the middle of the view moves in a straight line.
    /// </summary>
    public CanvasCamera Toward(CanvasCamera target, double progress, Size view)
    {
        var t = Math.Clamp(progress, 0, 1);
        var zoom = Zoom * Math.Pow(target.Zoom / Zoom, t);
        var from = ToCanvas(new Point(view.Width / 2, view.Height / 2));
        var to = target.ToCanvas(new Point(view.Width / 2, view.Height / 2));
        var center = from + (to - from) * t;
        return new CanvasCamera(zoom, new Point(center.X - view.Width / 2 / zoom, center.Y - view.Height / 2 / zoom));
    }

    /// <summary>The level this zoom shows, coming from <paramref name="current"/>.</summary>
    public CanvasLevel LevelFrom(CanvasLevel current) => current switch
    {
        CanvasLevel.Far => Zoom >= NearFrom + Hysteresis ? CanvasLevel.Near : Zoom >= MiddleFrom + Hysteresis ? CanvasLevel.Middle : CanvasLevel.Far,
        CanvasLevel.Middle => Zoom >= NearFrom + Hysteresis ? CanvasLevel.Near : Zoom < MiddleFrom - Hysteresis ? CanvasLevel.Far : CanvasLevel.Middle,
        _ => Zoom < MiddleFrom - Hysteresis ? CanvasLevel.Far : Zoom < NearFrom - Hysteresis ? CanvasLevel.Middle : CanvasLevel.Near,
    };
}
