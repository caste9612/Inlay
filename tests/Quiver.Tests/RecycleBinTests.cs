using System.Text;
using Quiver.Core;

namespace Quiver.Tests;

/// <summary>
/// Restoring from a Recycle Bin folder built by the test, in the format Windows uses: the real Recycle Bin is never touched.
/// </summary>
public sealed class RecycleBinTests : IDisposable
{
    private readonly TempArchive _archive = new();
    private readonly string _bin;
    private readonly RecycleBin _recycleBin;

    public RecycleBinTests()
    {
        _bin = _archive.AddFolder(".bin");
        _recycleBin = new RecycleBin(_ => _bin);
    }

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void AFile_GoesBackWhereItWas_AndLeavesTheRecycleBin()
    {
        var original = Path.Combine(_archive.Root, "Lavoro", "Documenti", "offerta.docx");
        Recycle(original, "ABC123", DateTime.UtcNow, content: "testo");

        var item = Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(RecycleBin.TryRestore(item));

        Assert.Equal("testo", File.ReadAllText(original));
        Assert.Empty(Directory.GetFileSystemEntries(_bin));
    }

    [Fact]
    public void AFolder_GoesBackWithEverythingInIt()
    {
        var original = Path.Combine(_archive.Root, "Lavoro", "Commessa");
        var content = Recycle(original, "DEF456", DateTime.UtcNow, isFolder: true);
        File.WriteAllText(Path.Combine(content, "disegno.dwg"), "");

        Assert.True(RecycleBin.TryRestore(Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-1)))));

        Assert.True(File.Exists(Path.Combine(original, "disegno.dwg")));
    }

    [Fact]
    public void RightAfterADeletion_TheNewestItemWithThatPathIsFound()
    {
        var original = Path.Combine(_archive.Root, "nota.txt");
        Recycle(original, "OLD001", DateTime.UtcNow.AddMinutes(-5), content: "vecchia");
        Recycle(original, "NEW001", DateTime.UtcNow, content: "nuova");

        var item = Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-10)));

        Assert.EndsWith("$INEW001.txt", item.IndexPath);
    }

    [Fact]
    public void TheItemFoundIsTheOneRestored_EvenIfAnotherWithTheSamePathFollowedIt()
    {
        // Deleted by Quiver, then a copy with the same name was made and deleted too: Ctrl+Z wants the first one back.
        var original = Path.Combine(_archive.Root, "nota.txt");
        Recycle(original, "FIRST1", DateTime.UtcNow.AddMinutes(-2), content: "originale");
        var item = Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-3)));
        Recycle(original, "LATER1", DateTime.UtcNow, content: "copia");

        Assert.True(RecycleBin.TryRestore(item));

        Assert.Equal("originale", File.ReadAllText(original));
        Assert.True(File.Exists(Path.Combine(_bin, "$RLATER1.txt")));
    }

    [Fact]
    public void ItemsDeletedBeforeTheOperation_AreNotItsOwn()
    {
        var original = Path.Combine(_archive.Root, "nota.txt");
        Recycle(original, "OLD001", DateTime.UtcNow.AddHours(-1));

        Assert.Empty(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void ARestore_NeverTakesThePlaceOfAnotherItem()
    {
        var original = _archive.AddFile("nota.txt", "nuova");
        Recycle(original, "GHI789", DateTime.UtcNow, content: "vecchia");
        var item = Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-1)));

        Assert.False(RecycleBin.TryRestore(item));

        Assert.Equal("nuova", File.ReadAllText(original));
        Assert.True(File.Exists(item.ContentPath));
    }

    [Fact]
    public void ARecordWithoutItsItem_RestoresNothing()
    {
        var original = Path.Combine(_archive.Root, "nota.txt");
        var content = Recycle(original, "JKL012", DateTime.UtcNow);
        var item = Assert.Single(_recycleBin.FindDeleted([original], DateTime.UtcNow.AddMinutes(-1)));
        File.Delete(content);

        Assert.False(RecycleBin.TryRestore(item));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void RecordsOfOlderWindows_AreReadToo()
    {
        var original = Path.Combine(_archive.Root, "nota.txt");
        var deleted = new DateTime(2026, 10, 7, 8, 30, 0, DateTimeKind.Utc);
        var bytes = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(bytes, 0);
        BitConverter.GetBytes(deleted.ToFileTimeUtc()).CopyTo(bytes, 16);
        Encoding.Unicode.GetBytes(original).CopyTo(bytes, 24);
        var index = Path.Combine(_bin, "$IXYZ000.txt");
        File.WriteAllBytes(index, bytes);

        var item = RecycleBin.ReadIndex(index);

        Assert.NotNull(item);
        Assert.Equal(original, item.OriginalPath);
        Assert.Equal(deleted, item.DeletedUtc);
        Assert.Equal(Path.Combine(_bin, "$RXYZ000.txt"), item.ContentPath);
    }

    [Fact]
    public void FilesThatAreNotRecords_AreIgnored()
    {
        var index = Path.Combine(_bin, "$IBROKEN.txt");
        File.WriteAllBytes(index, [1, 2, 3]);

        Assert.Null(RecycleBin.ReadIndex(index));
    }

    [Fact]
    public void NetworkDrives_HaveNoRecycleBin()
    {
        Assert.False(RecycleBin.IsAvailableFor(@"\\server\commesse\offerta.pdf"));
        Assert.True(RecycleBin.IsAvailableFor(Path.Combine(_archive.Root, "nota.txt")));
    }

    /// <summary>Puts an item in the test's Recycle Bin the way Windows does: "$R…" with the content, "$I…" with the record.</summary>
    /// <returns>The "$R…" item.</returns>
    private string Recycle(string originalPath, string id, DateTime deletedUtc, string content = "", bool isFolder = false)
    {
        var extension = isFolder ? "" : Path.GetExtension(originalPath);
        var item = Path.Combine(_bin, "$R" + id + extension);
        if (isFolder)
            Directory.CreateDirectory(item);
        else
            File.WriteAllText(item, content);

        // Version 2: version, size, deletion time, path length in characters (ending null included), path.
        var path = Encoding.Unicode.GetBytes(originalPath + '\0');
        var bytes = new byte[28 + path.Length];
        BitConverter.GetBytes(2L).CopyTo(bytes, 0);
        BitConverter.GetBytes((long)content.Length).CopyTo(bytes, 8);
        BitConverter.GetBytes(deletedUtc.ToFileTimeUtc()).CopyTo(bytes, 16);
        BitConverter.GetBytes(originalPath.Length + 1).CopyTo(bytes, 24);
        path.CopyTo(bytes, 28);
        File.WriteAllBytes(Path.Combine(_bin, "$I" + id + extension), bytes);
        return item;
    }
}
