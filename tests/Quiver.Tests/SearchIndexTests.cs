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
    public void FindsFolders()
    {
        _archive.AddFolder("Lavoro/Commessa 2026");

        var hit = Assert.Single(SearchIndex.Build(_archive.Root).Search("commessa"));

        Assert.True(hit.Item.IsFolder);
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
}
