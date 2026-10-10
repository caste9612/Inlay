using Inlay.Core;

namespace Inlay.Tests;

public sealed class TransferRulesTests
{
    [Fact]
    public void OnTheSameDrive_ItemsAreMoved() =>
        Assert.Equal(TransferKind.Move, TransferRules.Choose([@"D:\Archivio\A\file.txt"], @"D:\Archivio\B", false, false));

    [Fact]
    public void FromAnotherDrive_ItemsAreCopied() =>
        Assert.Equal(TransferKind.Copy, TransferRules.Choose([@"C:\Users\me\Downloads\file.txt"], @"D:\Archivio\B", false, false));

    [Fact]
    public void CtrlCopies_AndShiftMoves()
    {
        Assert.Equal(TransferKind.Copy, TransferRules.Choose([@"D:\Archivio\A\file.txt"], @"D:\Archivio\B", forceCopy: true, forceMove: false));
        Assert.Equal(TransferKind.Move, TransferRules.Choose([@"C:\Temp\file.txt"], @"D:\Archivio\B", forceCopy: false, forceMove: true));
    }

    [Fact]
    public void AFolderCannotGoInsideItself()
    {
        Assert.Equal(TransferKind.None, TransferRules.Choose([@"D:\Archivio\A"], @"D:\Archivio\A", false, false));
        Assert.Equal(TransferKind.None, TransferRules.Choose([@"D:\Archivio\A"], @"D:\Archivio\A\Sotto", false, false));
    }

    [Fact]
    public void DroppingOntoTheFolderItIsAlreadyIn_DoesNothing() =>
        Assert.Equal(TransferKind.None, TransferRules.Choose([@"D:\Archivio\A\file.txt"], @"D:\Archivio\A\", forceCopy: true, forceMove: false));

    [Fact]
    public void ADriveRootContainsItsFolders()
    {
        Assert.True(TransferRules.IsSameOrInside(@"D:\Archivio", @"D:\"));
        Assert.False(TransferRules.IsSameOrInside(@"E:\Archivio", @"D:\"));
    }

    [Fact]
    public void ASimilarNameIsNotTheSameFolder() =>
        Assert.Equal(TransferKind.Move, TransferRules.Choose([@"D:\Archivio\A"], @"D:\Archivio\AB", false, false));

    [Fact]
    public void AltMakesAShortcut_AndSoDoCtrlAndShiftTogether()
    {
        Assert.Equal(TransferKind.Link, TransferRules.Choose([@"D:\Archivio\A\file.txt"], @"D:\Archivio\B", false, false, forceLink: true));
        Assert.Equal(TransferKind.Link, TransferRules.Choose([@"D:\Archivio\A\file.txt"], @"D:\Archivio\B", forceCopy: true, forceMove: true));
    }

    [Fact]
    public void InstalledPrograms_AndWhatIsOnTheNetwork_HaveAPlaceOfTheirOwn()
    {
        Assert.True(TransferRules.HasItsOwnPlace(@"C:\Program Files\App\app.exe", @"D:\Archivio"));
        Assert.True(TransferRules.HasItsOwnPlace(@"\\server\commesse\2026\Rossi", @"D:\Archivio"));
        Assert.False(TransferRules.HasItsOwnPlace(@"C:\Users\me\Downloads\offerta.pdf", @"D:\Archivio"));
        // A program kept in the archive, like a portable one, is moved around like any other file.
        Assert.False(TransferRules.HasItsOwnPlace(@"D:\Archivio\Strumenti\portable.exe", @"D:\Archivio"));
    }

    [Fact]
    public void WhatHasItsOwnPlace_EntersAsAShortcut_UnlessAKeySaysOtherwise()
    {
        string[] program = [@"C:\Program Files\App\app.exe"];
        Assert.Equal(TransferKind.Link, TransferRules.Choose(program, @"C:\Archivio\Programmi", false, false, preferLink: true));
        Assert.Equal(TransferKind.Copy, TransferRules.Choose(program, @"C:\Archivio\Programmi", forceCopy: true, forceMove: false, preferLink: true));
        Assert.Equal(TransferKind.Move, TransferRules.Choose(program, @"C:\Archivio\Programmi", forceCopy: false, forceMove: true, preferLink: true));
    }
}
