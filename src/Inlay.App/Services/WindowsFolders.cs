using System.Security.Principal;
using Inlay.Core;

namespace Inlay.App.Services;

/// <summary>The Windows folders Inlay has to know about besides the archive: those an archive cannot be, and the Recycle Bin.</summary>
internal static class WindowsFolders
{
    private static readonly string? UserSid = WindowsIdentity.GetCurrent().User?.Value;

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
                "Il Desktop non può fare da archivio: Inlay ne prende il posto. Crea l'archivio in un'altra cartella, per esempio nella cartella del tuo utente."),
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
}
