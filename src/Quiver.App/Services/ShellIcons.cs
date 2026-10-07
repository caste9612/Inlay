using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Quiver.App.Interop;

namespace Quiver.App.Services;

/// <summary>The icons Explorer shows for files, folders and shortcuts, cached.</summary>
internal static class ShellIcons
{
    /// <summary>Files whose icon comes from the file itself rather than from its extension.</summary>
    private static readonly HashSet<string> OwnIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".cur", ".ani", ".msc", ".cpl", ".scr", ".appref-ms",
    };

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Must be called on the UI thread.</summary>
    public static ImageSource? Get(string path, bool isFolder)
    {
        var extension = Path.GetExtension(path);
        // Folders can have custom icons (desktop.ini), so like programs and shortcuts they are looked up one by one.
        var byExtension = !isFolder && !OwnIconExtensions.Contains(extension);
        var key = byExtension ? "*" + extension : path;
        if (!Cache.TryGetValue(key, out var icon))
            Cache[key] = icon = Load(path, byExtension);
        return icon;
    }

    private static ImageSource? Load(string path, bool byExtension)
    {
        var info = new NativeMethods.SHFILEINFO();
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON;
        if (byExtension)
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES; // from the extension alone, without opening the file
        var found = NativeMethods.SHGetFileInfo(
            path, NativeMethods.FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        if (found == 0 || info.hIcon == 0)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }
}
