using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;

namespace Inlay.App.Services;

/// <summary>
/// Files on the Windows clipboard, in the same format Explorer uses: what Inlay copies or cuts can be pasted
/// in Explorer, on the desktop or in a mail, and the other way round.
/// </summary>
internal static class FileClipboard
{
    // Tells Explorer whether the files were cut (move) or copied.
    private const string DropEffectFormat = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;

    public static bool HasFiles
    {
        get
        {
            try
            {
                return Clipboard.ContainsFileDropList();
            }
            catch (ExternalException)
            {
                return false;
            }
        }
    }

    /// <returns>False when another program is holding the clipboard.</returns>
    public static bool Put(IReadOnlyList<string> paths, bool cut)
    {
        var files = new StringCollection();
        files.AddRange([.. paths]);
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));
        try
        {
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    /// <returns>The files on the clipboard and whether they were cut; null when there are none.</returns>
    public static (IReadOnlyList<string> Paths, bool Cut)? Read()
    {
        try
        {
            if (!Clipboard.ContainsFileDropList())
                return null;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            var cut = Clipboard.GetData(DropEffectFormat) is MemoryStream effect && effect.Length >= 4 &&
                      (BitConverter.ToInt32(effect.ToArray(), 0) & DropEffectMove) != 0;
            return (paths, cut);
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    public static void Clear()
    {
        try
        {
            Clipboard.Clear();
        }
        catch (ExternalException)
        {
            // Busy: the cut files stay on the clipboard, which is harmless.
        }
    }
}
