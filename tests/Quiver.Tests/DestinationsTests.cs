using Quiver.Core;

namespace Quiver.Tests;

/// <summary>Where «Sposta in…» can send files: the topics, for their loose files, and the tiles, in the order of the sidebar.</summary>
public sealed class DestinationsTests : IDisposable
{
    private readonly TempArchive _archive = new();

    public void Dispose() => _archive.Dispose();

    [Fact]
    public void Destinations_FollowTheSidebar_EachTopic_ThenItsTiles_ThenTheTopicsInsideIt()
    {
        _archive.AddFile("Lavoro/Clienti/Rossi/Documenti/Offerta.docx");
        _archive.AddFolder("Lavoro/Interni");
        _archive.AddFolder("Lavoro/Archivio");
        _archive.AddFolder("Personale");
        _archive.AddFolder(ArchiveLayout.InboxFolderName);
        _archive.AddFolder(".git");
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTileOrder(["Lavoro/Interni", "Lavoro/Archivio"]);

        var destinations = _archive.Reader(metadata).ReadDestinations();

        // Documenti is inside the tile Rossi: an item, not a place to send things to.
        Assert.Equal(["Lavoro", "Interni", "Archivio", "Clienti", "Rossi", "Personale"], destinations.Select(d => d.Title));
        Assert.Equal(["", "Lavoro", "Lavoro", "Lavoro", "Lavoro › Clienti", ""], destinations.Select(d => d.Location));
        Assert.Equal([true, false, false, true, false, true], destinations.Select(d => d.IsTopic));
        Assert.Equal("Lavoro/Clienti/Rossi", destinations[4].RelativeKey);
        Assert.Equal(Path.Combine(_archive.Root, "Lavoro", "Clienti", "Rossi"), destinations[4].FullPath);
    }

    [Fact]
    public void AnEmptyArchive_HasNowhereToSendThingsTo()
    {
        Assert.Empty(_archive.Reader().ReadDestinations());
    }
}
