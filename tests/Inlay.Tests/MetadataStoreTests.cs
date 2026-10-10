using Inlay.Core;

namespace Inlay.Tests;

public sealed class MetadataStoreTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void MissingFile_GivesEmptyMetadata() => Assert.Empty(MetadataStore.Load(_archive.Root).Folders);

    [Fact]
    public void SavedMetadata_IsReadBack_WithKeysIgnoringCase()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.Folders["Lavoro/Clienti"].Workspace = "3";
        MetadataStore.Save(_archive.Root, metadata);

        var loaded = MetadataStore.Load(_archive.Root);

        Assert.True(loaded.IsTopic("lavoro/clienti"));
        Assert.Equal("3", loaded.Folders["LAVORO/CLIENTI"].Workspace);
    }

    [Fact]
    public void Save_PutsTheFileInAHiddenFolder_WithTheDocumentedShape()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);

        MetadataStore.Save(_archive.Root, metadata);

        var folder = new DirectoryInfo(Path.Combine(_archive.Root, ArchiveLayout.MetadataFolderName));
        Assert.True(folder.Attributes.HasFlag(FileAttributes.Hidden));
        var json = File.ReadAllText(MetadataStore.GetPath(_archive.Root));
        Assert.Contains("\"folders\"", json);
        Assert.Contains("\"kind\": \"topic\"", json);
        Assert.DoesNotContain("\"workspace\"", json);
    }

    [Fact]
    public void Demoting_RemovesOnlyEntriesLeftEmpty()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("A/B", true);
        metadata.SetTopic("A/C", true);
        metadata.Folders["A/C"].Color = "#FF0000";

        metadata.SetTopic("A/B", false);
        metadata.SetTopic("A/C", false);

        Assert.False(metadata.Folders.ContainsKey("A/B"));
        Assert.False(metadata.IsTopic("A/C"));
        Assert.Equal("#FF0000", metadata.Folders["A/C"].Color);
    }

    [Fact]
    public void SetTopic_ReportsWhetherSomethingChanged()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetTopic("A/B", true));
        Assert.False(metadata.SetTopic("a/b", true));
        Assert.True(metadata.SetTopic("A/B", false));
        Assert.False(metadata.SetTopic("A/B", false));
    }

    [Fact]
    public void AFolderIsATopicOrARowOfGenerale_NeverBoth_AndEachMarkGoesOnlyByItsOwnName()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetItem("A/B", true));
        Assert.False(metadata.SetItem("a/b", true));
        // Taking away the mark of a topic leaves a row as it is.
        Assert.False(metadata.SetTopic("A/B", false));
        Assert.True(metadata.IsItem("A/B"));

        Assert.True(metadata.SetTopic("A/B", true));
        Assert.False(metadata.IsItem("A/B"));
        Assert.False(metadata.SetItem("A/B", false));
        Assert.True(metadata.IsTopic("A/B"));

        Assert.True(metadata.SetTopic("A/B", false));
        Assert.Empty(metadata.Folders);
    }

    [Fact]
    public void ARowOfGenerale_IsSavedAsAnItem()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetItem("Lavoro/Foto", true);

        MetadataStore.Save(_archive.Root, metadata);

        Assert.Contains("\"kind\": \"item\"", File.ReadAllText(MetadataStore.GetPath(_archive.Root)));
        Assert.True(MetadataStore.Load(_archive.Root).IsItem("lavoro/foto"));
    }

    [Fact]
    public void MovingAFolder_CarriesItsEntriesAndThoseBelowIt()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);
        metadata.SetTopic("Lavoro/Clientela", true);

        Assert.True(metadata.MoveFolder("Lavoro/Clienti", "Archiviati/Clienti 2025"));

        Assert.Equal(
            new[] { "Archiviati/Clienti 2025", "Archiviati/Clienti 2025/Rossi", "Lavoro/Clientela" },
            metadata.Folders.Keys.Order());
    }

    [Fact]
    public void RemovingAFolder_ForgetsItsEntriesAndThoseBelowIt()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);
        metadata.SetTopic("Personale", true);

        Assert.True(metadata.RemoveFolder("lavoro/clienti"));
        Assert.False(metadata.RemoveFolder("Lavoro/Clienti"));

        Assert.Equal(new[] { "Personale" }, metadata.Folders.Keys);
    }

    [Fact]
    public void TileOrder_IsRemembered_AndTravelsWithARenamedFolder()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetOrder(["Rossi/B", "Rossi/A"]));
        Assert.False(metadata.SetOrder(["Rossi/B", "Rossi/A"]));
        metadata.MoveFolder("Rossi/B", "Rossi/Bozze");

        Assert.Equal(0, metadata.OrderOf("Rossi/Bozze"));
        Assert.Equal(1, metadata.OrderOf("Rossi/A"));
        Assert.Null(metadata.OrderOf("Rossi/B"));
    }

    [Fact]
    public void ATileSize_IsKeptWithinTheBoard_AndTheUsualOneNeedsNoEntry()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetSize("Rossi/Documenti", new TileSize(5, 100)));
        Assert.Equal(new TileSize(TileSize.MaxColumns, TileSize.MaxRows), metadata.SizeOf("Rossi/Documenti"));
        Assert.False(metadata.SetSize("Rossi/Documenti", new TileSize(3, 40)));

        Assert.True(metadata.SetSize("Rossi/Documenti", new TileSize(1, null)));
        Assert.Null(metadata.SizeOf("Rossi/Documenti"));
        Assert.Empty(metadata.Folders);
    }

    [Fact]
    public void ASort_AndAnOrderByHand_TakeEachOthersPlace()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetItemOrder("Rossi/Documenti", ["b.txt", "a.txt"]));
        Assert.True(metadata.SetSort("Rossi/Documenti", ItemSort.For(SortKey.Modified)));
        Assert.Null(metadata.ItemOrderOf("Rossi/Documenti"));
        Assert.Equal(new ItemSort(SortKey.Modified, Descending: true), metadata.SortOf("Rossi/Documenti"));

        Assert.True(metadata.SetItemOrder("Rossi/Documenti", ["b.txt", "a.txt"]));
        Assert.Null(metadata.SortOf("Rossi/Documenti"));
        Assert.False(metadata.SetItemOrder("Rossi/Documenti", ["b.txt", "a.txt"]));

        // By name from A is the usual order: it needs no entry.
        Assert.True(metadata.SetSort("Rossi/Documenti", ItemSort.For(SortKey.Name)));
        Assert.Empty(metadata.Folders);
    }

    [Fact]
    public void ARenamedItem_KeepsItsPlaceInTheOrderByHand()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetItemOrder("Rossi/Documenti", ["b.txt", "a.txt", "c.txt"]);

        Assert.True(metadata.RenameItem("Rossi/Documenti", "A.TXT", "z.txt"));
        Assert.False(metadata.RenameItem("Rossi/Documenti", "manca.txt", "y.txt"));

        Assert.Equal(["b.txt", "z.txt", "c.txt"], metadata.ItemOrderOf("Rossi/Documenti"));
    }

    [Fact]
    public void SizeSortAndOrderByHand_AreSavedWithReadableNames()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetSize("Rossi/Documenti", new TileSize(2, 12));
        metadata.SetSort("Rossi/Documenti", ItemSort.For(SortKey.Modified));
        metadata.SetItemOrder("Rossi/Programmi", ["Calcolatrice.lnk", "Blocco note.lnk"]);

        MetadataStore.Save(_archive.Root, metadata);
        var json = File.ReadAllText(MetadataStore.GetPath(_archive.Root));
        var loaded = MetadataStore.Load(_archive.Root);

        Assert.Contains("\"columns\": 2", json);
        Assert.Contains("\"key\": \"modified\"", json);
        Assert.Contains("\"items\": [", json);
        Assert.Equal(new TileSize(2, 12), loaded.SizeOf("rossi/documenti"));
        Assert.Equal(new ItemSort(SortKey.Modified, Descending: true), loaded.SortOf("Rossi/Documenti"));
        Assert.Equal(["Calcolatrice.lnk", "Blocco note.lnk"], loaded.ItemOrderOf("Rossi/Programmi"));
        Assert.True(loaded.Folders["Rossi/Programmi"].SameAs(metadata.Folders["Rossi/Programmi"]));
    }

    [Fact]
    public void UnreadableFile_IsSetAside_AndEmptyMetadataIsUsed()
    {
        var path = _archive.AddFile($"{ArchiveLayout.MetadataFolderName}/{ArchiveLayout.MetadataFileName}", "{ not json");

        var loaded = MetadataStore.Load(_archive.Root);

        Assert.Empty(loaded.Folders);
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }
}
