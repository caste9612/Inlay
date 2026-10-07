using CommunityToolkit.Mvvm.ComponentModel;

namespace Quiver.App.ViewModels;

/// <summary>Something whose name is edited in place, as on the desktop: an item, a tile or a topic.</summary>
public abstract partial class RenameableViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = "";

    /// <summary>The file or folder a rename changes.</summary>
    public abstract string RenamePath { get; }

    protected abstract bool RenamesFolder { get; }

    /// <summary>An extension left out of the name being edited and kept on renaming, like .lnk for shortcuts.</summary>
    protected virtual string HiddenExtension => "";

    /// <summary>How much of the name starts selected: a file leaves its extension out, as in Explorer.</summary>
    public int EditSelectionLength =>
        RenamesFolder || HiddenExtension.Length > 0 || !Path.HasExtension(EditName)
            ? EditName.Length
            : Path.GetFileNameWithoutExtension(EditName).Length;

    /// <summary>The file name the typed name stands for; empty when nothing was typed.</summary>
    public string NewFileName => EditName.Trim() is { Length: > 0 } typed ? typed + HiddenExtension : "";

    public void BeginEdit()
    {
        var name = Path.GetFileName(RenamePath);
        EditName = name[..(name.Length - HiddenExtension.Length)];
        IsEditing = true;
    }
}
