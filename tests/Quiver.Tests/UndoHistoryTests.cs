using Quiver.Core;

namespace Quiver.Tests;

/// <summary>The steps Ctrl+Z takes back, newest first: by when their operations were asked for.</summary>
public sealed class UndoHistoryTests
{
    [Fact]
    public void Steps_KeepTheOrderTheirOperationsWereAskedFor_NotThatTheyEnded()
    {
        var history = new UndoHistory();
        var copy = new CopyStep([@"D:\Archivio\Lavoro\Foto"]);
        var move = new MoveStep([new MovedItem(@"D:\Archivio\b.txt", @"D:\Archivio\Personale\b.txt")], MetadataSnapshot.Empty);

        // The long copy was asked for first, and ended after the move.
        history.Record(move, order: 2);
        history.Record(copy, order: 1);

        Assert.Same(move, history.Last);
        Assert.Equal(2, history.OrderOf(move));
        Assert.True(history.Remove(move));
        Assert.Same(copy, history.Last);
        Assert.False(history.Remove(move));
        Assert.Null(history.OrderOf(move));
    }

    [Fact]
    public void AStep_IsThatOneAndNoOtherEqualToIt_AndAReplacementTakesItsTurn()
    {
        var history = new UndoHistory();
        var first = new CreateStep(@"D:\Archivio\Lavoro\Nuovo riquadro");
        var same = new CreateStep(@"D:\Archivio\Lavoro\Nuovo riquadro");
        var later = new RenameStep(@"D:\Archivio\a.txt", @"D:\Archivio\b.txt");
        history.Record(first, order: 1);
        history.Record(same, order: 2);
        history.Record(later, order: 3);

        Assert.True(history.Remove(same));
        Assert.Equal(1, history.OrderOf(first));

        var replacement = new CreateStep(@"D:\Archivio\Lavoro\Altro");
        Assert.True(history.Replace(first, replacement));
        Assert.Equal(1, history.OrderOf(replacement));
        Assert.Same(later, history.Last);
    }
}
