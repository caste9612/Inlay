using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Inlay.App.Views;

/// <summary>The rectangle drawn while selecting by dragging on the empty space of the board, as on the desktop.</summary>
internal sealed class SelectionBandAdorner : Adorner
{
    private readonly Brush _fill;
    private readonly Pen _stroke;
    private Rect _rect = Rect.Empty;

    public SelectionBandAdorner(UIElement board, Brush fill, Brush stroke) : base(board)
    {
        IsHitTestVisible = false;
        _fill = fill;
        _stroke = new Pen(stroke, 1);
    }

    public void Show(Rect rect)
    {
        _rect = rect;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_rect.IsEmpty)
            drawingContext.DrawRectangle(_fill, _stroke, _rect);
    }
}
