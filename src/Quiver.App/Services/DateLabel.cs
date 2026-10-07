using System.Globalization;

namespace Quiver.App.Services;

/// <summary>When a file last changed, in a few characters: "oggi", "ieri", "5 ott", "12 mar 2025".</summary>
internal static class DateLabel
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    public static string For(DateTime? moment, DateTime? now = null)
    {
        if (moment is not { } when)
            return "";
        var today = (now ?? DateTime.Now).Date;
        if (when.Date == today)
            return "oggi";
        if (when.Date == today.AddDays(-1))
            return "ieri";
        return when.ToString(when.Year == today.Year ? "d MMM" : "d MMM yyyy", Italian);
    }

    /// <summary>The full date and time, for tooltips: "5 ott 2026, 14:32".</summary>
    public static string Full(DateTime? moment) =>
        moment is { } when ? when.ToString("d MMM yyyy, HH:mm", Italian) : "";
}
