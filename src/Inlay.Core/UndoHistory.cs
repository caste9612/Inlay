namespace Inlay.Core;

/// <summary>
/// What Inlay did to the files, newest last, so that Ctrl+Z can take it back one step at a time, as in Explorer.
/// Each step remembers where things went: undoing it checks that they are still there and leaves alone what changed since.
/// The newest is the one asked for last: operations on different files run together, and a long copy that ends after a
/// quick move asked later does not take its place.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>How many steps back Ctrl+Z can go.</summary>
    public const int Capacity = 50;

    // Oldest first, by when their operations were asked for.
    private readonly List<Entry> _steps = [];

    public UndoStep? Last => _steps.Count > 0 ? _steps[^1].Step : null;

    /// <summary>Keeps a step in the place of its operation among the others.</summary>
    /// <param name="order">When its operation was asked for: a number that grows with each one.</param>
    public void Record(UndoStep step, long order)
    {
        var index = _steps.Count;
        while (index > 0 && _steps[index - 1].Order > order)
            index--;
        _steps.Insert(index, new Entry(step, order));
        if (_steps.Count > Capacity)
            _steps.RemoveAt(0);
    }

    /// <summary>When the operation of a step was asked for; null when the step is not in the history.</summary>
    public long? OrderOf(UndoStep step) => IndexOf(step) is var index and >= 0 ? _steps[index].Order : null;

    /// <summary>Takes a step out, wherever it is: the one a toast offers to undo. False when it is no longer there.</summary>
    public bool Remove(UndoStep step)
    {
        var index = IndexOf(step);
        if (index < 0)
            return false;
        _steps.RemoveAt(index);
        return true;
    }

    /// <summary>Puts a step in the place of another, in its turn: a move that turns out to be that of a topic.</summary>
    public bool Replace(UndoStep step, UndoStep replacement)
    {
        var index = IndexOf(step);
        if (index < 0)
            return false;
        _steps[index] = _steps[index] with { Step = replacement };
        return true;
    }

    public void Clear() => _steps.Clear();

    // The same step, not an equal one: two moves of the same file are two steps.
    private int IndexOf(UndoStep step) => _steps.FindIndex(entry => ReferenceEquals(entry.Step, step));

    private sealed record Entry(UndoStep Step, long Order);
}

/// <summary>Something Inlay did, with what it takes to undo it.</summary>
public abstract record UndoStep;

/// <summary>Files and folders moved by dragging them or with cut and paste: undoing puts each one back where it was.</summary>
/// <param name="Forgotten">The metadata of the folders that left the archive, which they get back on their return.</param>
public sealed record MoveStep(IReadOnlyList<MovedItem> Items, MetadataSnapshot Forgotten) : UndoStep;

/// <param name="From">Where the item was.</param>
/// <param name="To">Where it went.</param>
/// <param name="BecameRow">
/// A folder that arrived in a topic and became a row of its «General»: put back, it is what it was before, like a tile.
/// </param>
public sealed record MovedItem(string From, string To, bool BecameRow = false);

/// <summary>Copies made by Inlay, also of what was dropped from other programs: undoing moves them to the Recycle Bin.</summary>
public sealed record CopyStep(IReadOnlyList<string> Copies) : UndoStep;

/// <summary>Shortcuts made by Inlay: undoing moves them to the Recycle Bin, and what they point to stays where it is.</summary>
public sealed record LinkStep(IReadOnlyList<string> Links) : UndoStep;

public sealed record RenameStep(string From, string To) : UndoStep;

/// <summary>A new tile, topic or folder: undoing removes it, as long as it is still empty.</summary>
public sealed record CreateStep(string Folder) : UndoStep;

/// <summary>Items moved to the Recycle Bin: undoing restores exactly those, even if others with the same path followed.</summary>
/// <param name="NotInBin">How many were deleted for good instead, like on a drive without a Recycle Bin.</param>
/// <param name="Forgotten">The metadata of the deleted folders, which they get back.</param>
public sealed record RecycleStep(IReadOnlyList<RecycledItem> Items, int NotInBin, MetadataSnapshot Forgotten) : UndoStep;

/// <summary>Tiles put in another order: undoing gives each tile the place it had.</summary>
/// <param name="PreviousOrder">By metadata key; null for tiles that were in name order.</param>
public sealed record TileOrderStep(IReadOnlyDictionary<string, int?> PreviousOrder) : UndoStep;

/// <summary>A topic dragged under another one: undoing moves its folder back, to the place it had among the topics there.</summary>
/// <param name="PreviousOrder">Its place before; null when it was in name order.</param>
public sealed record TopicMoveStep(MoveStep Move, int? PreviousOrder) : UndoStep;

/// <summary>Topics put in another order in the sidebar: undoing gives each the place it had.</summary>
/// <param name="PreviousOrder">By metadata key; null for topics that were in name order.</param>
public sealed record TopicOrderStep(IReadOnlyDictionary<string, int?> PreviousOrder) : UndoStep;

/// <summary>The items of a tile sorted, or put in order by hand: undoing gives the tile back the order it had.</summary>
/// <param name="Sort">The sort it had; null for by name.</param>
/// <param name="Items">The order by hand it had; null for none.</param>
public sealed record ArrangeStep(string Key, string Name, ItemSort? Sort, IReadOnlyList<string>? Items) : UndoStep;

/// <summary>A tile resized: undoing gives it back the size it had.</summary>
/// <param name="Previous">Null for the usual size.</param>
public sealed record SizeStep(string Key, string Name, TileSize? Previous) : UndoStep;

/// <summary>A tile promoted to topic, or a topic made a tile again: undoing turns it back.</summary>
public sealed record TopicStep(string Key, string Name, bool WasTopic) : UndoStep;

/// <summary>A row of «General» promoted to tile, or a tile put in «General» as a row: undoing turns it back.</summary>
public sealed record ItemStep(string Key, string Name, bool WasItem) : UndoStep;

/// <summary>A tile given a color, or none: undoing gives it back the color it had.</summary>
/// <param name="WasAutomatic">The color before was given by Inlay to a topic of the root, not chosen.</param>
public sealed record ColorStep(string Key, string Name, string? Previous, bool WasAutomatic = false) : UndoStep;
