using System.Windows;
using System.Windows.Media;
using Inlay.Core;

namespace Inlay.App.Views;

/// <summary>The icons of Brace for the common kinds of file, as its dictionary "un concetto, un'icona" assigns them.</summary>
internal static class FileGlyphs
{
    private static readonly Dictionary<FileKind, string> Keys = new()
    {
        [FileKind.Folder] = "Brace.Icon.Folder16",
        [FileKind.Text] = "Brace.Icon.DocumentText16",
        [FileKind.Word] = "Brace.Icon.DocumentWord16",
        [FileKind.Pdf] = "Brace.Icon.DocumentPdf16",
        [FileKind.Spreadsheet] = "Brace.Icon.DocumentTable16",
        [FileKind.Presentation] = "Brace.Icon.SlideText16",
        [FileKind.Image] = "Brace.Icon.Image16",
        [FileKind.Video] = "Brace.Icon.Video16",
        [FileKind.Audio] = "Brace.Icon.MusicNote216",
        [FileKind.Archive] = "Brace.Icon.FolderZip16",
        [FileKind.Code] = "Brace.Icon.Code16",
        [FileKind.Mail] = "Brace.Icon.Mail16",
        [FileKind.Web] = "Brace.Icon.Globe16",
        [FileKind.Model3D] = "Brace.Icon.Cube16",
    };

    /// <summary>Null for the kinds that keep the icon of Windows, and where the app's resources are not loaded (tests).</summary>
    public static Geometry? For(FileKind kind) =>
        Keys.TryGetValue(kind, out var key) ? Resource(key) : null;

    /// <summary>A file no program on this PC opens: Windows would show a blank page, Brace shows a document.</summary>
    public static Geometry? Unknown => Resource("Brace.Icon.Document16");

    private static Geometry? Resource(string key) => Application.Current?.TryFindResource(key) as Geometry;
}
