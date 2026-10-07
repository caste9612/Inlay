using Quiver.Core;

namespace Quiver.Tests;

public sealed class ArchiveNamesTests
{
    [Theory]
    [InlineData("Rossi")]
    [InlineData("Commessa 2026")]
    [InlineData("Offerta_v3.docx")]
    [InlineData("Perché")]
    [InlineData("CONTRATTI")]
    public void AcceptsOrdinaryNames(string name) => Assert.Null(ArchiveNames.Validate(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("domanda?")]
    [InlineData("fine.")]
    [InlineData("fine ")]
    [InlineData(".nascosto")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("com1")]
    public void RejectsNamesWindowsOrQuiverCannotUse(string name) => Assert.NotNull(ArchiveNames.Validate(name));
}
