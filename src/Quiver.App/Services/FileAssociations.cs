using System.Runtime.InteropServices;

namespace Quiver.App.Services;

/// <summary>Whether Windows knows a kind of file: then its icon says something, like the program that opens it.</summary>
internal static partial class FileAssociations
{
    private const uint ASSOCF_NOTRUNCATE = 0x20;
    private const uint ASSOCF_INIT_IGNOREUNKNOWN = 0x400;
    private const uint ASSOCSTR_PROGID = 20;

    private static readonly Dictionary<string, bool> Known = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="extension">Like ".dwg"; empty for files without one.</param>
    public static bool IsKnown(string extension)
    {
        if (extension.Length == 0)
            return false;
        if (!Known.TryGetValue(extension, out var known))
        {
            // The class Windows files the kind under, like "lnkfile" for .lnk: a kind nobody registered has none.
            // Names are no help here, Windows makes one up for any extension ("File DWG").
            uint length = 0;
            var result = AssocQueryString(ASSOCF_INIT_IGNOREUNKNOWN | ASSOCF_NOTRUNCATE, ASSOCSTR_PROGID, extension, null, null, ref length);
            Known[extension] = known = result is 0 or 1;
        }
        return known;
    }

    [LibraryImport("shlwapi.dll", EntryPoint = "AssocQueryStringW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AssocQueryString(uint flags, uint str, string pszAssoc, string? pszExtra, char[]? pszOut, ref uint pcchOut);
}
