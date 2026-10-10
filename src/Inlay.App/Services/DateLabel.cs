using System.Globalization;

namespace Inlay.App.Services;

/// <summary>When a file last changed, in a few characters: "today", "yesterday", "5 Oct", "12 Mar 2025".</summary>
internal static class DateLabel
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static string For(DateTime? moment, DateTime? now = null)
    {
        if (moment is not { } when)
            return "";
        var today = (now ?? DateTime.Now).Date;
        if (when.Date == today)
            return "today";
        if (when.Date == today.AddDays(-1))
            return "yesterday";
        return when.ToString(when.Year == today.Year ? "d MMM" : "d MMM yyyy", English);
    }

    /// <summary>The full date and time, for tooltips: "5 Oct 2026, 14:32".</summary>
    public static string Full(DateTime? moment) =>
        moment is { } when ? when.ToString("d MMM yyyy, HH:mm", English) : "";
}
