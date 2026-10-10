using System.Windows.Media;

namespace Inlay.App.Views;

/// <summary>One color of the palette, with the name the menu shows.</summary>
public sealed record TileColor(string Name, string Hex);

/// <summary>
/// The colors a tile can have: the eight of Brace that read well on its dark surfaces, the charts' five and the
/// three of success, warning and danger, named after the stones of an inlay. A color is kept in the metadata as
/// "#RRGGBB", so an archive opened elsewhere shows the same.
/// </summary>
internal static class TileColors
{
    public static readonly IReadOnlyList<TileColor> All =
    [
        new("Amber", "#FFA133"),
        new("Lapis", "#5DA9FF"),
        new("Amethyst", "#B48CFF"),
        new("Rose", "#FF7EB6"),
        new("Jade", "#36CFB0"),
        new("Citrine", "#F2D24B"),
        new("Coral", "#FF7272"),
        new("Pearl", "#C3CAD3"),
    ];

    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A frozen brush for a color written as "#RRGGBB"; null for none, or for something that is not a color.</summary>
    public static Brush? BrushFor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        if (Brushes.TryGetValue(hex, out var brush))
            return brush;
        try
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            // Written by hand in archive.json, and not a color: shown as none.
            return null;
        }
        brush.Freeze();
        Brushes[hex] = brush;
        return brush;
    }
}
