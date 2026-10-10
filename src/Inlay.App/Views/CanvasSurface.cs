using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Inlay.App.ViewModels;

namespace Inlay.App.Views;

/// <summary>
/// The surface of the canvas: its content seen through a camera that zooms and moves, as a map. The wheel zooms around
/// the pointer; dragging the empty canvas, or with the wheel pressed anywhere, moves it; a double click on the empty
/// canvas shows it whole. Flights from place to place take the time and the curve of Brace.
/// </summary>
public sealed class CanvasSurface : Border
{
    public static readonly DependencyProperty CameraProperty = DependencyProperty.Register(
        nameof(Camera), typeof(CanvasCamera), typeof(CanvasSurface),
        new FrameworkPropertyMetadata(CanvasCamera.Default, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (surface, _) => ((CanvasSurface)surface).ApplyCamera()));

    /// <summary>
    /// How close the canvas is looked at, for all that is on it: what they show changes with it. Inherited, so that each
    /// tile reads it on itself: thousands of bindings looking for the canvas up the tree took seconds to settle.
    /// </summary>
    public static readonly DependencyProperty LevelProperty = DependencyProperty.RegisterAttached(
        "Level", typeof(CanvasLevel), typeof(CanvasSurface),
        new FrameworkPropertyMetadata(CanvasLevel.Near, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>
    /// What undoes the zoom, for the names seen from afar: scaled by it, they stay as big on screen at any zoom. One
    /// transform for all of them, inherited; the camera changes it at every frame, and nothing is laid out again.
    /// </summary>
    public static readonly DependencyProperty LabelScaleProperty = DependencyProperty.RegisterAttached(
        "LabelScale", typeof(ScaleTransform), typeof(CanvasSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    public static CanvasLevel GetLevel(DependencyObject element) => (CanvasLevel)element.GetValue(LevelProperty);

    public static void SetLevel(DependencyObject element, CanvasLevel value) => element.SetValue(LevelProperty, value);

    public static ScaleTransform? GetLabelScale(DependencyObject element) => (ScaleTransform?)element.GetValue(LabelScaleProperty);

    public static void SetLabelScale(DependencyObject element, ScaleTransform? value) => element.SetValue(LabelScaleProperty, value);

    // A notch of the wheel zooms by 15 %: from the whole archive to a tile in a few turns.
    private const double ZoomStep = 1.15;
    private static readonly TimeSpan FlightTime = TimeSpan.FromMilliseconds(240);
    private static readonly KeySpline EaseStandard = new(0, 0, 0.58, 1);
    // The rows of the tiles in view come once the camera has been still this long: made while it moves, they would make
    // it stutter. Then they may take this long before the view is drawn again.
    private static readonly TimeSpan WakeDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan WakeBudget = TimeSpan.FromMilliseconds(12);

    private readonly MatrixTransform _transform = new();
    private readonly ScaleTransform _labelScale = new();
    private readonly Stopwatch _flightClock = new();
    private readonly Stopwatch _sinceMoved = new();
    private readonly DispatcherTimer _wakeTimer;
    private readonly HashSet<TileList> _sleeping = [];
    private Point? _panFrom;
    private CanvasCamera _flightFrom;
    private CanvasCamera _flightTo;
    private bool _flying;

    public CanvasSurface()
    {
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        SetLabelScale(this, _labelScale);
        _wakeTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = WakeDelay };
        _wakeTimer.Tick += (_, _) => WakeWhenStill();
    }

    public CanvasCamera Camera
    {
        get => (CanvasCamera)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>All the canvas, from its top left corner: the size its zones take.</summary>
    public Rect WorldBounds => Child is { } world ? new Rect(new Point(0, 0), world.DesiredSize) : Rect.Empty;

    /// <summary>The whole canvas in view.</summary>
    public void FitAll(bool animated = true) => Show(WorldBounds, animated, maxZoom: 1);

    /// <summary>A part of the canvas in view, centered, not closer than <paramref name="maxZoom"/>.</summary>
    public void Show(Rect area, bool animated = true, double maxZoom = 1)
    {
        if (area.IsEmpty || ActualWidth <= 0 || ActualHeight <= 0)
            return;
        var target = CanvasCamera.Fit(area, RenderSize, maxZoom: maxZoom);
        if (animated)
            FlyTo(target);
        else
            Jump(target);
    }

    /// <summary>Zoomed by a factor around the middle of the view, as the keys + and - do.</summary>
    public void ZoomBy(double factor) => Jump(Camera.ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor));

    /// <summary>Where an element of the canvas is, in points of the canvas.</summary>
    public Rect BoundsOf(FrameworkElement element) =>
        Child is Visual world && world.IsAncestorOf(element)
            ? element.TransformToAncestor(world).TransformBounds(new Rect(element.RenderSize))
            : Rect.Empty;

    /// <summary>To <paramref name="target"/> in a short flight; at once when Windows keeps its animations off.</summary>
    public void FlyTo(CanvasCamera target)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Jump(target);
            return;
        }
        _flightFrom = Camera;
        _flightTo = target;
        _flightClock.Restart();
        if (_flying)
            return;
        _flying = true;
        CompositionTarget.Rendering += OnFlightFrame;
    }

    private void Jump(CanvasCamera target)
    {
        StopFlight();
        Camera = target;
    }

    private void OnFlightFrame(object? sender, EventArgs e)
    {
        var progress = _flightClock.Elapsed.TotalMilliseconds / FlightTime.TotalMilliseconds;
        if (progress >= 1)
        {
            StopFlight();
            Camera = _flightTo;
            return;
        }
        Camera = _flightFrom.Toward(_flightTo, EaseStandard.GetSplineProgress(progress), RenderSize);
    }

    private void StopFlight()
    {
        if (!_flying)
            return;
        _flying = false;
        CompositionTarget.Rendering -= OnFlightFrame;
    }

    private void ApplyCamera()
    {
        _transform.Matrix = Camera.Transform;
        _labelScale.ScaleX = _labelScale.ScaleY = 1 / Camera.Zoom;
        if (Child is UIElement world && !ReferenceEquals(world.RenderTransform, _transform))
            world.RenderTransform = _transform;
        _sinceMoved.Restart();
        QueueWake();
    }

    /// <summary>A list of a tile on the canvas, still without its rows: they come when it is in view close up.</summary>
    internal void Sleeping(TileList list)
    {
        _sleeping.Add(list);
        QueueWake();
    }

    internal void Gone(TileList list) => _sleeping.Remove(list);

    /// <summary>The lists still without their rows: for the measure of the canvas.</summary>
    internal int SleepingCount => _sleeping.Count;

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == LevelProperty)
            QueueWake();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        QueueWake();
    }

    /// <summary>When the camera is still: a flight, a drag or the wheel go on smoothly, and the rows come in their pauses.</summary>
    private void QueueWake()
    {
        if (_sleeping.Count > 0 && GetLevel(this) == CanvasLevel.Near && !_wakeTimer.IsEnabled)
            _wakeTimer.Start();
    }

    private void WakeWhenStill()
    {
        if (_sinceMoved.IsRunning && _sinceMoved.Elapsed < WakeDelay)
            return;
        _wakeTimer.Stop();
        WakeInView(WakeBudget);
    }

    /// <summary>The rows of all the tiles in view, at once: for the previews, which have no time to wait.</summary>
    internal void WakeInViewNow() => WakeInView(TimeSpan.MaxValue);

    /// <summary>
    /// The lists in view close up, and those just around it, get their rows: the nearest to the middle first, as many as
    /// fit in a short time; then the view is drawn, and the others follow.
    /// </summary>
    private void WakeInView(TimeSpan budget)
    {
        if (GetLevel(this) != CanvasLevel.Near || Child is not Visual world || ActualWidth <= 0)
            return;
        var view = Camera.Visible(RenderSize);
        var middle = new Point(view.X + view.Width / 2, view.Y + view.Height / 2);
        view.Inflate(view.Width / 4, view.Height / 4);
        var due = new List<(TileList List, double Distance)>();
        // Those of tiles that went away: back on the canvas, they say so again at their next layout.
        _sleeping.RemoveWhere(list => !world.IsAncestorOf(list));
        foreach (var list in _sleeping)
        {
            var bounds = list.TransformToAncestor(world).TransformBounds(new Rect(list.RenderSize));
            if (bounds.IntersectsWith(view))
                due.Add((list, (new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2) - middle).LengthSquared));
        }
        var clock = Stopwatch.StartNew();
        foreach (var (list, _) in due.OrderBy(entry => entry.Distance))
        {
            list.Wake();
            // Asleep it was already as tall as its rows: only they are laid out, nothing around them moves.
            list.UpdateLayout();
            if (clock.Elapsed >= budget)
            {
                // The others after the view is drawn, unless the camera moves meanwhile.
                Dispatcher.BeginInvoke(WakeWhenStill, DispatcherPriority.Background);
                return;
            }
        }
    }

    // The content is as big as it wants: the camera decides what of it is seen.
    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(double.IsInfinity(constraint.Width) ? 0 : constraint.Width, double.IsInfinity(constraint.Height) ? 0 : constraint.Height);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } world)
        {
            world.Arrange(new Rect(world.DesiredSize));
            ApplyCamera();
        }
        return arrangeSize;
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        // Close up, a tile that scrolls gets the wheel, as on the board; with Ctrl the wheel always zooms.
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && ListScrolls(e.OriginalSource as DependencyObject, e.Delta))
            return;
        e.Handled = true;
        StopFlight();
        Camera = Camera.ZoomAt(e.GetPosition(this), Math.Pow(ZoomStep, e.Delta / 120.0));
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseDown(e);
        // The wheel pressed moves the canvas from anywhere, over a tile too.
        if (e.ChangedButton == MouseButton.Middle && StartPan(e.GetPosition(this)))
            e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled || !IsEmptySpot(e.OriginalSource as DependencyObject))
            return;
        Focus();
        if (e.ClickCount == 2)
        {
            FitAll();
            e.Handled = true;
            return;
        }
        // With Maiusc the drag draws a rectangle that selects: the canvas stays still.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;
        if (StartPan(e.GetPosition(this)))
            e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_panFrom is not { } from)
            return;
        var at = e.GetPosition(this);
        Camera = Camera.Pan(at - from);
        _panFrom = at;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_panFrom is not null && e.ChangedButton is MouseButton.Left or MouseButton.Middle)
            EndPan();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _panFrom = null;
        ClearValue(CursorProperty);
    }

    private bool StartPan(Point at)
    {
        if (!CaptureMouse())
            return false;
        StopFlight();
        _panFrom = at;
        Cursor = Cursors.SizeAll;
        return true;
    }

    private void EndPan()
    {
        _panFrom = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        ClearValue(CursorProperty);
    }

    /// <summary>The canvas or a zone where nothing takes the click: not a tile, nor a control.</summary>
    private bool IsEmptySpot(DependencyObject? source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = ParentOf(node))
        {
            if (node is GroupBox or ButtonBase or TextBoxBase or ScrollBar or Thumb or ListBoxItem)
                return false;
        }
        return true;
    }

    /// <summary>Over the list of a tile in view that can still scroll that way.</summary>
    private bool ListScrolls(DependencyObject? source, int delta)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, this); node = ParentOf(node))
        {
            if (node is not TileList { IsVisible: true } list)
                continue;
            if (FindScrollViewer(list) is not { } viewer)
                return false;
            return delta > 0 ? viewer.VerticalOffset > 0 : viewer.VerticalOffset < viewer.ScrollableHeight;
        }
        return false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ScrollViewer viewer)
                return viewer;
            if (FindScrollViewer(child) is { } deeper)
                return deeper;
        }
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
}
