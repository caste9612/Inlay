using Quiver.Core;

namespace Quiver.Tests;

public sealed class MetadataStoreTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void MissingFile_GivesEmptyMetadata() => Assert.Empty(MetadataStore.Load(_archive.Root).Folders);

    [Fact]
    public void SavedMetadata_IsReadBack_WithKeysIgnoringCase()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.Folders["Lavoro/Clienti"].Workspace = "3";
        MetadataStore.Save(_archive.Root, metadata);

        var loaded = MetadataStore.Load(_archive.Root);

        Assert.True(loaded.IsTopic("lavoro/clienti"));
        Assert.Equal("3", loaded.Folders["LAVORO/CLIENTI"].Workspace);
    }

    [Fact]
    public void Save_PutsTheFileInAHiddenFolder_WithTheDocumentedShape()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);

        MetadataStore.Save(_archive.Root, metadata);

        var folder = new DirectoryInfo(Path.Combine(_archive.Root, ArchiveLayout.MetadataFolderName));
        Assert.True(folder.Attributes.HasFlag(FileAttributes.Hidden));
        var json = File.ReadAllText(MetadataStore.GetPath(_archive.Root));
        Assert.Contains("\"folders\"", json);
        Assert.Contains("\"kind\": \"topic\"", json);
        Assert.DoesNotContain("\"workspace\"", json);
    }

    [Fact]
    public void Demoting_RemovesOnlyEntriesLeftEmpty()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("A/B", true);
        metadata.SetTopic("A/C", true);
        metadata.Folders["A/C"].Color = "#FF0000";

        metadata.SetTopic("A/B", false);
        metadata.SetTopic("A/C", false);

        Assert.False(metadata.Folders.ContainsKey("A/B"));
        Assert.False(metadata.IsTopic("A/C"));
        Assert.Equal("#FF0000", metadata.Folders["A/C"].Color);
    }

    [Fact]
    public void SetTopic_ReportsWhetherSomethingChanged()
    {
        var metadata = new ArchiveMetadata();

        Assert.True(metadata.SetTopic("A/B", true));
        Assert.False(metadata.SetTopic("a/b", true));
        Assert.True(metadata.SetTopic("A/B", false));
        Assert.False(metadata.SetTopic("A/B", false));
    }

    [Fact]
    public void MovingAFolder_CarriesItsEntriesAndThoseBelowIt()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);
        metadata.SetTopic("Lavoro/Clientela", true);

        Assert.True(metadata.MoveFolder("Lavoro/Clienti", "Archiviati/Clienti 2025"));

        Assert.Equal(
            new[] { "Archiviati/Clienti 2025", "Archiviati/Clienti 2025/Rossi", "Lavoro/Clientela" },
            metadata.Folders.Keys.Order());
    }

    [Fact]
    public void RemovingAFolder_ForgetsItsEntriesAndThoseBelowIt()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);
        metadata.SetTopic("Personale", true);

        Assert.True(metadata.RemoveFolder("lavoro/clienti"));
        Assert.False(metadata.RemoveFolder("Lavoro/Clienti"));

        Assert.Equal(new[] { "Personale" }, metadata.Folders.Keys);
    }

    [Fact]
    public void UnreadableFile_IsSetAside_AndEmptyMetadataIsUsed()
    {
        var path = _archive.AddFile($"{ArchiveLayout.MetadataFolderName}/{ArchiveLayout.MetadataFileName}", "{ not json");

        var loaded = MetadataStore.Load(_archive.Root);

        Assert.Empty(loaded.Folders);
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }
}
