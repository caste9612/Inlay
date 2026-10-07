using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Quiver.App.Views;

/// <summary>Shows an element only when the bound value is not null.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
