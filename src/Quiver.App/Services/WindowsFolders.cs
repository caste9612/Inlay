using System.Runtime.InteropServices;
using System.Security.Principal;
using Quiver.Core;

namespace Quiver.App.Services;

/// <summary>The Windows folders Quiver works with besides the archive: Desktop, Downloads, Recycle Bin.</summary>
internal static partial class WindowsFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly string? UserSid = WindowsIdentity.GetCurrent().User?.Value;

    /// <summary>
    /// The places outside the archive shown in "Da smistare", as chosen in the settings. The Desktop includes the one
    /// shared by all users, where installers often put their shortcuts.
    /// </summary>
    public static IReadOnlyList<InboxSource> InboxSources(AppSettings settings)
    {
        var sources = new List<InboxSource>();
        if (settings.InboxDesktop)
        {
            sources.Add(new InboxSource("Desktop", Existing(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory))));
        }
        if (settings.InboxDownloads)
            sources.Add(new InboxSource("Download", Existing(KnownFolder(DownloadsId))));
        return sources.Where(source => source.Folders.Count > 0).ToList();
    }

    /// <summary>The folders of Windows that cannot be an archive, nor contain one, with the reason.</summary>
    public static IReadOnlyList<ReservedFolder> ReservedForArchives()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
        const string Programs = "Questa è una cartella dei programmi: scegline un'altra, per esempio nella cartella del tuo utente.";
        return new List<ReservedFolder>
        {
            new(profile, $"Questa è la cartella del tuo utente: crea l'archivio dentro, per esempio «{Path.Combine(profile, "Archivio")}».",
                InsideAllowed: true),
            new(Folder(Environment.SpecialFolder.DesktopDirectory),
                "Il Desktop resta tra le cose da smistare: crea l'archivio in un'altra cartella, per esempio nella cartella del tuo utente."),
            new(Folder(Environment.SpecialFolder.Windows), "Questa è una cartella di Windows: scegline un'altra."),
            new(Folder(Environment.SpecialFolder.ProgramFiles), Programs),
            new(Folder(Environment.SpecialFolder.ProgramFilesX86), Programs),
            new(Folder(Environment.SpecialFolder.CommonApplicationData), Programs),
            new(Folder(Environment.SpecialFolder.LocalApplicationData), Programs),
            new(Folder(Environment.SpecialFolder.ApplicationData), Programs),
        }.Where(place => !string.IsNullOrEmpty(place.Path)).ToList();
    }

    /// <summary>The current user's Recycle Bin folder on the drive of a path, like C:\$Recycle.Bin\&lt;SID&gt;.</summary>
    public static string? RecycleBinOf(string path) =>
        UserSid is not null && Path.GetPathRoot(path) is { Length: > 0 } drive ? Path.Combine(drive, "$Recycle.Bin", UserSid) : null;

    private static List<string> Existing(params string?[] folders) =>
        folders.Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder)).Select(folder => folder!).ToList();

    private static string? KnownFolder(Guid id)
    {
        var result = SHGetKnownFolderPath(id, 0, 0, out var path);
        try
        {
            return result == 0 ? Marshal.PtrToStringUni(path) : null;
        }
        finally
        {
            // Freed whether the call succeeded or not, as its documentation asks.
            Marshal.FreeCoTaskMem(path);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid rfid, uint dwFlags, nint hToken, out nint ppszPath);
}
