using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Inlay.App.Views;

/// <summary>
/// The little movements of Brace: something that appears fades in and settles into place, with the durations and
/// curves of the design system. Nothing moves when Windows has its animations turned off.
/// </summary>
internal static class Motion
{
    /// <param name="slide">How many pixels from below the element arrives.</param>
    /// <param name="scaleFrom">How small it starts, 1 for its own size.</param>
    public static void Enter(FrameworkElement element, double slide = 0, double scaleFrom = 1)
    {
        if (!SystemParameters.ClientAreaAnimation)
            return;
        var duration = element.TryFindResource("Brace.DurationNormal") is Duration known ? known : new Duration(TimeSpan.FromMilliseconds(160));
        var easing = element.TryFindResource("Brace.EaseEnter.Function") as IEasingFunction;
        var scale = new ScaleTransform(scaleFrom, scaleFrom);
        var translate = new TranslateTransform(0, slide);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        if (slide != 0)
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(slide, 0, duration) { EasingFunction = easing });
        if (scaleFrom != 1)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleFrom, 1, duration) { EasingFunction = easing });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleFrom, 1, duration) { EasingFunction = easing });
        }
    }
}
