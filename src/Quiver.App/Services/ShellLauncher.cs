using System.Diagnostics;

namespace Quiver.App.Services;

internal static class ShellLauncher
{
    /// <summary>Opens a file, folder or shortcut like a double-click in Explorer does.</summary>
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>Opens the containing folder in Explorer, with the item selected.</summary>
    public static void Reveal(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
}
