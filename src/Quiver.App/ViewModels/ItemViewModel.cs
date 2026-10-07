using System.Windows.Media;
using Quiver.App.Services;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A file, folder or shortcut inside a tile.</summary>
public class ItemViewModel(ArchiveItem item)
{
    private ImageSource? _icon;
    private bool _iconLoaded;

    public ArchiveItem Item { get; } = item;

    public string Name => Item.DisplayName;

    public string ToolTip => Item.FullPath;

    /// <summary>Loaded on first use, so only the items actually displayed pay for it.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconLoaded)
            {
                _icon = ShellIcons.Get(Item.FullPath, Item.IsFolder);
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
