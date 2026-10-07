namespace Quiver.Core;

/// <summary>
/// What a file is, for its icon: the common kinds get one in the style of Brace, the same in every app. Programs, shortcuts
/// and anything else keep the icon Windows gives them, which for them is a logo.
/// </summary>
public enum FileKind
{
    /// <summary>Not one of the kinds below: the icon of Windows.</summary>
    Other,
    Folder,
    Text,
    Word,
    Pdf,
    Spreadsheet,
    Presentation,
    Image,
    Video,
    Audio,
    Archive,
    Code,
    Mail,
    Web,
    Model3D,
}

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build(
        (FileKind.Text, "txt md markdown log nfo"),
        (FileKind.Word, "doc docx docm dot dotx odt rtf pages"),
        (FileKind.Pdf, "pdf"),
        (FileKind.Spreadsheet, "xls xlsx xlsm xlsb csv tsv ods numbers"),
        (FileKind.Presentation, "ppt pptx pptm pps ppsx odp key"),
        (FileKind.Image, "jpg jpeg png gif bmp tif tiff webp heic heif avif svg ico psd jpx jp2 raw dng cr2 nef arw"),
        (FileKind.Video, "mp4 m4v mov avi mkv wmv webm mpg mpeg flv 3gp"),
        (FileKind.Audio, "mp3 wav flac m4a aac ogg opus wma aiff"),
        (FileKind.Archive, "zip 7z rar tar gz tgz bz2 xz zst cab iso"),
        (FileKind.Code, "cs py js ts jsx tsx json xml yaml yml toml css scss cpp c h hpp java kt go rs rb php sql sh ps1 psm1 bat cmd vb fs swift lua ipynb xaml csproj sln slnx props targets ini cfg conf config"),
        (FileKind.Mail, "eml msg mbox oft"),
        (FileKind.Web, "url html htm mht mhtml webloc"),
        (FileKind.Model3D, "stl obj 3mf step stp iges igs fbx glb gltf ply"));

    public static FileKind Of(ArchiveItem item) =>
        item.IsFolder
            ? FileKind.Folder
            : ByExtension.GetValueOrDefault(Path.GetExtension(item.Name).TrimStart('.'), FileKind.Other);

    private static Dictionary<string, FileKind> Build(params (FileKind Kind, string Extensions)[] kinds)
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, extensions) in kinds)
        {
            foreach (var extension in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                map[extension] = kind;
        }
        return map;
    }
}
