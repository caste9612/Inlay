using Quiver.Core;

namespace Quiver.Tests;

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
}
