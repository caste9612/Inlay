using System.Text;
using System.Windows;
using Quiver.App.Interop;

namespace Quiver.App.Tests;

/// <summary>
/// Attachments and links dropped by other programs, simulated with the same formats Outlook and the browsers use.
/// </summary>
public sealed class VirtualFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AnAttachment_IsSavedWithItsNameAndDate_AndATakenNameGetsANumber() => StaThread.Run(() =>
    {
        var written = new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc);
        var data = Dropped(("offerta.pdf", "contenuto", written));

        var first = new List<string>();
        VirtualFiles.SaveTo(data, _root, first);
        var second = new List<string>();
        VirtualFiles.SaveTo(Dropped(("offerta.pdf", "contenuto", written)), _root, second);

        Assert.Equal([Path.Combine(_root, "offerta.pdf")], first);
        Assert.Equal("contenuto", File.ReadAllText(first[0]));
        Assert.Equal(written, File.GetLastWriteTimeUtc(first[0]));
        Assert.Equal([Path.Combine(_root, "offerta (2).pdf")], second);
    });

    [Fact]
    public void TwoAttachmentsWithTheSameName_AreBothKept() => StaThread.Run(() =>
    {
        var created = new List<string>();

        VirtualFiles.SaveTo(Dropped(("image001.png", "a", null), ("image001.png", "a", null)), _root, created);

        Assert.Equal([Path.Combine(_root, "image001.png"), Path.Combine(_root, "image001 (2).png")], created);
        Assert.All(created, path => Assert.True(File.Exists(path)));
    });

    [Fact]
    public void Descriptors_TellFilesFromFolders_WithTheirSize()
    {
        using var stream = new MemoryStream(Descriptors(("Allegati", null, null), (@"Allegati\nota.txt", "ciao", null)));

        var descriptors = VirtualFiles.ReadDescriptors(stream);

        Assert.Equal(2, descriptors.Count);
        Assert.True(descriptors[0].IsFolder);
        Assert.Equal(@"Allegati\nota.txt", descriptors[1].Name);
        Assert.Equal(4, descriptors[1].Size);
        Assert.Null(descriptors[1].LastWriteUtc);
    }

    [Fact]
    public void NamesFromOtherPrograms_CannotLeaveTheFolder()
    {
        Assert.Equal(["Lavoro", "offerta.pdf"], VirtualFiles.SafeParts(@"..\..\Lavoro\offerta.pdf"));
        Assert.Equal(["Domanda_ sì_.txt"], VirtualFiles.SafeParts("Domanda: sì?.txt"));
        Assert.Empty(VirtualFiles.SafeParts(@"..\"));
    }

    /// <summary>A drop as Outlook makes it: the descriptions of the files, and the content of the first one.</summary>
    private static DataObject Dropped(params (string Name, string? Content, DateTime? Written)[] files)
    {
        var data = new DataObject();
        data.SetData(VirtualFiles.DescriptorFormat, new MemoryStream(Descriptors(files)));
        data.SetData("FileContents", new MemoryStream(Encoding.UTF8.GetBytes(files[0].Content ?? "")));
        return data;
    }

    /// <summary>FILEGROUPDESCRIPTORW: the count, then 592 bytes for each file.</summary>
    private static byte[] Descriptors(params (string Name, string? Content, DateTime? Written)[] files)
    {
        const uint FD_ATTRIBUTES = 0x04, FD_WRITESTIME = 0x20, FD_FILESIZE = 0x40;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((uint)files.Length);
        foreach (var (name, content, written) in files)
        {
            var isFolder = content is null;
            var flags = FD_ATTRIBUTES | (isFolder ? 0 : FD_FILESIZE) | (written is null ? 0 : FD_WRITESTIME);
            var size = isFolder ? 0 : Encoding.UTF8.GetByteCount(content!);
            writer.Write(flags);
            writer.Write(new byte[16 + 8 + 8]);
            writer.Write(isFolder ? 0x10u : 0x80u);
            writer.Write(new byte[8 + 8]);
            writer.Write(written?.ToFileTimeUtc() ?? 0L);
            writer.Write(0u);
            writer.Write((uint)size);
            var nameBytes = new byte[520];
            Encoding.Unicode.GetBytes(name).CopyTo(nameBytes, 0);
            writer.Write(nameBytes);
        }
        writer.Flush();
        return stream.ToArray();
    }
}
