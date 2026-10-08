using System.Globalization;

namespace Quiver.Core;

/// <summary>Sizes as Explorer writes them, in Italian: "512 byte", "12,4 MB", "120 GB". Units of 1024, three figures.</summary>
public static class ByteSize
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static readonly string[] Units = ["KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 1024)
            return string.Create(Italian, $"{Math.Max(0, bytes)} byte");
        var value = bytes / 1024.0;
        var unit = 0;
        // Never four figures: 1.016 KB is written 0,99 MB.
        while (value >= 999.5 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = value >= 99.95 ? "N0" : value >= 9.995 ? "N1" : "N2";
        return value.ToString(format, Italian) + " " + Units[unit];
    }
}
