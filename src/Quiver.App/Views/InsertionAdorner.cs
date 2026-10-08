using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Quiver.App.Views;

/// <summary>
/// The line that shows where a tile dragged by its title will land: before the tile under the pointer, or after the
/// last one. Drawn over the control of the tiles, so it scrolls with them.
/// </summary>
internal sealed class InsertionAdorner(UIElement tiles, Brush brush) : Adorner(tiles)
{
    private Rect _line = Rect.Empty;

    /// <summary>At the left edge of <paramref name="before"/>; without one, at the right edge of <paramref name="last"/>.</summary>
    /// <param name="spacing">The gap between tiles, which the line sits in the middle of.</param>
    public void ShowBefore(FrameworkElement? before, FrameworkElement? last, double spacing)
    {
        Rect line;
        if (before is not null)
        {
            var origin = before.TranslatePoint(new Point(0, 0), AdornedElement);
            line = new Rect(origin.X - spacing / 2 - 1, origin.Y, 2, before.ActualHeight);
        }
        else if (last is not null)
        {
            var origin = last.TranslatePoint(new Point(last.ActualWidth, 0), AdornedElement);
            line = new Rect(origin.X + spacing / 2 - 1, origin.Y, 2, last.ActualHeight);
        }
        else
        {
            line = Rect.Empty;
        }
        if (line == _line)
            return;
        _line = line;
        InvalidateVisual();
    }

    public void Hide()
    {
        if (_line.IsEmpty)
            return;
        _line = Rect.Empty;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_line.IsEmpty)
            drawingContext.DrawRoundedRectangle(brush, null, _line, 1, 1);
    }
}
