using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Inlay.App.Services;

internal static class ShellLauncher
{
    private const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    private const int SW_SHOWNORMAL = 1;

    /// <summary>Opens a file, folder or shortcut like a double-click in Explorer does.</summary>
    public static void Open(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    /// <summary>Opens the containing folder in Explorer, with the item selected.</summary>
    public static void Reveal(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    /// <summary>The Windows "Open with" dialog, to pick the program for a file.</summary>
    public static void OpenWith(string path) =>
        Process.Start(new ProcessStartInfo("rundll32.exe", $"shell32.dll,OpenAs_RunDLL {path}"))?.Dispose();

    /// <summary>The Windows "Properties" window of a file or folder.</summary>
    public static void ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_INVOKEIDLIST,
            lpVerb = "properties",
            lpFile = path,
            nShow = SW_SHOWNORMAL,
        };
        if (!ShellExecuteEx(ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public nint hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public nint hInstApp;
        public nint lpIDList;
        public string? lpClass;
        public nint hkeyClass;
        public uint dwHotKey;
        public nint hIconOrMonitor;
        public nint hProcess;
    }

    // SHELLEXECUTEINFO holds strings, which [LibraryImport] cannot marshal.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);
}
