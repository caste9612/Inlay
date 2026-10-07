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
    private bool _iconRequested;
    private bool _iconOnItsWay;
    private Geometry? _braceGlyph;
    private bool _braceGlyphKnown;

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
    /// and the files of an installed program, which keep the icon of Windows. While that icon is on its way, the
    /// document of Brace stands in for it.
    /// </summary>
    public Geometry? Glyph
    {
        get
        {
            RequestIcon();
            return BraceGlyph ?? (_iconOnItsWay ? FileGlyphs.Unknown : null);
        }
    }

    /// <summary>
    /// The icon of Windows, for programs, shortcuts and the other files: for them it is a logo. Read on first use, so only
    /// the items actually displayed pay for it, and on a thread of its own, so a slow one never holds the panel.
    /// </summary>
    public ImageSource? Icon
    {
        get
        {
            RequestIcon();
            return _icon;
        }
    }

    private Geometry? BraceGlyph
    {
        get
        {
            if (!_braceGlyphKnown)
            {
                _braceGlyph = FileGlyphs.For(Kind) ??
                    (Kind == FileKind.Other && !FileAssociations.IsKnown(Path.GetExtension(Item.Name)) ? FileGlyphs.Unknown : null);
                _braceGlyphKnown = true;
            }
            return _braceGlyph;
        }
    }

    /// <summary>Asks Windows for the icon once, the first time either the glyph or the icon is shown.</summary>
    private void RequestIcon()
    {
        if (_iconRequested)
            return;
        _iconRequested = true;
        if (BraceGlyph is not null || ShellIcons.TryGet(Item.FullPath, Item.IsFolder, out _icon))
            return;
        _iconOnItsWay = true;
        ShellIcons.Load(Item.FullPath, Item.IsFolder, icon =>
        {
            _icon = icon;
            _iconOnItsWay = false;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(Glyph));
        });
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
