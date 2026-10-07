namespace Quiver.App.Interop;

/// <summary>
/// Tells Explorer about changes Quiver makes without going through the shell, so its windows, the desktop and the
/// Recycle Bin show them at once.
/// </summary>
internal static class ShellNotify
{
    /// <summary>An item moved, or was renamed: from the Recycle Bin back to its place, for example.</summary>
    public static void Moved(string from, string to) =>
        NativeMethods.SHChangeNotify(
            Directory.Exists(to) ? NativeMethods.SHCNE_RENAMEFOLDER : NativeMethods.SHCNE_RENAMEITEM,
            NativeMethods.SHCNF_PATHW, from, to);

    /// <summary>The Recycle Bin lost or gained items: its icon shows whether it is empty.</summary>
    public static void RecycleBinChanged() => NativeMethods.SHUpdateRecycleBinIcon();
}
