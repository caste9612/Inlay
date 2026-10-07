using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quiver.App.Interop;

namespace Quiver.App.Services;

/// <summary>
/// The icons Explorer shows for programs, shortcuts and the files no glyph of Brace stands for, cached. Windows reads
/// them one file at a time, and a shortcut to a network place that does not answer can take seconds: so they are read
/// on a thread of their own, and the panel shows a glyph of Brace until each one arrives.
/// </summary>
internal static class ShellIcons
{
    /// <summary>Files whose icon comes from the file itself rather than from its extension.</summary>
    private static readonly HashSet<string> OwnIconExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".cur", ".ani", ".msc", ".cpl", ".scr", ".appref-ms",
    };

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Who is waiting for an icon on its way, by key: the files of a kind share one icon, read once.</summary>
    private static readonly Dictionary<string, List<Action<ImageSource?>>> Waiting = new(StringComparer.OrdinalIgnoreCase);

    private static readonly BlockingCollection<Request> Queue = new();
    private static Thread? _reader;

    /// <summary>
    /// Reads every icon at once, on the calling thread: for the previews drawn from the command line, where nothing
    /// pumps the dispatcher and nothing else happens meanwhile.
    /// </summary>
    public static bool Synchronous { get; set; }

    /// <summary>The icon, if it was read already; null when Windows has none for the file.</summary>
    /// <returns>Whether the icon is known. Otherwise <see cref="Load"/> reads it.</returns>
    public static bool TryGet(string path, bool isFolder, out ImageSource? icon)
    {
        lock (Gate)
            return Cache.TryGetValue(KeyOf(path, isFolder, out _), out icon);
    }

    /// <summary>
    /// Reads the icon on the icon thread and hands it to <paramref name="deliver"/> on the thread that asked, through
    /// its dispatcher; at once when it is known already, or when <see cref="Synchronous"/> is set.
    /// </summary>
    public static void Load(string path, bool isFolder, Action<ImageSource?> deliver)
    {
        var key = KeyOf(path, isFolder, out var byExtension);
        ImageSource? icon;
        bool known;
        lock (Gate)
        {
            known = Cache.TryGetValue(key, out icon);
            if (!known && !Synchronous)
            {
                if (Waiting.TryGetValue(key, out var callbacks))
                {
                    callbacks.Add(deliver);
                }
                else
                {
                    Waiting[key] = [deliver];
                    Queue.Add(new Request(key, path, byExtension, Dispatcher.CurrentDispatcher));
                    _reader ??= StartReader();
                }
                return;
            }
        }
        if (!known)
        {
            icon = Read(path, byExtension);
            lock (Gate)
                Cache[key] = icon;
        }
        // Outside the lock: whoever asked may ask for more icons while taking this one.
        deliver(icon);
    }

    private static Thread StartReader()
    {
        // The shell extracts icons from a single-threaded apartment, like the UI thread.
        var thread = new Thread(ReadAll) { IsBackground = true, Name = "Quiver icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return thread;
    }

    private static void ReadAll()
    {
        foreach (var request in Queue.GetConsumingEnumerable())
        {
            var icon = Read(request.Path, request.ByExtension);
            request.Dispatcher.InvokeAsync(() => Deliver(request.Key, icon));
        }
    }

    /// <summary>On the thread that asked: the icon goes in the cache and to everyone waiting for it.</summary>
    private static void Deliver(string key, ImageSource? icon)
    {
        List<Action<ImageSource?>>? callbacks;
        lock (Gate)
        {
            Cache[key] = icon;
            Waiting.Remove(key, out callbacks);
        }
        foreach (var deliver in callbacks ?? [])
            deliver(icon);
    }

    private static string KeyOf(string path, bool isFolder, out bool byExtension)
    {
        var extension = Path.GetExtension(path);
        // Folders can have custom icons (desktop.ini), so like programs and shortcuts they are looked up one by one.
        byExtension = !isFolder && !OwnIconExtensions.Contains(extension);
        return byExtension ? "*" + extension : path;
    }

    private static ImageSource? Read(string path, bool byExtension)
    {
        var info = new NativeMethods.SHFILEINFO();
        // Small icons: 16 px at 100% scaling, 24 px at 150%, so they stay sharp in 16-pixel rows.
        var flags = NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON;
        if (byExtension)
            flags |= NativeMethods.SHGFI_USEFILEATTRIBUTES; // from the extension alone, without opening the file
        var found = NativeMethods.SHGetFileInfo(
            path, NativeMethods.FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);
        if (found == 0 || info.hIcon == 0)
            return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Frozen: made on the icon thread, shown on the UI thread.
            image.Freeze();
            return image;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private sealed record Request(string Key, string Path, bool ByExtension, Dispatcher Dispatcher);
}
