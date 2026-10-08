using Quiver.Core;

namespace Quiver.Tests;

/// <summary>The color of a topic: given once by Quiver, or chosen, and kept in the metadata of the archive.</summary>
public sealed class TopicColorTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void TheTopicsOfTheRoot_TakeTheLeastUsedColor_InTheirOrder_AndKeepIt()
    {
        var metadata = new ArchiveMetadata();
        string[] topics = ["Lavoro", "Personale", "Strumenti"];

        Assert.True(TopicColors.AssignMissing(metadata, topics));

        Assert.Equal(TopicColors.Automatic.Take(3), topics.Select(metadata.ColorOf));
        Assert.All(topics, topic => Assert.True(metadata.IsColorAutomatic(topic)));
        // Seen again, nothing changes: the colors stay theirs.
        Assert.False(TopicColors.AssignMissing(metadata, topics));

        // A new topic, in the middle of the order, takes a color nobody has yet.
        Assert.True(TopicColors.AssignMissing(metadata, ["Lavoro", "Casa", "Personale", "Strumenti"]));
        Assert.Equal(TopicColors.Automatic[3], metadata.ColorOf("Casa"));
        Assert.Equal(TopicColors.Automatic[1], metadata.ColorOf("Personale"));
    }

    [Fact]
    public void AColorChosen_OrNoneOnPurpose_IsLeftAsItIs()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetColor("Lavoro", TopicColors.Automatic[0]);
        metadata.SetColor("Personale", TopicColors.None);

        Assert.True(TopicColors.AssignMissing(metadata, ["Lavoro", "Personale", "Strumenti"]));

        Assert.False(metadata.IsColorAutomatic("Lavoro"));
        Assert.Equal(TopicColors.None, metadata.ColorOf("Personale"));
        // The first color is taken by Lavoro, so the next one.
        Assert.Equal(TopicColors.Automatic[1], metadata.ColorOf("Strumenti"));
    }

    [Fact]
    public void WhenThePaletteRunsOut_TheLeastUsedComesAgain_FirstInOrder()
    {
        Assert.Equal(TopicColors.Automatic[0], TopicColors.LeastUsed(TopicColors.Automatic));
        Assert.Equal(TopicColors.Automatic[1], TopicColors.LeastUsed([.. TopicColors.Automatic, TopicColors.Automatic[0]]));
        Assert.Equal(TopicColors.Automatic[0], TopicColors.LeastUsed([null, TopicColors.None, "#C3CAD3"]));
    }

    [Fact]
    public void TheMarkOfAColorGivenByQuiver_IsSaved_AndAnOlderFileWithoutItStillReads()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetColor("Lavoro", "#5DA9FF", automatic: true);
        MetadataStore.Save(_archive.Root, metadata);

        var json = File.ReadAllText(MetadataStore.GetPath(_archive.Root));
        Assert.Contains("\"autoColor\": true", json);
        Assert.True(MetadataStore.Load(_archive.Root).IsColorAutomatic("Lavoro"));

        // As Quiver 0.2.3 writes it again: the color without the mark, as if chosen.
        File.WriteAllText(MetadataStore.GetPath(_archive.Root), json.Replace(",\n      \"autoColor\": true", "").Replace(",\r\n      \"autoColor\": true", ""));
        var older = MetadataStore.Load(_archive.Root);
        Assert.Equal("#5DA9FF", older.ColorOf("Lavoro"));
        Assert.False(older.IsColorAutomatic("Lavoro"));
    }

    [Fact]
    public void ChoosingAColor_TakesTheMarkAway_AndNoColorAtAll_LeavesNoEntry()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetColor("Lavoro", "#5DA9FF", automatic: true);

        Assert.True(metadata.SetColor("Lavoro", "#5DA9FF"));
        Assert.False(metadata.IsColorAutomatic("Lavoro"));

        Assert.True(metadata.SetColor("Lavoro", null));
        Assert.Empty(metadata.Folders);
    }

    [Fact]
    public void TheSidebar_ReadsTheColors_AndATileDoesNotShowOneGivenByQuiver()
    {
        _archive.AddFolder("Lavoro/Clienti");
        _archive.AddFolder("Lavoro/Documenti");
        var metadata = new ArchiveMetadata();
        metadata.SetColor("Lavoro", "#5DA9FF", automatic: true);
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetColor("Lavoro/Clienti", "#FF7EB6");
        // Once a topic of the root, now a tile: its old color was not chosen for it.
        metadata.SetColor("Lavoro/Documenti", "#36CFB0", automatic: true);

        var reader = _archive.Reader(metadata);
        var lavoro = Assert.Single(reader.ReadTopics());

        Assert.Equal(("#5DA9FF", true), (lavoro.Color, lavoro.ColorIsAutomatic));
        Assert.Equal(("#FF7EB6", false), (lavoro.Children[0].Color, lavoro.Children[0].ColorIsAutomatic));
        Assert.Equal([null], reader.ReadBoard(lavoro).Tiles.Select(tile => tile.Color));
    }
}
