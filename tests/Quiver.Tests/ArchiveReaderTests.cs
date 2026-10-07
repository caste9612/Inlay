using Quiver.Core;

namespace Quiver.Tests;

public sealed class ArchiveReaderTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void FoldersInTheRootAreTopics_ExceptTheInboxAndHiddenFolders()
    {
        _archive.AddFolder("Personale");
        _archive.AddFolder("Lavoro");
        _archive.AddFolder(ArchiveLayout.InboxFolderName);
        _archive.AddFolder(".git");
        new DirectoryInfo(_archive.AddFolder("Nascosta")).Attributes |= FileAttributes.Hidden;

        var topics = _archive.Reader().ReadTopics();

        Assert.Equal(new[] { "Lavoro", "Personale" }, topics.Select(t => t.Name));
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
    public void Inbox_CollectsTheInboxFolderAndLooseFilesInTheRoot()
    {
        _archive.AddFile("scaricato.pdf");
        _archive.AddFile($"{ArchiveLayout.InboxFolderName}/scansione.pdf");
        _archive.AddFolder("Lavoro");

        var inbox = _archive.Reader().ReadInbox();

        Assert.Equal(new[] { "scaricato.pdf", "scansione.pdf" }, inbox.Select(i => i.Name));
    }

    [Fact]
    public void MissingInboxFolder_MeansAnEmptyInbox() => Assert.Empty(_archive.Reader().ReadInbox());

    [Fact]
    public void Shortcuts_AreShownWithoutExtension()
    {
        Assert.Equal("Blocco note", new ArchiveItem(@"C:\a\Blocco note.lnk", "Blocco note.lnk", IsFolder: false).DisplayName);
        Assert.Equal("Portale", new ArchiveItem(@"C:\a\Portale.url", "Portale.url", IsFolder: false).DisplayName);
        Assert.Equal("Offerta.docx", new ArchiveItem(@"C:\a\Offerta.docx", "Offerta.docx", IsFolder: false).DisplayName);
    }
}
