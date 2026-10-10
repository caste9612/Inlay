namespace Inlay.Core;

/// <summary>A rectangle of the canvas, in points of the canvas at 100 %.</summary>
public readonly record struct CanvasRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    /// <summary>Whether the two share some area; touching edges do not count.</summary>
    public bool Overlaps(CanvasRect other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

/// <summary>A zone to place on the canvas: its size, and where the user put it, if they did.</summary>
public readonly record struct ZoneBox(double Width, double Height, double? X = null, double? Y = null)
{
    public bool IsPinned => X is not null && Y is not null;
}

/// <summary>
/// Where the zones of the canvas go. Those the user put somewhere stay there; the others take, in their order, the
/// highest free place within the width of a row, the leftmost among equals, so that none covers another.
/// </summary>
public static class CanvasPlacement
{
    /// <param name="zones">In the order of the sidebar.</param>
    /// <param name="rowWidth">How wide the canvas grows before a zone goes under the others.</param>
    /// <param name="gap">The space kept between zones.</param>
    /// <returns>The place of each zone, in the order given.</returns>
    public static IReadOnlyList<CanvasRect> Place(IReadOnlyList<ZoneBox> zones, double rowWidth, double gap)
    {
        var result = new CanvasRect[zones.Count];
        var taken = new List<CanvasRect>();
        for (var i = 0; i < zones.Count; i++)
        {
            if (zones[i] is { IsPinned: true } pinned)
            {
                result[i] = new CanvasRect(pinned.X!.Value, pinned.Y!.Value, pinned.Width, pinned.Height);
                taken.Add(result[i]);
            }
        }
        for (var i = 0; i < zones.Count; i++)
        {
            if (zones[i].IsPinned)
                continue;
            result[i] = FreePlace(zones[i].Width, zones[i].Height, taken, rowWidth, gap);
            taken.Add(result[i]);
        }
        return result;
    }

    /// <summary>
    /// The width of the rows that, seen whole in a view of this shape, shows the zones biggest: rows too narrow make a
    /// tall column, rows too wide a thin strip. Tries the widths of the first zones side by side and some in between.
    /// </summary>
    /// <param name="aspectRatio">Width compared with height of the view, as 16/9.</param>
    public static double BestRowWidth(IReadOnlyList<ZoneBox> zones, double gap, double aspectRatio)
    {
        if (zones.Count == 0)
            return 0;
        var widest = zones.Max(zone => zone.Width);
        var total = zones.Sum(zone => zone.Width) + gap * (zones.Count - 1);
        var candidates = new SortedSet<double> { widest, total };
        var sideBySide = 0.0;
        foreach (var zone in zones)
        {
            sideBySide += zone.Width;
            candidates.Add(Math.Max(widest, sideBySide));
            sideBySide += gap;
        }
        for (var step = 1; step < 12; step++)
            candidates.Add(widest + (total - widest) * step / 12);

        var best = widest;
        var bestScale = 0.0;
        foreach (var width in candidates)
        {
            var places = Place(zones, width, gap);
            var scale = Math.Min(aspectRatio / places.Max(place => place.Right), 1 / places.Max(place => place.Bottom));
            if (scale > bestScale * (1 + 1e-9))
            {
                bestScale = scale;
                best = width;
            }
        }
        return best;
    }

    /// <summary>
    /// The highest place for a zone this big: at the left edge or just right of a zone already there, and just below
    /// everything above that stretch. Below everything it never covers a zone, so a place is always found.
    /// </summary>
    private static CanvasRect FreePlace(double width, double height, List<CanvasRect> taken, double rowWidth, double gap)
    {
        // Plain loops: the best width of the rows is found by placing every zone many times over.
        var best = new CanvasRect(0, TopAt(0), width, height);
        foreach (var other in taken)
        {
            var x = other.Right + gap;
            // Too far right for the row, unless nothing fits anyway: a zone wider than a row starts at the left edge.
            if (x > 0 && x + width > rowWidth)
                continue;
            var y = TopAt(x);
            if (y < best.Y || (y == best.Y && x < best.X))
                best = new CanvasRect(x, y, width, height);
        }
        return best;

        // Just below everything above the stretch from x as wide as the zone, gaps included.
        double TopAt(double x)
        {
            var top = 0.0;
            foreach (var rect in taken)
            {
                if (rect.X < x + width + gap && x < rect.Right + gap)
                    top = Math.Max(top, rect.Bottom + gap);
            }
            return top;
        }
    }
}
