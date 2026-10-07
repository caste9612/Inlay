using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Quiver.Core;

namespace Quiver.App.Interop;

/// <summary>Windows shortcuts (.lnk), the same Explorer makes: how programs and things kept elsewhere enter the archive.</summary>
internal static class ShellLinks
{
    /// <summary>
    /// Makes a shortcut to <paramref name="target"/> in <paramref name="folder"/>. A shortcut given as the target is copied
    /// instead: a shortcut to a shortcut would only be in the way.
    /// </summary>
    /// <returns>The new file.</returns>
    public static string CreateIn(string folder, string target)
    {
        var extension = Path.GetExtension(target);
        if (ArchiveItem.IsShortcut(target))
        {
            var copy = Path.Combine(folder, ArchiveNames.Unique(folder, Path.GetFileNameWithoutExtension(target), extension));
            File.Copy(target, copy);
            return copy;
        }
        var path = Path.Combine(folder, ArchiveNames.Unique(folder, NameFor(target), ".lnk"));
        Create(path, target);
        return path;
    }

    public static void Create(string linkPath, string target)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            // Programs often look for their files in the folder they start from.
            link.SetWorkingDirectory(Directory.Exists(target) ? target : Path.GetDirectoryName(target) ?? "");
            ((IPersistFile)link).Save(linkPath, fRemember: true);
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>Where a shortcut points; null when it does not point to a file or folder.</summary>
    public static string? TargetOf(string linkPath)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(linkPath, 0);
            var target = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 0);
            return target.Length > 0 ? target.ToString() : null;
        }
        finally
        {
            Marshal.FinalReleaseComObject(link);
        }
    }

    /// <summary>
    /// The name a program shows, like "Google Chrome" for chrome.exe, as it would have in the Start menu; the name of a
    /// drive or of a network share for those; the file name for everything else, extension included, so that
    /// "Offerta.pdf" stays recognizable next to the shortcut arrow.
    /// </summary>
    internal static string NameFor(string target)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(target);
        var name = Path.GetFileName(trimmed);
        if (name.Length == 0)
            name = RootName(trimmed);
        else if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = ProgramName(target) is { } program && ArchiveNames.Validate(program) is null
                ? program
                : Path.GetFileNameWithoutExtension(target);
        // Whatever the target is called, the shortcut lands in the folder, never elsewhere.
        return ArchiveNames.Validate(name) is null ? name : "Collegamento";
    }

    private static string? ProgramName(string program)
    {
        try
        {
            var description = FileVersionInfo.GetVersionInfo(program).FileDescription?.Trim();
            return string.IsNullOrEmpty(description) ? null : description;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>"Dati (D)" for a drive, as Explorer calls it but without the colon; "commesse su server" for a share.</summary>
    private static string RootName(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = root.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? $"{parts[1]} su {parts[0]}" : "Rete";
        }
        var letter = root.TrimEnd('\\', ':');
        try
        {
            var label = new DriveInfo(root).VolumeLabel;
            return string.IsNullOrWhiteSpace(label) ? $"Disco ({letter})" : $"{label} ({letter})";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return $"Disco ({letter})";
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, nint pfd, uint fFlags);
        void GetIDList(out nint ppidl);
        void SetIDList(nint pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(nint hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
