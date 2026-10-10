using Inlay.Core;

namespace Inlay.Tests;

public sealed class ArchiveReaderTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void FoldersInTheRootAreTopics_ExceptHiddenOnes()
    {
        _archive.AddFolder("Personale");
        _archive.AddFolder("Lavoro");
        _archive.AddFolder(".git");
        new DirectoryInfo(_archive.AddFolder("Nascosta")).Attributes |= FileAttributes.Hidden;

        var topics = _archive.Reader().ReadTopics();

        Assert.Equal(new[] { "Lavoro", "Personale" }, topics.Select(t => t.Name));
    }

    [Fact]
    public void TheFolderOfTheOldInbox_IsATopicLikeTheOthers()
    {
        // Inlay no longer has «Da smistare»: the folder it read, in the archives made before, is an ordinary folder.
        _archive.AddFile("_Da smistare/scansione.pdf");

        var reader = _archive.Reader();
        var topic = Assert.Single(reader.ReadTopics());

        Assert.Equal("_Da smistare", topic.Name);
        Assert.Equal(new[] { "scansione.pdf" }, Assert.Single(reader.ReadBoard(topic).Tiles).Items.Select(i => i.Name));
    }

    [Fact]
    public void FilesLooseInTheRoot_AreReadApart_InNaturalOrder_WithoutHiddenOnes()
    {
        _archive.AddFile("scaricato 10.pdf");
        _archive.AddFile("scaricato 2.pdf");
        _archive.AddFile(".nascosto");
        _archive.AddFile("Lavoro/nota.txt");

        Assert.Equal(new[] { "scaricato 2.pdf", "scaricato 10.pdf" }, _archive.Reader().ReadRootFiles().Select(i => i.Name));
    }

    [Fact]
    public void SubfoldersAreTiles_UnlessMarkedAsTopics()
    {
        _archive.AddFile("Lavoro/Clienti/Rossi/Documenti/Offerta.docx");
        _archive.AddFile("Lavoro/Interni/Procedura.txt");
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);

        var reader = _archive.Reader(metadata);
        var lavoro = Assert.Single(reader.ReadTopics());
        var clienti = Assert.Single(lavoro.Children);
        var rossi = Assert.Single(clienti.Children);

        Assert.Equal("Lavoro/Clienti/Rossi", rossi.RelativePath);
        Assert.Equal(new[] { "Interni" }, reader.ReadBoard(lavoro).Tiles.Select(t => t.Title));
        Assert.Equal(new[] { "Documenti" }, reader.ReadBoard(rossi).Tiles.Select(t => t.Title));
    }

    [Fact]
    public void LooseFilesFormTheGeneralTile_ShownFirst()
    {
        _archive.AddFile("Rossi/Note.md");
        _archive.AddFile("Rossi/Documenti/Offerta.docx");

        var reader = _archive.Reader();
        var board = reader.ReadBoard(Assert.Single(reader.ReadTopics()));

        Assert.Collection(board.Tiles,
            general =>
            {
                Assert.True(general.IsGeneral);
                Assert.Equal(ArchiveReader.GeneralTileTitle, general.Title);
                Assert.Equal(new[] { "Note.md" }, general.Items.Select(i => i.Name));
            },
            documents =>
            {
                Assert.False(documents.IsGeneral);
                Assert.Equal("Documenti", documents.Title);
            });
    }

    [Fact]
    public void FoldersMarkedAsItems_AreRowsOfGeneral_BeforeItsFiles_NotTiles()
    {
        _archive.AddFile("Rossi/Note.md");
        _archive.AddFile("Rossi/Foto/mare.jpg");
        _archive.AddFile("Rossi/Documenti/Offerta.docx");
        var metadata = new ArchiveMetadata();
        metadata.SetItem("Rossi/Foto", true);

        var reader = _archive.Reader(metadata);
        var board = reader.ReadBoard(Assert.Single(reader.ReadTopics()));

        Assert.Equal(["General", "Documenti"], board.Tiles.Select(t => t.Title));
        Assert.Equal(["Foto", "Note.md"], board.Tiles[0].Items.Select(i => i.Name));
        Assert.True(board.Tiles[0].Items[0].IsFolder);
    }

    [Fact]
    public void ARowOfGeneral_IsEnoughForGeneral()
    {
        _archive.AddFile("Rossi/Foto/mare.jpg");
        var metadata = new ArchiveMetadata();
        metadata.SetItem("Rossi/Foto", true);

        var reader = _archive.Reader(metadata);
        var tile = Assert.Single(reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles);

        Assert.True(tile.IsGeneral);
        Assert.Equal(["Foto"], tile.Items.Select(i => i.Name));
    }

    [Fact]
    public void TileItems_ListFoldersFirst_InNaturalOrder_WithoutHiddenOnes()
    {
        _archive.AddFile("Rossi/Documenti/Verbale 10.txt");
        _archive.AddFile("Rossi/Documenti/Verbale 2.txt");
        _archive.AddFile("Rossi/Documenti/Commessa 2026/Disegno.dwg");
        _archive.AddFile("Rossi/Documenti/.nascosto.txt");

        var reader = _archive.Reader();
        var tile = Assert.Single(reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles);

        Assert.Equal(new[] { "Commessa 2026", "Verbale 2.txt", "Verbale 10.txt" }, tile.Items.Select(i => i.Name));
        Assert.True(tile.Items[0].IsFolder);
    }

    [Fact]
    public void TilesFollowTheSavedOrder_ThenTheOthersByName()
    {
        foreach (var name in new[] { "A", "B", "C", "D" })
            _archive.AddFolder($"Rossi/{name}");
        var metadata = new ArchiveMetadata();
        metadata.SetOrder(["Rossi/C", "Rossi/A"]);

        var reader = _archive.Reader(metadata);
        var tiles = reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles;

        Assert.Equal(new[] { "C", "A", "B", "D" }, tiles.Select(t => t.Title));
    }

    [Fact]
    public void TopicsFollowTheSavedOrder_InTheRootAndInsideATopic()
    {
        foreach (var name in new[] { "Lavoro", "Personale", "Strumenti" })
            _archive.AddFolder(name);
        foreach (var name in new[] { "Bianchi", "Rossi", "Verdi" })
            _archive.AddFolder($"Lavoro/{name}");
        var metadata = new ArchiveMetadata();
        foreach (var name in new[] { "Bianchi", "Rossi", "Verdi" })
            metadata.SetTopic($"Lavoro/{name}", true);
        metadata.SetOrder(["Strumenti", "Lavoro"]);
        metadata.SetOrder(["Lavoro/Verdi"]);

        var topics = _archive.Reader(metadata).ReadTopics();

        Assert.Equal(["Strumenti", "Lavoro", "Personale"], topics.Select(topic => topic.Name));
        Assert.Equal(["Verdi", "Bianchi", "Rossi"], topics[1].Children.Select(topic => topic.Name));
    }

    [Fact]
    public void ATile_ShowsItsItemsInItsOrder_WithItsSize_AndGeneralGoesByTheTopic()
    {
        _archive.AddFile("Rossi/a.txt", "aaa");
        _archive.AddFile("Rossi/b.txt", "b");
        _archive.AddFile("Rossi/Documenti/x.txt");
        _archive.AddFile("Rossi/Documenti/y.txt");
        var metadata = new ArchiveMetadata();
        metadata.SetSort("Rossi", new ItemSort(SortKey.Size, Descending: false));
        metadata.SetSize("Rossi", new TileSize(2, null));
        metadata.SetItemOrder("Rossi/Documenti", ["y.txt", "x.txt"]);

        var reader = _archive.Reader(metadata);
        var tiles = reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles;

        var general = tiles[0];
        Assert.Equal(["b.txt", "a.txt"], general.Items.Select(item => item.Name));
        Assert.Equal(3, general.Items[1].Size);
        Assert.Equal(new ItemSort(SortKey.Size, Descending: false), general.Sort);
        Assert.Equal(new TileSize(2, null), general.Size);
        Assert.False(general.ByHand);
        var documents = tiles[1];
        Assert.Equal(["y.txt", "x.txt"], documents.Items.Select(item => item.Name));
        Assert.True(documents.ByHand);
        Assert.Null(documents.Sort);
        Assert.Null(documents.Size);
    }

    [Fact]
    public void Items_KnowWhenTheyLastChanged()
    {
        var file = _archive.AddFile("Rossi/Documenti/Offerta.docx");
        var changed = new DateTime(2026, 10, 5, 14, 32, 0, DateTimeKind.Local);
        File.SetLastWriteTime(file, changed);

        var reader = _archive.Reader();
        var item = Assert.Single(Assert.Single(reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles).Items);

        Assert.Equal(changed, item.Modified);
    }

    [Fact]
    public void Shortcuts_AreShownWithoutExtension()
    {
        Assert.Equal("Blocco note", new ArchiveItem(@"C:\a\Blocco note.lnk", "Blocco note.lnk", IsFolder: false).DisplayName);
        Assert.Equal("Portale", new ArchiveItem(@"C:\a\Portale.url", "Portale.url", IsFolder: false).DisplayName);
        Assert.Equal("Offerta.docx", new ArchiveItem(@"C:\a\Offerta.docx", "Offerta.docx", IsFolder: false).DisplayName);
    }
}
