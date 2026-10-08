namespace Quiver.Core;

/// <summary>
/// What Quiver did to the files, newest last, so that Ctrl+Z can take it back one step at a time, as in Explorer.
/// Each step remembers where things went: undoing it checks that they are still there and leaves alone what changed since.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>How many steps back Ctrl+Z can go.</summary>
    public const int Capacity = 50;

    private readonly List<UndoStep> _steps = [];

    public UndoStep? Last => _steps.Count > 0 ? _steps[^1] : null;

    public void Record(UndoStep step)
    {
        _steps.Add(step);
        if (_steps.Count > Capacity)
            _steps.RemoveAt(0);
    }

    /// <summary>Takes the newest step out of the history; null when there is nothing to undo.</summary>
    public UndoStep? Pop()
    {
        if (_steps.Count == 0)
            return null;
        var step = _steps[^1];
        _steps.RemoveAt(_steps.Count - 1);
        return step;
    }

    public void Clear() => _steps.Clear();
}

/// <summary>Something Quiver did, with what it takes to undo it.</summary>
public abstract record UndoStep;

/// <summary>Files and folders moved by dragging them or with cut and paste: undoing puts each one back where it was.</summary>
/// <param name="Forgotten">The metadata of the folders that left the archive, which they get back on their return.</param>
public sealed record MoveStep(IReadOnlyList<MovedItem> Items, MetadataSnapshot Forgotten) : UndoStep;

/// <param name="From">Where the item was.</param>
/// <param name="To">Where it went.</param>
public sealed record MovedItem(string From, string To);

/// <summary>Copies made by Quiver, also of what was dropped from other programs: undoing moves them to the Recycle Bin.</summary>
public sealed record CopyStep(IReadOnlyList<string> Copies) : UndoStep;

/// <summary>Shortcuts made by Quiver: undoing moves them to the Recycle Bin, and what they point to stays where it is.</summary>
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

/// <summary>A tile promoted to topic, or a topic made a tile again: undoing turns it back.</summary>
public sealed record TopicStep(string Key, string Name, bool WasTopic) : UndoStep;

/// <summary>A tile given a color, or none: undoing gives it back the color it had.</summary>
public sealed record ColorStep(string Key, string Name, string? Previous) : UndoStep;
