using Quiver.Core;

namespace Quiver.Tests;

public sealed class SearchIndexTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void FindsItemsInSubfolders_WithTheirLocation()
    {
        _archive.AddFile("Lavoro/Rossi/Documenti/Offerta_v3.docx");

        var hit = Assert.Single(SearchIndex.Build(_archive.Root).Search("offerta"));

        Assert.Equal("Offerta_v3.docx", hit.Item.Name);
        Assert.Equal(Path.Combine("Lavoro", "Rossi", "Documenti"), hit.RelativeDirectory);
    }

    [Fact]
    public void KnowsWhatTheArchiveTakes_WithoutItsHiddenFilesNorTheDesktop()
    {
        _archive.AddFile("Lavoro/Offerta.docx", new string('a', 1000));
        _archive.AddFile("Lavoro/Rossi/Pianta.dwg", new string('b', 234));
        _archive.AddFile(".quiver/archive.json", new string('c', 50));
        var desktop = Path.Combine(_archive.Root, "..", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(desktop);
        try
        {
            File.WriteAllText(Path.Combine(desktop, "Lasciato qui.txt"), new string('d', 70));

            var index = SearchIndex.Build(_archive.Root, [new InboxSource("Desktop", [desktop])]);

            Assert.Equal(1234, index.ArchiveBytes);
        }
        finally
        {
            Directory.Delete(desktop, recursive: true);
        }
    }

    [Fact]
    public void FindsFolders()
    {
        _archive.AddFolder("Lavoro/Commessa 2026");

        var hit = Assert.Single(SearchIndex.Build(_archive.Root).Search("commessa"));

        Assert.True(hit.Item.IsFolder);
    }

    [Fact]
    public void Results_KnowWhenTheyLastChanged()
    {
        var file = _archive.AddFile("Lavoro/Offerta.docx");
        var changed = new DateTime(2026, 10, 5, 14, 32, 0, DateTimeKind.Local);
        File.SetLastWriteTime(file, changed);

        var hit = Assert.Single(SearchIndex.Build(_archive.Root).Search("offerta"));

        Assert.Equal(changed, hit.Item.Modified);
    }

    [Fact]
    public void EveryWordMustMatch_InTheNameOrInTheFolderPath()
    {
        var rossi = _archive.AddFile("Rossi/Offerta.docx");
        _archive.AddFile("Bianchi/Offerta.docx");

        var hit = Assert.Single(SearchIndex.Build(_archive.Root).Search("rossi offerta"));

        Assert.Equal(rossi, hit.Item.FullPath);
    }

    [Fact]
    public void MatchesAtTheStartOfTheName_RankFirst_ThenAtTheStartOfAWord()
    {
        _archive.AddFile("Note/controfferta.txt");
        _archive.AddFile("Note/promemoria offerta.txt");
        _archive.AddFile("Note/Offerta.txt");

        var names = SearchIndex.Build(_archive.Root).Search("offerta").Select(h => h.Item.Name);

        Assert.Equal(new[] { "Offerta.txt", "promemoria offerta.txt", "controfferta.txt" }, names);
    }

    [Fact]
    public void IgnoresCaseAndAccents()
    {
        _archive.AddFile("Viaggi/Perché andare a Lisbona.md");

        Assert.Single(SearchIndex.Build(_archive.Root).Search("PERCHE lisbona"));
    }

    [Fact]
    public void SkipsHiddenEntries_AndQuiversOwnData()
    {
        _archive.AddFile(".quiver/archive.json");
        _archive.AddFile("Lavoro/.git/config");
        File.SetAttributes(_archive.AddFile("Lavoro/nascosto.txt"), FileAttributes.Hidden);
        _archive.AddFile("Lavoro/visibile.txt");

        var index = SearchIndex.Build(_archive.Root);

        Assert.Empty(index.Search("archive"));
        Assert.Empty(index.Search("config"));
        Assert.Empty(index.Search("nascosto"));
        Assert.Single(index.Search("visibile"));
    }

    [Fact]
    public void BlankQuery_FindsNothing()
    {
        _archive.AddFile("Lavoro/file.txt");

        Assert.Empty(SearchIndex.Build(_archive.Root).Search("   "));
    }

    [Fact]
    public void ResultsAreCapped()
    {
        for (var i = 0; i < 30; i++)
            _archive.AddFile($"Lavoro/file{i}.txt");

        Assert.Equal(10, SearchIndex.Build(_archive.Root).Search("file", maxResults: 10).Count);
    }

    [Fact]
    public void TheDesktop_IsSearchedToo_ButOnlyWhatItsTileShows()
    {
        // The archive is a folder on the Desktop, which also holds a project with its own files.
        var desktop = _archive.AddFolder("Desktop");
        var root = _archive.AddFolder("Desktop/Archivio");
        _archive.AddFile("Desktop/preventivo.pdf");
        _archive.AddFile("Desktop/Progetto/preventivo vecchio.pdf");
        _archive.AddFile("Desktop/Archivio/Lavoro/preventivo firmato.pdf");

        var index = SearchIndex.Build(root, [new InboxSource("Desktop", [desktop])]);

        Assert.Equal(new[] { "preventivo firmato.pdf", "preventivo.pdf" },
            index.Search("preventivo").Select(hit => hit.Item.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Desktop", Assert.Single(index.Search("preventivo.pdf")).RelativeDirectory);
        Assert.Single(index.Search("progetto"));
        Assert.Empty(index.Search("archivio"));
    }
}
