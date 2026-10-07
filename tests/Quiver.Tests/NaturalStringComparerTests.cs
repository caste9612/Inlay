using Quiver.Core;

namespace Quiver.Tests;

public sealed class NaturalStringComparerTests
{
    [Theory]
    [InlineData("Verbale 2", "Verbale 10")]
    [InlineData("file9.txt", "file10.txt")]
    [InlineData("a", "B")]
    [InlineData("Rev A", "Rev B")]
    [InlineData("2026", "2026 bis")]
    public void OrdersNamesLikeExplorer(string first, string second)
    {
        Assert.True(NaturalStringComparer.Instance.Compare(first, second) < 0);
        Assert.True(NaturalStringComparer.Instance.Compare(second, first) > 0);
    }

    [Fact]
    public void SortsAList()
    {
        var names = new List<string> { "Verbale 10", "verbale 1", "Verbale 2", "Allegato" };

        names.Sort(NaturalStringComparer.Instance);

        Assert.Equal(new[] { "Allegato", "verbale 1", "Verbale 2", "Verbale 10" }, names);
    }
}
