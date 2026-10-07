using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.App.Services;
using Quiver.App.Views;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A file, folder or shortcut inside a tile.</summary>
public partial class ItemViewModel(ArchiveItem item) : RenameableViewModel
{
    private ImageSource? _icon;
    private bool _iconLoaded;
    private Geometry? _glyph;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Cut with Ctrl+X and not pasted yet: shown faded, as in Explorer.</summary>
    [ObservableProperty]
    private bool _isCut;

    public ArchiveItem Item { get; } = item;

    public string Name => Item.DisplayName;

    public string DateText => DateLabel.For(Item.Modified);

    public string ToolTip => Item.Modified is null ? Item.FullPath : $"{Item.FullPath}\nModificato: {DateLabel.Full(Item.Modified)}";

    public override string RenamePath => Item.FullPath;

    protected override bool RenamesFolder => Item.IsFolder;

    // As in Explorer, a shortcut is renamed without seeing its .lnk or .url.
    protected override string HiddenExtension => Name == Item.Name ? "" : Path.GetExtension(Item.Name);

    public FileKind Kind { get; } = FileKinds.Of(item);

    /// <summary>
    /// The icon of Brace for folders, the common kinds of file and those no program opens; null for programs, shortcuts
    /// and the files of an installed program, which keep the icon of Windows.
    /// </summary>
    public Geometry? Glyph => _glyph ??= FileGlyphs.For(Kind) ??
        (Kind == FileKind.Other && !FileAssociations.IsKnown(Path.GetExtension(Item.Name)) ? FileGlyphs.Unknown : null);

    /// <summary>
    /// The icon of Windows, for programs, shortcuts and the other files: for them it is a logo. Loaded on first use, so only
    /// the items actually displayed pay for it.
    /// </summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _icon = Glyph is null ? ShellIcons.Get(Item.FullPath, Item.IsFolder) : null;
                _iconLoaded = true;
            }
            return _icon;
        }
    }
}

/// <summary>A search result: an item plus where it lives.</summary>
public sealed class SearchResultViewModel(SearchHit hit) : ItemViewModel(hit.Item)
{
    /// <summary>Like "Lavoro › Clienti › Rossi".</summary>
    public string Location { get; } = hit.RelativeDirectory.Length == 0
        ? "Archivio"
        : string.Join(" › ", hit.RelativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries));
}
