using Inlay.Core;

namespace Inlay.Tests;

/// <summary>Which file operations can run at the same time, and which wait for the one before them.</summary>
public sealed class FileTouchesTests
{
    private const string Lavoro = @"D:\Archivio\Lavoro";

    [Fact]
    public void TransfersIntoOtherFolders_RunTogether_IntoTheSameFolder_OneAfterTheOther()
    {
        var intoDocuments = FileTouches.Transfer([@"E:\Foto"], $@"{Lavoro}\Documenti");
        var intoArchivio = FileTouches.Transfer([@"E:\Video"], $@"{Lavoro}\Archivio");
        var alsoIntoDocuments = FileTouches.Transfer([@"E:\Musica"], $@"{Lavoro}\Documenti\");

        Assert.False(intoDocuments.Overlaps(intoArchivio));
        // Both add names there: those of each copy could no longer be told apart.
        Assert.True(intoDocuments.Overlaps(alsoIntoDocuments));
        Assert.True(alsoIntoDocuments.Overlaps(intoDocuments));
    }

    [Fact]
    public void WhatArrivesSomewhere_WaitsBeforeGoingOnFromThere()
    {
        var first = FileTouches.Transfer([$@"{Lavoro}\Documenti\a.txt"], $@"{Lavoro}\Archivio");
        var second = FileTouches.Transfer([$@"{Lavoro}\Archivio\a.txt"], @"D:\Archivio\Personale\Casa");

        Assert.True(first.Overlaps(second));
    }

    [Fact]
    public void ACopyIntoATopic_LeavesItsTilesFree_ButNotTheFolderItLandsIn()
    {
        var copy = FileTouches.Transfer([@"E:\Foto"], Lavoro);
        var betweenTiles = FileTouches.Transfer([$@"{Lavoro}\Documenti\a.txt"], $@"{Lavoro}\Archivio");
        var outOfTheTopic = FileTouches.Transfer([$@"{Lavoro}\nota.txt"], @"D:\Archivio\Personale");
        var intoTheCopy = FileTouches.Transfer([@"E:\b.jpg"], $@"{Lavoro}\Foto");

        Assert.False(copy.Overlaps(betweenTiles));
        Assert.True(copy.Overlaps(outOfTheTopic));
        Assert.True(copy.Overlaps(intoTheCopy));
    }

    [Fact]
    public void ACopyNextToItsOriginal_WaitsForADeletionThere_WhoseNameItCouldTake()
    {
        // The shell names the copy «a - Copia.txt» once that name is free: it would look like the file deleted.
        var copy = FileTouches.Transfer([$@"{Lavoro}\a.txt"], Lavoro);
        var deletion = new FileTouches([$@"{Lavoro}\a - Copia.txt"]);

        Assert.True(copy.Overlaps(deletion));
        Assert.True(deletion.Overlaps(copy));
    }

    [Fact]
    public void DeletingOrMovingAFolder_WaitsForWhatGoesOnInside()
    {
        var deletion = new FileTouches([$@"{Lavoro}\Archivio"]);
        var intoIt = FileTouches.Transfer([@"E:\Foto"], $@"{Lavoro}\Archivio\2026");
        var fromIt = FileTouches.Transfer([$@"{Lavoro}\Archivio\vecchio.txt"], @"D:\Archivio\Personale");
        var elsewhere = FileTouches.Transfer([$@"{Lavoro}\Documenti\a.txt"], @"D:\Archivio\Personale");

        Assert.True(deletion.Overlaps(intoIt));
        Assert.True(intoIt.Overlaps(deletion));
        Assert.True(deletion.Overlaps(fromIt));
        Assert.False(deletion.Overlaps(elsewhere));
    }

    [Fact]
    public void Paths_AreComparedWholeAndWithoutCase()
    {
        var documents = new FileTouches([$@"{Lavoro}\Documenti"]);

        Assert.True(documents.Overlaps(new FileTouches([$@"{Lavoro.ToUpperInvariant()}\DOCUMENTI\a.txt"])));
        Assert.False(documents.Overlaps(new FileTouches([$@"{Lavoro}\Documenti vecchi"])));
    }
}
