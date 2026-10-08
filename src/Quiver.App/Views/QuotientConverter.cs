using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Quiver.App.Views;

/// <summary>
/// The first value divided by the second: how wide a zone of the canvas looks on screen, its width over the scale that
/// undoes the zoom, which its name seen from afar must not go beyond.
/// </summary>
public sealed class QuotientConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values is [double dividend, double divisor] && !double.IsNaN(dividend) && divisor > 0
            ? dividend / divisor
            : DependencyProperty.UnsetValue;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
