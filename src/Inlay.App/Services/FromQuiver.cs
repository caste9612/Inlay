namespace Inlay.App.Services;

/// <summary>
/// What Inlay takes over from the versions called Quiver, up to 0.3.2: the settings and the recent places of this PC, copied
/// the first time Inlay starts. The old folder stays, untouched. The start with Windows and «Invia a» of Quiver go away
/// when Quiver is uninstalled; the archives take their own metadata over when they are opened
/// (<see cref="Core.ArchiveFolders.TakeOverOldMetadata"/>).
/// </summary>
internal static class FromQuiver
{
    private static readonly string[] Files = ["settings.json", "recenti.json"];

    public static void TakeOverSettings()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        TakeOver(Path.Combine(appData, "Quiver"), Path.GetDirectoryName(AppSettings.FilePath)!);
    }

    /// <summary>The files of Quiver in <paramref name="newFolder"/>, if it does not exist yet: once only, never over Inlay's own.</summary>
    internal static void TakeOver(string oldFolder, string newFolder)
    {
        if (Directory.Exists(newFolder) || !Directory.Exists(oldFolder))
            return;
        try
        {
            Directory.CreateDirectory(newFolder);
            foreach (var name in Files)
            {
                var old = Path.Combine(oldFolder, name);
                if (File.Exists(old))
                    File.Copy(old, Path.Combine(newFolder, name));
            }
            Log.Write($"Took over the settings of Quiver from {oldFolder}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not take over the settings of Quiver: {e.Message}");
        }
    }
}
