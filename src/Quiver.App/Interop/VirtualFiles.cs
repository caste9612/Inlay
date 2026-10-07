using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using Quiver.Core;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using IDataObject = System.Windows.IDataObject;

namespace Quiver.App.Interop;

/// <summary>
/// Files that exist only inside another program until they are dropped: attachments and messages dragged from Outlook,
/// links and images from the browser. They come as a list of names (FileGroupDescriptorW) plus the content of each one
/// (FileContents), which must be read while the drop is happening.
/// </summary>
internal static class VirtualFiles
{
    public const string DescriptorFormat = "FileGroupDescriptorW";
    private const string ContentsFormat = "FileContents";

    // FILEDESCRIPTORW: flags, CLSID, SIZEL, POINTL, attributes, three FILETIMEs, size (high, low), name of 260 characters.
    private const int DescriptorSize = 4 + 16 + 8 + 8 + 4 + 8 + 8 + 8 + 4 + 4 + 520;
    private const uint FD_ATTRIBUTES = 0x04;
    private const uint FD_WRITESTIME = 0x20;
    private const uint FD_FILESIZE = 0x40;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    public static bool ArePresent(IDataObject data) => data.GetDataPresent(DescriptorFormat);

    /// <summary>One file or folder of the group, as the program describes it.</summary>
    /// <param name="Name">Its path inside the group, like "Allegati\offerta.pdf".</param>
    internal sealed record Descriptor(string Name, bool IsFolder, long? Size, DateTime? LastWriteUtc);

