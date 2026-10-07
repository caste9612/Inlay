using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Quiver.App.Interop;

namespace Quiver.App.Tests;

/// <summary>
/// Runs the shell operations for real in a temporary folder, without any dialog.
/// Deleting is not tested here: it would fill the Recycle Bin.
/// </summary>
public sealed class ShellFileOperationsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Move_PutsTheItemsInTheDestination()
    {
        var file = AddFile("A/uno.txt");
        var folder = AddFile("A/Sotto/due.txt");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;

        Assert.True(StaThread.Run(() => ShellFileOperations.Move([file, Path.GetDirectoryName(folder)!], destination, 0, interactive: false)));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(destination, "uno.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "Sotto", "due.txt")));
    }

    [Fact]
    public void OnTheSameDrive_AMoveOnlyChangesThePath_TheContentIsNeitherCopiedNorRewritten()
    {
        var file = AddFile("A/grande.bin", "contenuto");
        var before = FileId(file);
        var destination = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;

        Assert.True(StaThread.Run(() => ShellFileOperations.Move([file], destination, 0, interactive: false)));

        // Same file on the disk, under a new path: what NTFS calls the file index does not change.
        var moved = Path.Combine(destination, "grande.bin");
        Assert.Equal(before, FileId(moved));
        // A copy instead is another file, with an index of its own.
        File.Copy(moved, Path.Combine(destination, "copia.bin"));
        Assert.NotEqual(before, FileId(Path.Combine(destination, "copia.bin")));
    }

    [Fact]
    public void Copy_KeepsTheOriginal()
    {
        var file = AddFile("A/uno.txt", "contenuto");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;

        Assert.True(StaThread.Run(() => ShellFileOperations.Copy([file], destination, 0, interactive: false)));

        Assert.True(File.Exists(file));
        Assert.Equal("contenuto", File.ReadAllText(Path.Combine(destination, "uno.txt")));
    }

    [Fact]
    public void Rename_WorksForFilesAndFolders_EvenWhenOnlyTheCaseChanges()
    {
        var file = AddFile("A/vecchio.txt");
        var folder = Path.Combine(_root, "A");

        Assert.True(StaThread.Run(() => ShellFileOperations.Rename(file, Path.Combine(folder, "nuovo.txt"), 0, interactive: false)));
        Assert.True(StaThread.Run(() => ShellFileOperations.Rename(folder, Path.Combine(_root, "a"), 0, interactive: false)));

        Assert.Equal(new[] { "a" }, Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_root, "a", "nuovo.txt")));
    }

    private string AddFile(string relativePath, string content = "")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The volume and the index NTFS gives the file: the same pair means the same file on the disk.</summary>
    private static (uint Volume, ulong Index) FileId(string path)
    {
        using var handle = File.OpenHandle(path);
        Assert.True(GetFileInformationByHandle(handle, out var info));
        return (info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    // The FILETIMEs are pairs of 32-bit values in Windows: aligned to 4 bytes, not 8.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION information);
}
