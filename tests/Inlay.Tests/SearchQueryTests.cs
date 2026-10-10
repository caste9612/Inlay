using Inlay.Core;

namespace Inlay.Tests;

/// <summary>What is typed in the search box: words, and the filters "type:" and "in:".</summary>
public sealed class SearchQueryTests
{
    [Fact]
    public void WordsAndFilters_AreToldApart_WithoutCaseOrAccents_AndQuotesKeepSpaces()
    {
        var query = SearchQuery.Parse("Offerta  Type:PDF in:\"Foto vacanze\" Perché");

        Assert.Equal(["offerta", "perche"], query.Words);
        Assert.Equal(["pdf"], query.Kinds.Select(kind => kind.Name));
        Assert.Equal(["Foto vacanze"], query.Places);
        Assert.True(query.HasFilters);
    }

    [Fact]
    public void AKind_IsFoundByTheStartOfItsName_OrOfAnotherWayToSayIt_OtherwiseItIsAnExtension()
    {
        Assert.Equal(["images"], SearchQuery.Parse("type:ima").Kinds.Select(kind => kind.Name));
        Assert.Equal(["images"], SearchQuery.Parse("type:photo").Kinds.Select(kind => kind.Name));
        Assert.Equal(["spreadsheets"], SearchQuery.Parse("type:excel").Kinds.Select(kind => kind.Name));
        // Halfway through the word, every kind it can still become: "photos" are images, "programs" shortcuts.
        Assert.Equal(["pdf", "presentations", "images", "shortcuts"], SearchQuery.Parse("type:p").Kinds.Select(kind => kind.Name));
        var drawings = SearchQuery.Parse("type:.DWG");
        Assert.Empty(drawings.Kinds);
        Assert.Equal(["dwg"], drawings.Extensions);
    }

    [Fact]
    public void AFilterStillBeingWritten_FiltersNothing_AndOtherColonsAreWords()
    {
        var query = SearchQuery.Parse("type: in: riunione ore:10");

        Assert.False(query.HasFilters);
        Assert.Equal(["riunione", "ore:10"], query.Words);
        Assert.True(SearchQuery.Parse("   ").IsEmpty);
    }

    [Fact]
    public void AKindFilter_IsAddedAndTakenAway_LeavingTheWordsAsTheyAre()
    {
        var pdf = SearchQuery.Parse("type:pdf").Kinds.Single();

        var added = SearchQuery.ToggleKind("Offerta Rossi ", pdf);
        Assert.Equal("Offerta Rossi type:pdf", added);
        Assert.Equal("Offerta Rossi", SearchQuery.ToggleKind(added, pdf));
        // Written in another way, it goes all the same.
        Assert.Equal("Offerta", SearchQuery.ToggleKind("Offerta TYPE:Pdf", pdf));
        Assert.Equal("type:pdf", SearchQuery.ToggleKind("", pdf));
    }

    [Fact]
    public void APlace_TakesThePlaceOfThoseWrittenBefore_InQuotesWhenItHasSpaces()
    {
        Assert.Equal("offerta in:/Lavoro/Clienti", SearchQuery.WithPlace("offerta in:Personale", "/Lavoro/Clienti"));
        Assert.Equal("in:\"Foto vacanze\"", SearchQuery.WithPlace("", "Foto vacanze"));
        Assert.Equal("offerta", SearchQuery.WithPlace("offerta in:\"Foto vacanze\"", null));
        Assert.True(SearchQuery.SamePlace("Lavoro/Clienti", "lavoro › clienti"));
        // From the root it is another place: a folder with that name deeper down does not count.
        Assert.False(SearchQuery.SamePlace("/Lavoro", "Lavoro"));
        Assert.Equal("Lavoro › Clienti", SearchQuery.Display("/Lavoro/Clienti"));
    }

    [Fact]
    public void AFilterAddedAfterAQuoteLeftOpen_ClosesItFirst()
    {
        var pdf = SearchQuery.Parse("type:pdf").Kinds.Single();

        var text = SearchQuery.ToggleKind("in:\"Foto vacanze", pdf);

        Assert.Equal("in:\"Foto vacanze\" type:pdf", text);
        var query = SearchQuery.Parse(text);
        Assert.Equal(["Foto vacanze"], query.Places);
        Assert.Equal(["pdf"], query.Kinds.Select(kind => kind.Name));
        Assert.Equal("type:\"pd\" in:/Lavoro", SearchQuery.WithPlace("type:\"pd", "/Lavoro"));
    }

    [Fact]
    public void TakingAKindAway_FromAValueThatStoodForMore_LeavesTheOthersWrittenOut()
    {
        var images = SearchQuery.Parse("type:images").Kinds.Single();

        var text = SearchQuery.ToggleKind("offerta type:p", images);

        Assert.Equal("offerta type:pdf type:presentations type:shortcuts", text);
        Assert.DoesNotContain(images, SearchQuery.Parse(text).Kinds);
    }
}
