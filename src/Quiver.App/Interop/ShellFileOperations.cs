using System.Runtime.InteropServices;

namespace Quiver.App.Interop;

/// <summary>
/// Moves, copies, renames and deletes through the Windows shell, so everything behaves as in Explorer:
/// a progress window for long operations, the usual question when a name is already taken,
/// and deletions that go to the Recycle Bin (with a warning when that is not possible, as on network drives).
/// Must run on an STA thread, like the UI thread.
/// </summary>
internal static class ShellFileOperations
{
    private const uint FO_MOVE = 0x0001;
    private const uint FO_COPY = 0x0002;
    private const uint FO_DELETE = 0x0003;
    private const uint FO_RENAME = 0x0004;

    private const ushort FOF_MULTIDESTFILES = 0x0001;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_RENAMEONCOLLISION = 0x0008;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMMKDIR = 0x0200;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public nint hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public nint hNameMappings;
        public string? lpszProgressTitle;
    }

    // SHFILEOPSTRUCT holds strings, which [LibraryImport] cannot marshal.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    /// <param name="interactive">False only in tests: no dialogs at all, and existing files are replaced.</param>
    /// <returns>False when the operation failed or the user cancelled it.</returns>
    public static bool Recycle(IReadOnlyList<string> paths, nint owner, bool interactive = true) =>
        Run(FO_DELETE, paths, null, owner, FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING, interactive);

    public static bool Move(IReadOnlyList<string> paths, string destinationFolder, nint owner, bool interactive = true) =>
        Run(FO_MOVE, paths, destinationFolder, owner, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR, interactive);

    /// <summary>Moves each item to its own full path, folders included: this is how things are put back where they were.</summary>
    /// <param name="destinations">One path for each of <paramref name="paths"/>, in the same order.</param>
    public static bool MoveEach(IReadOnlyList<string> paths, IReadOnlyList<string> destinations, nint owner, bool interactive = true) =>
        Run(FO_MOVE, paths, string.Join('\0', destinations), owner, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOF_MULTIDESTFILES, interactive);

    /// <param name="renameOnCollision">A copy into the folder the files come from gets "- Copia" names, as in Explorer.</param>
    public static bool Copy(IReadOnlyList<string> paths, string destinationFolder, nint owner, bool interactive = true,
        bool renameOnCollision = false) =>
        Run(FO_COPY, paths, destinationFolder, owner,
            (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | (renameOnCollision ? FOF_RENAMEONCOLLISION : 0)), interactive);

    public static bool Rename(string path, string newPath, nint owner, bool interactive = true) =>
        Run(FO_RENAME, [path], newPath, owner, FOF_ALLOWUNDO, interactive);

    private static bool Run(uint function, IReadOnlyList<string> paths, string? destination, nint owner, ushort flags, bool interactive)
    {
        if (!interactive)
            flags |= FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;
        var operation = new SHFILEOPSTRUCT
        {
            hwnd = owner,
            wFunc = function,
            // The shell expects lists that end with two null characters: the marshaller adds the second one.
            pFrom = string.Join('\0', paths) + '\0',
            pTo = destination is null ? null : destination + '\0',
            fFlags = flags,
        };
        return SHFileOperation(ref operation) == 0 && !operation.fAnyOperationsAborted;
    }
}
