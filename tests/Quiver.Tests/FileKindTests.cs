using Quiver.Core;

namespace Quiver.Tests;

public sealed class FileKindTests
{
    [Theory]
    [InlineData("Offerta.PDF", FileKind.Pdf)]
    [InlineData("Verbale riunione.docx", FileKind.Word)]
    [InlineData("Bollette 2026.csv", FileKind.Spreadsheet)]
    [InlineData("Presentazione.pptx", FileKind.Presentation)]
    [InlineData("Screenshot.png", FileKind.Image)]
    [InlineData("Note.md", FileKind.Text)]
    [InlineData("Archivio.7z", FileKind.Archive)]
    [InlineData("Risposta.eml", FileKind.Mail)]
    [InlineData("Portale.url", FileKind.Web)]
    [InlineData("Staffa.step", FileKind.Model3D)]
    public void CommonFiles_HaveTheirKind(string name, FileKind kind) =>
        Assert.Equal(kind, FileKinds.Of(new ArchiveItem(@"C:\a\" + name, name, IsFolder: false)));

    [Theory]
    [InlineData("Blocco note.lnk")]
    [InlineData("setup.exe")]
    [InlineData("Pianta.dwg")]
    [InlineData("senza estensione")]
    public void ProgramsShortcutsAndUnknownFiles_KeepTheIconOfWindows(string name) =>
        Assert.Equal(FileKind.Other, FileKinds.Of(new ArchiveItem(@"C:\a\" + name, name, IsFolder: false)));

    [Fact]
    public void AFolder_IsAFolder_WhateverItsName() =>
        Assert.Equal(FileKind.Folder, FileKinds.Of(new ArchiveItem(@"C:\a\Foto.zip", "Foto.zip", IsFolder: true)));
}
