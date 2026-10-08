using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Quiver.App.Views;

/// <summary>
/// The label that follows the pointer while something is dragged inside the panel: what it is, or how many. Windows
/// keeps choosing the cursor; the label only says what is in hand.
/// </summary>
internal sealed class DragLabelAdorner : Adorner
{
    private static readonly Size Padding = new(10, 5);
    private static readonly Vector Offset = new(18, 20);

    private readonly FormattedText _text;
    private readonly Brush _background;
    private readonly Pen _border;
    private Point _position;

    public DragLabelAdorner(FrameworkElement panel, string text) : base(panel)
    {
        IsHitTestVisible = false;
        var font = panel.TryFindResource("Brace.FontSans") as FontFamily ?? new FontFamily("Segoe UI");
        var foreground = panel.TryFindResource("Brace.TextPrimary") as Brush ?? Brushes.White;
        _background = panel.TryFindResource("Brace.SurfaceOverlay") as Brush ?? Brushes.Black;
        _border = new Pen(panel.TryFindResource("Brace.BorderDefault") as Brush ?? Brushes.Gray, 1);
        _text = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(font, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal), 12, foreground,
            VisualTreeHelper.GetDpi(panel).PixelsPerDip)
        {
            MaxTextWidth = 320,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }

    /// <summary>Where the pointer is, in the panel's coordinates.</summary>
    public void MoveTo(Point position)
    {
        if (position == _position)
            return;
        _position = position;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = new Size(_text.WidthIncludingTrailingWhitespace + Padding.Width * 2, _text.Height + Padding.Height * 2);
        var rect = new Rect(_position + Offset, size);
        drawingContext.DrawRoundedRectangle(_background, _border, rect, 4, 4);
        drawingContext.DrawText(_text, new Point(rect.X + Padding.Width, rect.Y + Padding.Height));
    }
}
