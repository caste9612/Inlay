using Inlay.Core;

namespace Inlay.Tests;

/// <summary>What is typed in the search box: words, and the filters "tipo:" and "in:".</summary>
public sealed class SearchQueryTests
{
    [Fact]
    public void WordsAndFilters_AreToldApart_WithoutCaseOrAccents_AndQuotesKeepSpaces()
    {
        var query = SearchQuery.Parse("Offerta  Tipo:PDF in:\"Foto vacanze\" Perché");

        Assert.Equal(["offerta", "perche"], query.Words);
        Assert.Equal(["pdf"], query.Kinds.Select(kind => kind.Name));
        Assert.Equal(["Foto vacanze"], query.Places);
        Assert.True(query.HasFilters);
    }

    [Fact]
    public void AKind_IsFoundByTheStartOfItsName_OrOfAnotherWayToSayIt_OtherwiseItIsAnExtension()
    {
        Assert.Equal(["immagini"], SearchQuery.Parse("tipo:imm").Kinds.Select(kind => kind.Name));
        Assert.Equal(["immagini"], SearchQuery.Parse("tipo:foto").Kinds.Select(kind => kind.Name));
        Assert.Equal(["fogli"], SearchQuery.Parse("tipo:excel").Kinds.Select(kind => kind.Name));
        // Halfway through the word, every kind it can still become: "programmi" are shortcuts.
        Assert.Equal(["pdf", "presentazioni", "collegamenti", "posta"], SearchQuery.Parse("tipo:p").Kinds.Select(kind => kind.Name));
        var drawings = SearchQuery.Parse("tipo:.DWG");
        Assert.Empty(drawings.Kinds);
        Assert.Equal(["dwg"], drawings.Extensions);
    }

    [Fact]
    public void AFilterStillBeingWritten_FiltersNothing_AndOtherColonsAreWords()
    {
        var query = SearchQuery.Parse("tipo: in: riunione ore:10");

        Assert.False(query.HasFilters);
        Assert.Equal(["riunione", "ore:10"], query.Words);
        Assert.True(SearchQuery.Parse("   ").IsEmpty);
    }

    [Fact]
    public void AKindFilter_IsAddedAndTakenAway_LeavingTheWordsAsTheyAre()
    {
        var pdf = SearchQuery.Parse("tipo:pdf").Kinds.Single();

        var added = SearchQuery.ToggleKind("Offerta Rossi ", pdf);
        Assert.Equal("Offerta Rossi tipo:pdf", added);
        Assert.Equal("Offerta Rossi", SearchQuery.ToggleKind(added, pdf));
        // Written in another way, it goes all the same.
        Assert.Equal("Offerta", SearchQuery.ToggleKind("Offerta TIPO:Pdf", pdf));
        Assert.Equal("tipo:pdf", SearchQuery.ToggleKind("", pdf));
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
        var pdf = SearchQuery.Parse("tipo:pdf").Kinds.Single();

        var text = SearchQuery.ToggleKind("in:\"Foto vacanze", pdf);

        Assert.Equal("in:\"Foto vacanze\" tipo:pdf", text);
        var query = SearchQuery.Parse(text);
        Assert.Equal(["Foto vacanze"], query.Places);
        Assert.Equal(["pdf"], query.Kinds.Select(kind => kind.Name));
        Assert.Equal("tipo:\"pd\" in:/Lavoro", SearchQuery.WithPlace("tipo:\"pd", "/Lavoro"));
    }

    [Fact]
    public void TakingAKindAway_FromAValueThatStoodForMore_LeavesTheOthersWrittenOut()
    {
        var posta = SearchQuery.Parse("tipo:posta").Kinds.Single();

        var text = SearchQuery.ToggleKind("offerta tipo:p", posta);

        Assert.Equal("offerta tipo:pdf tipo:presentazioni tipo:collegamenti", text);
        Assert.DoesNotContain(posta, SearchQuery.Parse(text).Kinds);
    }
}
