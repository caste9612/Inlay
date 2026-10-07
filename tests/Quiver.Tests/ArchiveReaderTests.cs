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
    public void TilesFollowTheSavedOrder_ThenTheOthersByName()
    {
        foreach (var name in new[] { "A", "B", "C", "D" })
            _archive.AddFolder($"Rossi/{name}");
        var metadata = new ArchiveMetadata();
        metadata.SetTileOrder(["Rossi/C", "Rossi/A"]);

        var reader = _archive.Reader(metadata);
        var tiles = reader.ReadBoard(Assert.Single(reader.ReadTopics())).Tiles;

        Assert.Equal(new[] { "C", "A", "B", "D" }, tiles.Select(t => t.Title));
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
    public void Inbox_CollectsTheInboxFolderAndLooseFilesInTheRoot()
    {
        _archive.AddFile("scaricato.pdf");
        _archive.AddFile($"{ArchiveLayout.InboxFolderName}/scansione.pdf");
        _archive.AddFolder("Lavoro");

        var tile = Assert.Single(_archive.Reader().ReadInbox());

        Assert.Equal(ArchiveReader.ArchiveInboxTitle, tile.Title);
        Assert.Equal(Path.Combine(_archive.Root, ArchiveLayout.InboxFolderName), tile.FolderPath);
        Assert.Equal(new[] { "scaricato.pdf", "scansione.pdf" }, tile.Items.Select(i => i.Name));
    }

    [Fact]
    public void MissingInboxFolder_MeansAnEmptyInbox() => Assert.Empty(_archive.Reader().ReadInbox());

    [Fact]
    public void TheDesktop_IsATileOfTheInbox_WithTheFilesOfBothItsFolders()
    {
        _archive.AddFile($"{ArchiveLayout.InboxFolderName}/scansione.pdf");
        var desktop = _archive.AddFolder("../Desktop-" + Guid.NewGuid().ToString("N"));
        var shared = _archive.AddFolder("../Pubblico-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(Path.Combine(desktop, "Verbale 10.txt"), "");
        File.WriteAllText(Path.Combine(shared, "Verbale 2.txt"), "");
        Directory.CreateDirectory(Path.Combine(desktop, "Progetto"));
        try
        {
            var reader = new ArchiveReader(_archive.Root, new ArchiveMetadata(), [new InboxSource("Desktop", [desktop, shared])]);

            var tiles = reader.ReadInbox();

            Assert.Equal(new[] { ArchiveReader.ArchiveInboxTitle, "Desktop" }, tiles.Select(t => t.Title));
            // Folders first, then files, in natural order across both folders; dropped files go to the first one.
            Assert.Equal(new[] { "Progetto", "Verbale 2.txt", "Verbale 10.txt" }, tiles[1].Items.Select(i => i.Name));
            Assert.Equal(desktop, tiles[1].FolderPath);
            Assert.True(reader.IsInInbox(Path.Combine(shared, "Verbale 2.txt")));
        }
        finally
        {
            Directory.Delete(desktop, recursive: true);
            Directory.Delete(shared, recursive: true);
        }
    }

    [Fact]
    public void TheFolderHoldingTheArchive_IsNotSomethingToSort()
    {
        // The archive is a folder on the Desktop.
        var desktop = _archive.AddFolder("Desktop");
        var root = _archive.AddFolder("Desktop/Archivio");
        File.WriteAllText(Path.Combine(desktop, "nota.txt"), "");

        var tile = Assert.Single(new ArchiveReader(root, new ArchiveMetadata(), [new InboxSource("Desktop", [desktop])]).ReadInbox());

        Assert.Equal(new[] { "nota.txt" }, tile.Items.Select(i => i.Name));
    }

    [Fact]
    public void ASourceInsideTheArchive_IsLeftOut_ItsFilesAreShownThereAlready()
    {
        _archive.AddFile("Lavoro/Scrivania/nota.txt");

        var reader = new ArchiveReader(_archive.Root, new ArchiveMetadata(),
            [new InboxSource("Desktop", [Path.Combine(_archive.Root, "Lavoro", "Scrivania")])]);

        Assert.Empty(reader.InboxSources);
        Assert.Empty(reader.ReadInbox());
    }

    [Fact]
    public void WhatWaitsToBeSorted_IsInTheInboxFolder_OrLooseInTheRoot()
    {
        var reader = _archive.Reader();

        Assert.True(reader.IsInInbox(Path.Combine(_archive.Root, "scaricato.pdf")));
        Assert.True(reader.IsInInbox(Path.Combine(_archive.Root, ArchiveLayout.InboxFolderName, "scansione.pdf")));
        Assert.False(reader.IsInInbox(Path.Combine(_archive.Root, "Lavoro", "nota.txt")));
        Assert.False(reader.IsInInbox(Path.Combine(_archive.Root, "Lavoro", "Documenti", "offerta.docx")));
    }

    [Fact]
    public void Shortcuts_AreShownWithoutExtension()
    {
        Assert.Equal("Blocco note", new ArchiveItem(@"C:\a\Blocco note.lnk", "Blocco note.lnk", IsFolder: false).DisplayName);
        Assert.Equal("Portale", new ArchiveItem(@"C:\a\Portale.url", "Portale.url", IsFolder: false).DisplayName);
        Assert.Equal("Offerta.docx", new ArchiveItem(@"C:\a\Offerta.docx", "Offerta.docx", IsFolder: false).DisplayName);
    }
}
