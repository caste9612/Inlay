using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Inlay.App.Views;

/// <summary>
/// The line that shows where something dragged will land: a tile before another one or after the last, a row of a tile
/// above another one or under the last, a topic above or under another one. Drawn over the control it belongs to, so it
/// scrolls with it.
/// </summary>
internal sealed class InsertionAdorner(UIElement host, Brush brush) : Adorner(host)
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
        Show(line);
    }

    /// <summary>Along the top edge of <paramref name="row"/>, or along its bottom edge: where a row or a topic will land.</summary>
    public void ShowAt(FrameworkElement row, bool atBottom)
    {
        var origin = row.TranslatePoint(new Point(0, atBottom ? row.ActualHeight : 0), AdornedElement);
        Show(new Rect(origin.X, origin.Y - 1, row.ActualWidth, 2));
    }

    public void Hide() => Show(Rect.Empty);

    private void Show(Rect line)
    {
        if (line == _line)
            return;
        _line = line;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_line.IsEmpty)
            drawingContext.DrawRoundedRectangle(brush, null, _line, 1, 1);
    }
}
