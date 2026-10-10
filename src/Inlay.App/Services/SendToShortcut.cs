using System.Runtime.InteropServices;
using Inlay.App.Interop;

namespace Inlay.App.Services;

/// <summary>
/// «Inlay» in the «Invia a» menu of Explorer: a shortcut in the SendTo folder of the user, which starts Inlay with the
/// chosen files so that they can be sent into the archive. Made when Inlay is installed, removed with it.
/// </summary>
internal static class SendToShortcut
{
    public const string Arguments = "--sort";

    public static string DefaultFolder => Environment.GetFolderPath(Environment.SpecialFolder.SendTo);

    private static string Program => Environment.ProcessPath ?? "";

    /// <summary>Whether the shortcut is there, whatever it points to.</summary>
    public static bool IsPresent(string? folder = null) => File.Exists(LinkPath(folder ?? DefaultFolder));

    /// <summary>True only when the menu would start this very Inlay.exe, not a copy elsewhere.</summary>
    public static bool IsInstalled(string? folder = null)
    {
        var link = LinkPath(folder ?? DefaultFolder);
        try
        {
            return File.Exists(link) && string.Equals(ShellLinks.TargetOf(link), Program, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <returns>False when the shortcut could not be written.</returns>
    public static bool TryInstall(string? folder = null)
    {
        folder ??= DefaultFolder;
        try
        {
            Directory.CreateDirectory(folder);
            ShellLinks.Create(LinkPath(folder), Program, Arguments, "Manda i file nell'archivio di Inlay");
            return true;
        }
        catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not add Inlay to the «Invia a» menu: {e.Message}");
            return false;
        }
    }

    /// <returns>False when the shortcut is there and could not be removed.</returns>
    public static bool TryRemove(string? folder = null)
    {
        try
        {
            File.Delete(LinkPath(folder ?? DefaultFolder));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not remove Inlay from the «Invia a» menu: {e.Message}");
            return false;
        }
    }

    private static string LinkPath(string folder) => Path.Combine(folder, "Inlay.lnk");
}
