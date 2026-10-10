using Inlay.Core;

namespace Inlay.Tests;

public sealed class FolderSizeTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void FilesAndFolders_EverythingInsideThemCounts_HiddenToo()
    {
        var file = _archive.AddFile("Offerta.txt", new string('a', 1000));
        _archive.AddFile("Commessa/Disegni/Pianta.txt", new string('b', 300));
        _archive.AddFile("Commessa/Note.txt", new string('c', 200));
        var hidden = _archive.AddFile("Commessa/.nascosto", new string('d', 50));
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Assert.Equal(1550, FolderSize.Of([file, Path.Combine(_archive.Root, "Commessa")]));
    }

    [Fact]
    public void WhatIsNotThere_CountsNothing() =>
        Assert.Equal(0, FolderSize.Of([Path.Combine(_archive.Root, "Sparito.txt")]));

    [Fact]
    public void AStoppedCount_SaysSo()
    {
        _archive.AddFile("Commessa/Note.txt", "x");
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderSize.Of([Path.Combine(_archive.Root, "Commessa")], stop.Token));
    }
}
