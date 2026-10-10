using Inlay.Core;

namespace Inlay.Tests;

public sealed class WebShortcutTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Theory]
    [InlineData("https://portale.rossi.it/ordini", "https://portale.rossi.it/ordini")]
    [InlineData("www.rossi.it", "https://www.rossi.it/")]
    [InlineData("  \"rossi.it/contatti\"  ", "https://rossi.it/contatti")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void AddressesAreRecognized_AsInTheAddressBar(string typed, string expected) =>
        Assert.Equal(expected, WebShortcut.Parse(typed)?.AbsoluteUri);

    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"D:\Archivio")]
    [InlineData(@"\\server.studio.local\commesse")]
    [InlineData("offerta")]
    [InlineData("ftp://server/file")]
    [InlineData("")]
    public void PathsAndWords_AreNotWebsites(string typed) => Assert.Null(WebShortcut.Parse(typed));

    [Fact]
    public void AShortcut_IsNamedAfterTheSite_AndWrittenAsWindowsDoes()
    {
        var folder = _archive.AddFolder("Lavoro/Siti");

        var first = WebShortcut.Create(folder, WebShortcut.Parse("www.rossi.it/ordini")!);
        var second = WebShortcut.Create(folder, WebShortcut.Parse("rossi.it")!);

        Assert.Equal("rossi.it.url", Path.GetFileName(first));
        Assert.Equal("rossi.it (2).url", Path.GetFileName(second));
        Assert.Equal("[InternetShortcut]\r\nURL=https://www.rossi.it/ordini\r\n", File.ReadAllText(first));
        Assert.Equal("rossi.it", new ArchiveItem(first, Path.GetFileName(first), IsFolder: false).DisplayName);
    }
}
