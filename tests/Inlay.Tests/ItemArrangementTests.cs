using Inlay.Core;

namespace Inlay.Tests;

/// <summary>The order of the items of a tile: sorted as in Explorer, or as given by dragging them.</summary>
public sealed class ItemArrangementTests
{
    // In the order of the folder: folders first, then files, by name.
    private static readonly IReadOnlyList<ArchiveItem> Items =
    [
        Folder("Archivio", day: 9),
        Folder("Commessa", day: 3),
        File("Offerta.pdf", day: 5, size: 900),
        File("Preventivo.docx", day: 8, size: 40),
        File("Verbale 2.txt", day: 1, size: 300),
        File("Verbale 10.txt", day: 7, size: 300),
    ];

    [Fact]
    public void ByName_IsTheOrderOfTheFolder() =>
        Assert.Same(Items, ItemArrangement.Arrange(Items, new ItemSort(SortKey.Name, Descending: false), byHand: null));

    [Fact]
    public void TheNewestFirst_KeepsFoldersBeforeFiles()
    {
        var sorted = ItemArrangement.Arrange(Items, ItemSort.For(SortKey.Modified), byHand: null);

        Assert.Equal(["Archivio", "Commessa", "Preventivo.docx", "Verbale 10.txt", "Offerta.pdf", "Verbale 2.txt"], Names(sorted));
    }

    [Fact]
    public void TheLargestFirst_ThenByName_WhenTheSizeIsTheSame()
    {
        var sorted = ItemArrangement.Arrange(Items, ItemSort.For(SortKey.Size), byHand: null);

        Assert.Equal(["Archivio", "Commessa", "Offerta.pdf", "Verbale 2.txt", "Verbale 10.txt", "Preventivo.docx"], Names(sorted));
    }

    [Fact]
    public void ByType_GroupsTheExtensions_AndByNameFromZ_TurnsTheNamesAround()
    {
        Assert.Equal(["Archivio", "Commessa", "Preventivo.docx", "Offerta.pdf", "Verbale 2.txt", "Verbale 10.txt"],
            Names(ItemArrangement.Arrange(Items, ItemSort.For(SortKey.Type), byHand: null)));
        Assert.Equal(["Commessa", "Archivio", "Verbale 10.txt", "Verbale 2.txt", "Preventivo.docx", "Offerta.pdf"],
            Names(ItemArrangement.Arrange(Items, new ItemSort(SortKey.Name, Descending: true), byHand: null)));
    }

    [Fact]
    public void ByHand_KeepsThePlacesGiven_AndWhatArrivedSinceFollowsByName()
    {
        // "Bozza.txt" is gone since; "Preventivo.docx" and the folders were never placed.
        IReadOnlyList<string> byHand = ["verbale 10.txt", "Bozza.txt", "Offerta.pdf", "Verbale 2.txt"];

        var arranged = ItemArrangement.Arrange(Items, ItemSort.For(SortKey.Size), byHand);

        Assert.Equal(["Verbale 10.txt", "Offerta.pdf", "Verbale 2.txt", "Archivio", "Commessa", "Preventivo.docx"], Names(arranged));
    }

    private static List<string> Names(IEnumerable<ArchiveItem> items) => items.Select(item => item.Name).ToList();

    private static ArchiveItem Folder(string name, int day) =>
        new(@"C:\Archivio\Rossi\" + name, name, IsFolder: true, new DateTime(2026, 10, day));

    private static ArchiveItem File(string name, int day, long size) =>
        new(@"C:\Archivio\Rossi\" + name, name, IsFolder: false, new DateTime(2026, 10, day), size);
}
