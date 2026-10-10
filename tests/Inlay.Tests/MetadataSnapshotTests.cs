using Inlay.Core;

namespace Inlay.Tests;

/// <summary>Ctrl+Z puts metadata back with snapshots: a folder moved back must take its topic mark and its place along.</summary>
public sealed class MetadataSnapshotTests
{
    [Fact]
    public void AFolderMovedBack_TakesItsEntriesAlong_AndLeavesNothingWhereItWas()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("Lavoro/Clienti", true);
        metadata.SetTopic("Lavoro/Clienti/Rossi", true);
        metadata.SetOrder(["Lavoro/Clienti/Rossi/Documenti"]);
        var before = metadata.Snapshot(["Lavoro/Clienti", "Archivio/Clienti"]);

        metadata.MoveFolder("Lavoro/Clienti", "Archivio/Clienti");
        Assert.True(metadata.Restore(before));

        Assert.True(metadata.IsTopic("Lavoro/Clienti"));
        Assert.True(metadata.IsTopic("Lavoro/Clienti/Rossi"));
        Assert.Equal(0, metadata.OrderOf("Lavoro/Clienti/Rossi/Documenti"));
        Assert.DoesNotContain(metadata.Folders.Keys, key => key.StartsWith("Archivio", StringComparison.Ordinal));
    }

    [Fact]
    public void ASnapshot_IsACopy_LaterChangesDoNotTouchIt()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetOrder(["A/B", "A/C"]);
        var before = metadata.Snapshot(["A/B", "A/C"]);

        metadata.SetOrder(["A/C", "A/B"]);
        metadata.Restore(before);

        Assert.Equal(0, metadata.OrderOf("A/B"));
        Assert.Equal(1, metadata.OrderOf("A/C"));
    }

    [Fact]
    public void RestoringWhatIsAlreadyThere_ChangesNothing()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("A/B", true);
        var before = metadata.Snapshot(["A/B", "A/X"]);

        Assert.False(metadata.Restore(before));
    }

    [Fact]
    public void ARestrictedSnapshot_PutsBackOnlyItsFolders()
    {
        var metadata = new ArchiveMetadata();
        metadata.SetTopic("A/B", true);
        metadata.SetTopic("A/C", true);
        var before = metadata.Snapshot(["A/B", "A/C"]);
        metadata.RemoveFolder("A/B");
        metadata.RemoveFolder("A/C");

        metadata.Restore(before.Restrict(["a/b"]));

        Assert.True(metadata.IsTopic("A/B"));
        Assert.False(metadata.IsTopic("A/C"));
    }

    [Fact]
    public void ANewFolder_Undone_LosesTheTopicMarkItGot()
    {
        var metadata = new ArchiveMetadata();
        var before = metadata.Snapshot(["Lavoro/Nuovo argomento"]);
        metadata.SetTopic("Lavoro/Nuovo argomento", true);

        metadata.Restore(before);

        Assert.Empty(metadata.Folders);
    }
}
