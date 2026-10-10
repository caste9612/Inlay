using Inlay.Core;

namespace Inlay.Tests;

public sealed class SearchTextTests
{
    [Fact]
    public void TheStartOfTheName_BeatsAWordInside_WhichBeatsTheMiddleOfAWord_WhichBeatsThePath()
    {
        var words = SearchText.Words("Ross");

        var atStart = SearchText.Score(SearchText.Normalize("Rossi"), "", words);
        var atWord = SearchText.Score(SearchText.Normalize("Cliente Rossi"), "", words);
        var inside = SearchText.Score(SearchText.Normalize("Grossi"), "", words);
        var inPath = SearchText.Score(SearchText.Normalize("Offerta"), SearchText.Normalize("Lavoro/Rossi"), words);
        var nowhere = SearchText.Score(SearchText.Normalize("Bianchi"), SearchText.Normalize("Lavoro"), words);

        Assert.True(atStart > atWord && atWord > inside && inside > inPath && inPath > 0);
        Assert.Equal(0, nowhere);
    }

    [Fact]
    public void EveryWordMustMatch_AccentsAndCaseAside()
    {
        var words = SearchText.Words("perché LISBONA");

        Assert.True(SearchText.Score(SearchText.Normalize("Perche andare a Lisbona.md"), "", words) > 0);
        Assert.Equal(0, SearchText.Score(SearchText.Normalize("Perché andare a Porto.md"), "", words));
        Assert.Empty(SearchText.Words("   "));
    }
}
