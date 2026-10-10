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
    public void KnowsWhatTheArchiveTakes_WithoutItsHiddenFiles()
    {
        _archive.AddFile("Lavoro/Offerta.docx", new string('a', 1000));
        _archive.AddFile("Lavoro/Rossi/Pianta.dwg", new string('b', 234));
        _archive.AddFile(".quiver/archive.json", new string('c', 50));

        var index = SearchIndex.Build(_archive.Root);

        Assert.Equal(1234, index.ArchiveBytes);
        Assert.Equal(4, index.ArchiveCount);
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

    // ---- Filters: "tipo:" and "in:" ----

    [Fact]
    public void Filters_NarrowTheWords_ByKindAndByPlace_AndOneOfEachSortIsEnough()
    {
        _archive.AddFile("Lavoro/Clienti/Rossi/Offerta.pdf");
        _archive.AddFile("Lavoro/Clienti/Rossi/Offerta.docx");
        _archive.AddFile("Lavoro/Interni/Offerta tipo.docx");
        _archive.AddFile("Personale/Offerta.pdf");
        _archive.AddFolder("Lavoro/Offerte");
        var index = SearchIndex.Build(_archive.Root);

        Assert.Equal(["Offerta.pdf", "Offerta.pdf"], index.Search("offerta tipo:pdf").Select(hit => hit.Item.Name));
        Assert.Equal([Path.Combine("Lavoro", "Clienti", "Rossi")], index.Search("offerta tipo:pdf in:lavoro").Select(hit => hit.RelativeDirectory));
        Assert.Equal(3, index.Search("offerta tipo:pdf tipo:documenti in:lavoro").Count);
        Assert.Equal(["Offerte"], index.Search("off tipo:cartelle").Select(hit => hit.Item.Name));
    }

    [Fact]
    public void APlace_IsAWholeFolderName_AtAnyDepth_OrAPathOfThem()
    {
        _archive.AddFile("Lavoro/Clienti/Rossi/Offerta.pdf");
        _archive.AddFile("Lavoro vecchio/Offerta.pdf");
        _archive.AddFile("Personale/Clienti fedeli/Offerta.pdf");
        var index = SearchIndex.Build(_archive.Root);

        Assert.Single(index.Search("offerta in:Lavoro"));
        Assert.Single(index.Search("offerta in:rossi"));
        Assert.Single(index.Search("offerta in:\"lavoro › clienti\""));
        Assert.Empty(index.Search("offerta in:client"));
        Assert.Equal(2, index.Search("offerta in:lavoro in:\"Lavoro vecchio\"").Count);
    }

    [Fact]
    public void APlaceFromTheRoot_LeavesOutTheFoldersWithItsNameDeeperDown()
    {
        _archive.AddFile("Lavoro/Offerta.pdf");
        _archive.AddFile("Personale/Lavoro/Offerta.pdf");
        var index = SearchIndex.Build(_archive.Root);

        Assert.Equal(2, index.Search("offerta in:lavoro").Count);
        Assert.Equal(["Lavoro"], index.Search("offerta in:/lavoro").Select(hit => hit.RelativeDirectory));
        Assert.Equal([Path.Combine("Personale", "Lavoro")], index.Search("offerta in:/personale/lavoro").Select(hit => hit.RelativeDirectory));
    }

    [Fact]
    public void FiltersWithoutWords_ListWhatPassesThem_TheNewestFirst()
    {
        var older = _archive.AddFile("Lavoro/Vecchio.pdf");
        var newer = _archive.AddFile("Personale/Nuovo.pdf");
        _archive.AddFile("Lavoro/Nota.txt");
        File.SetLastWriteTime(older, new DateTime(2025, 1, 1));
        File.SetLastWriteTime(newer, new DateTime(2026, 1, 1));

        Assert.Equal(["Nuovo.pdf", "Vecchio.pdf"], SearchIndex.Build(_archive.Root).Search("tipo:pdf").Select(hit => hit.Item.Name));
    }

    [Fact]
    public void Shortcuts_AreAKindOfTheirOwn_AndAnExtensionFindsItsFiles()
    {
        _archive.AddFile("Strumenti/Blocco note.lnk");
        _archive.AddFile("Strumenti/Portale.url");
        _archive.AddFile("Lavoro/Pianta.dwg");
        _archive.AddFile("Lavoro/Pianta.pdf");
        var index = SearchIndex.Build(_archive.Root);

        Assert.Equal(["Blocco note.lnk", "Portale.url"], index.Search("tipo:collegamenti").Select(hit => hit.Item.Name).Order(StringComparer.Ordinal));
        Assert.Equal(["Pianta.dwg"], index.Search("pianta tipo:dwg").Select(hit => hit.Item.Name));
    }
}