    /// <summary>
    /// Writes the dropped files into <paramref name="folder"/>: a name already taken there gets " (2)", as in Explorer.
    /// </summary>
    /// <param name="created">Receives what was created at the top level, also when a later file fails.</param>
    /// <exception cref="COMException">The program did not give the content of a file.</exception>
    /// <exception cref="IOException">A file could not be written.</exception>
    public static void SaveTo(IDataObject data, string folder, List<string> created)
    {
        if (data.GetData(DescriptorFormat) is not MemoryStream stream)
            return;
        var descriptors = ReadDescriptors(stream);
        var comData = (ComDataObject)data;
        // A dropped folder gets a name free in the destination once, and everything inside it follows that name.
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < descriptors.Count; index++)
        {
            var descriptor = descriptors[index];
            var parts = SafeParts(descriptor.Name);
            if (parts.Count == 0)
                continue;
            if (parts.Count > 1 || descriptor.IsFolder)
            {
                if (!folders.TryGetValue(parts[0], out var top))
                {
                    folders[parts[0]] = top = ArchiveNames.Unique(folder, parts[0]);
                    created.Add(Path.Combine(folder, top));
                }
                parts[0] = top;
            }
            var path = Path.Combine([folder, .. parts]);
            if (descriptor.IsFolder)
            {
                Directory.CreateDirectory(path);
                continue;
            }
            // Each file gets a name free in its folder: two attachments called "image001.png" are both kept.
            var parent = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(parent);
            path = Path.Combine(parent, ArchiveNames.Unique(parent, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path)));
            if (parts.Count == 1)
                created.Add(path);
            WriteContent(comData, index, path, descriptor.Size);
            if (descriptor.LastWriteUtc is { } written)
                File.SetLastWriteTimeUtc(path, written);
        }
    }

    internal static List<Descriptor> ReadDescriptors(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Unicode, leaveOpen: true);
        var count = reader.ReadUInt32();
        var descriptors = new List<Descriptor>();
        for (var i = 0; i < count && stream.Length - stream.Position >= DescriptorSize; i++)
        {
            var flags = reader.ReadUInt32();
            reader.ReadBytes(16 + 8 + 8); // CLSID, SIZEL, POINTL
            var attributes = reader.ReadUInt32();
            reader.ReadBytes(8 + 8); // creation and last access time
            var lastWrite = reader.ReadInt64();
            var sizeHigh = reader.ReadUInt32();
            var sizeLow = reader.ReadUInt32();
            var name = Encoding.Unicode.GetString(reader.ReadBytes(520));
            var end = name.IndexOf('\0');
            descriptors.Add(new Descriptor(
                end >= 0 ? name[..end] : name,
                (flags & FD_ATTRIBUTES) != 0 && (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0,
                (flags & FD_FILESIZE) != 0 ? ((long)sizeHigh << 32) | sizeLow : null,
                (flags & FD_WRITESTIME) != 0 && lastWrite > 0 ? DateTime.FromFileTimeUtc(lastWrite) : null));
        }
        return descriptors;
    }

    /// <summary>
    /// The parts of a path given by another program, made safe: no way out of the destination folder with "..",
    /// and characters Windows does not accept replaced, as Explorer does.
    /// </summary>
    internal static List<string> SafeParts(string name) =>
        name.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => string.Concat(part.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.'))
            .Where(part => part.Length > 0 && part != "..")
            .ToList();

    private static void WriteContent(ComDataObject data, int index, string path, long? size)
    {
        var format = new FORMATETC
        {
            cfFormat = (short)DataFormats.GetDataFormat(ContentsFormat).Id,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTORAGE,
        };
        data.GetData(ref format, out var medium);
        try
        {
            switch (medium.tymed)
            {
                case TYMED.TYMED_HGLOBAL:
                    WriteFromGlobal(medium.unionmember, path, size);
                    break;
                case TYMED.TYMED_ISTREAM:
                    var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
                    try
                    {
                        WriteFromStream(stream, path);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(stream);
                    }
                    break;
                case TYMED.TYMED_ISTORAGE:
                    // A message dragged from Outlook: saved as a .msg file, like on the desktop.
                    var storage = (IStorage)Marshal.GetObjectForIUnknown(medium.unionmember);
                    try
                    {
                        WriteFromStorage(storage, path);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(storage);
                    }
                    break;
                default:
                    throw new COMException($"Unsupported content for {Path.GetFileName(path)}: {medium.tymed}");
            }
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static void WriteFromGlobal(nint global, string path, long? size)
    {
        var pointer = GlobalLock(global);
        if (pointer == 0)
            throw new COMException($"Could not read the content of {Path.GetFileName(path)}");
        try
        {
            // The block can be larger than the file: the size in the description is the right one.
            var length = (long)GlobalSize(global);
            if (size is { } known && known <= length)
                length = known;
            using var output = File.Create(path);
            CopyUnmanaged(pointer, length, output);
        }
        finally
        {
            GlobalUnlock(global);
        }
    }

    private static void CopyUnmanaged(nint pointer, long length, Stream output)
    {
        var buffer = new byte[81920];
        for (long offset = 0; offset < length;)
        {
            var count = (int)Math.Min(buffer.Length, length - offset);
            Marshal.Copy(pointer + (nint)offset, buffer, 0, count);
            output.Write(buffer, 0, count);
            offset += count;
        }
    }

    private static void WriteFromStream(IStream stream, string path)
    {
        try
        {
            // Some programs hand over the stream already read to the end.
            stream.Seek(0, 0 /* STREAM_SEEK_SET */, 0);
        }
        catch (Exception e) when (e is COMException or NotImplementedException or NotSupportedException)
        {
            // Not seekable (E_NOTIMPL and the like): it starts at the beginning anyway.
        }
        using var output = File.Create(path);
        var buffer = new byte[81920];
        var read = Marshal.AllocCoTaskMem(sizeof(int));
        try
        {
            while (true)
            {
                stream.Read(buffer, buffer.Length, read);
                var count = Marshal.ReadInt32(read);
                if (count <= 0)
                    break;
                output.Write(buffer, 0, count);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(read);
        }
    }

    private static void WriteFromStorage(IStorage storage, string path)
    {
        const uint STGM_CREATE = 0x1000, STGM_READWRITE = 0x2, STGM_SHARE_EXCLUSIVE = 0x10;
        Marshal.ThrowExceptionForHR(StgCreateDocfile(path, STGM_CREATE | STGM_READWRITE | STGM_SHARE_EXCLUSIVE, 0, out var file));
        try
        {
            storage.CopyTo(0, 0, 0, file);
            file.Commit(0);
        }
        finally
        {
            Marshal.FinalReleaseComObject(file);
        }
    }

    [ComImport]
    [Guid("0000000B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        // Only CopyTo and Commit are called; the others keep the order of the interface.
        void CreateStream();
        void OpenStream();
        void CreateStorage();
        void OpenStorage();
        void CopyTo(uint ciidExclude, nint rgiidExclude, nint snbExclude, IStorage pstgDest);
        void MoveElementTo();
        void Commit(uint grfCommitFlags);
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode)]
    private static extern int StgCreateDocfile(string pwcsName, uint grfMode, uint reserved, out IStorage ppstgOpen);

    [DllImport("kernel32.dll")]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint hMem);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(nint hMem);
}
