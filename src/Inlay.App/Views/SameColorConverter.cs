using System.Globalization;
using System.Windows.Data;

namespace Inlay.App.Views;

/// <summary>True when the color of a tile is the one of the menu entry; the entry without a parameter stands for no color.</summary>
public sealed class SameColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals((value as string) ?? "", (parameter as string) ?? "", StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
