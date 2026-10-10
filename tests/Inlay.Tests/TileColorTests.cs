using Inlay.Core;

namespace Inlay.Tests;

/// <summary>The color of a tile: in the metadata, with the archive, and on the board.</summary>
public sealed class TileColorTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void AColor_IsKept_ReadBack_AndTakenAway()
    {
        var metadata = new ArchiveMetadata();
        Assert.False(metadata.SetColor("Lavoro/Documenti", null));
        Assert.True(metadata.SetColor("Lavoro/Documenti", "#FFA133"));
        Assert.False(metadata.SetColor("Lavoro/Documenti", "#ffa133"));
        Assert.Equal("#FFA133", metadata.ColorOf("lavoro/documenti"));

        MetadataStore.Save(_archive.Root, metadata);
        Assert.Equal("#FFA133", MetadataStore.Load(_archive.Root).ColorOf("Lavoro/Documenti"));

        Assert.True(metadata.SetColor("Lavoro/Documenti", null));
        Assert.Empty(metadata.Folders);
    }

    [Fact]
    public void TheBoard_ShowsTheColorOfEachTile()
    {
        _archive.AddFile("Lavoro/Documenti/a.txt");
        _archive.AddFolder("Lavoro/Archivio");
        var metadata = new ArchiveMetadata();
        metadata.SetColor("Lavoro/Documenti", "#5DA9FF");

        var reader = _archive.Reader(metadata);
        var board = reader.ReadBoard(Assert.Single(reader.ReadTopics()));

        Assert.Equal([null, "#5DA9FF"], board.Tiles.Select(tile => tile.Color));
    }
}
