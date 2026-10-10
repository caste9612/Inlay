using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Inlay.App.Views;

/// <summary>
/// Visible when the value, written out, is the parameter: the check mark of the sort in use in «Ordina per». The menus
/// of Brace do not draw IsChecked, so the mark is the icon of the entry.
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
