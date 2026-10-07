using System.Security;
using Microsoft.Win32;

namespace Quiver.App.Services;

/// <summary>Starts Quiver when the user signs in to Windows, through the per-user Run key.</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Quiver";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True only when Windows would start this very Quiver.exe, not a copy elsewhere.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <returns>False when the setting could not be changed.</returns>
    public static bool TrySet(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, Command);
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            Log.Write($"Could not change the start with Windows: {e.Message}");
            return false;
        }
    }
}
