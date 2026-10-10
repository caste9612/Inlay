using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Inlay.App.Interop;
using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App.Views;

/// <summary>
/// Everything inside the panel, used like the desktop: a click selects (Ctrl and Shift for more), a double-click or Enter
/// opens, files and tiles are dragged where they belong, F2 or a slow second click renames in place, Del moves to the
/// Recycle Bin, Ctrl+C, Ctrl+X and Ctrl+V work with Explorer too. Typing anywhere searches the whole archive.
/// </summary>
public partial class PanelView : UserControl
{
    /// <summary>Data of a tile dragged by its title: its folder.</summary>
    private const string TileFormat = "Inlay.Tile";

    /// <summary>Data of a topic dragged in the sidebar: its metadata key.</summary>
    private const string TopicFormat = "Inlay.Topic";

    // As in Explorer, a second click on the selected item renames it once it is clear that it is not a double click.
    private readonly DispatcherTimer _renameTimer = new() { Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime()) };

    // As over a folder of Explorer's tree, or a taskbar button: a drag held still over a topic opens it.
    private readonly DispatcherTimer _springTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private object? _springTarget;
    private Point _dragStart;
    private ItemViewModel? _pressedItem;
    private ItemViewModel? _pendingClick;
    private bool _pendingClickRenames;
    private ItemViewModel? _renameCandidate;
    private TileViewModel? _pressedTile;
    private TopicViewModel? _pressedTopic;
    private IInputElement? _focusBeforePrompt;
    private DragLabelAdorner? _dragLabel;
    private InsertionAdorner? _insertionLine;
    private InsertionAdorner? _topicLine;

    // Rows dragged from a tile of the board, while the drag lasts: dropped in the same tile, they change place in it.
    private IReadOnlyList<ItemViewModel> _draggedItems = [];
    private TileViewModel? _dragSourceTile;

    // The corner of a tile being dragged: where the drag started, and the size the tile had then.
    private Point _resizeStart;
    private double _resizeStartWidth;
    private double _resizeStartListHeight;
    private TileList? _resizeList;
    private TileGridPanel? _resizePanel;
    private TileSize? _sizeBefore;

    // The canvas: the archive it last showed whole, and a search result to fly to once it is clear the click was single.
    private string? _canvasFitFor;
    private readonly DispatcherTimer _resultTimer = new() { Interval = TimeSpan.FromMilliseconds(NativeMethods.GetDoubleClickTime()) };
    private SearchResultViewModel? _resultToShow;

    // Selecting with a rectangle: where the press was, the rectangle on screen, and what Ctrl keeps selected.
    private Point? _bandStart;
    private SelectionBandAdorner? _band;
    private HashSet<ItemViewModel>? _bandKept;
    private bool _banding;
    private bool _selectionSummaryQueued;
    private DateTime _shortcutsClosedAt;

    // Where the keyboard focus was, to put it back when a reload replaces the element that had it.
    private bool _focusWasInTree;
    private string? _focusedTile;

    public PanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _renameTimer.Tick += RenameTimer_Tick;
        _springTimer.Tick += SpringTimer_Tick;
        _resultTimer.Tick += ResultTimer_Tick;
        ShortcutsPopup.CustomPopupPlacementCallback = AboveRightEdge;
        ShortcutsPopup.Closed += (_, _) => _shortcutsClosedAt = DateTime.UtcNow;
    }

    /// <summary>Esc with nothing left to cancel, or the close button: the window goes back to the notification area.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The name of the archive was clicked: the window of the archives should open.</summary>
    public event EventHandler? ArchivesRequested;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>A question is open over the board: a confirmation, or where to send the files.</summary>
    private bool IsAsking => ViewModel.Prompt is not null || ViewModel.Picker is not null;

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
        // A search cleared on opening or with Esc is not something Ctrl+Z should bring back: it undoes on the board.
        if (SearchBox.Text.Length == 0)
        {
            SearchBox.IsUndoEnabled = false;
            SearchBox.IsUndoEnabled = true;
        }
    }

    /// <summary>The panel just appeared: its content fades in and settles, the way Brace moves things.</summary>
    public void AnimateIn() => Motion.Enter(this, slide: 8);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old)
        {
            old.Results.CollectionChanged -= OnResultsChanged;
            old.PropertyChanged -= OnViewModelPropertyChanged;
            old.SelectionRestored -= OnSelectionRestored;
            old.Reloaded -= OnReloaded;
            old.ShowOnCanvasRequested -= OnShowOnCanvasRequested;
        }
        if (e.NewValue is MainViewModel current)
        {
            current.Results.CollectionChanged += OnResultsChanged;
            current.PropertyChanged += OnViewModelPropertyChanged;
            current.SelectionRestored += OnSelectionRestored;
            current.Reloaded += OnReloaded;
            current.ShowOnCanvasRequested += OnShowOnCanvasRequested;
        }
    }

    // ---- Keyboard ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _renameTimer.Stop();
        // Esc closes the keys worth knowing, not the window.
        if (ShortcutsPopup.IsOpen && e.Key == Key.Escape)
        {
            ShortcutsPopup.IsOpen = false;
            e.Handled = true;
            return;
        }
        if (ViewModel.Picker is { } picker)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    picker.CancelCommand.Execute(null);
                    break;
                // Enter confirms, unless the user moved to one of the buttons with Tab.
                case Key.Enter when Keyboard.FocusedElement is Button { Command: { } command } button:
                    command.Execute(button.CommandParameter);
                    break;
                case Key.Enter:
                    picker.ConfirmCommand.Execute(null);
                    break;
                case Key.Down:
                    picker.MoveSelection(+1);
                    ScrollPickerToSelection();
                    break;
                case Key.Up:
                    picker.MoveSelection(-1);
                    ScrollPickerToSelection();
                    break;
                default:
                    return;
            }
            e.Handled = true;
            return;
        }
        if (ViewModel.Prompt is { } prompt)
        {
            if (e.Key == Key.Escape)
                prompt.CancelCommand.Execute(null);
            // Enter confirms, unless the user moved to one of the buttons with Tab.
            else if (e.Key == Key.Enter && Keyboard.FocusedElement is Button { Command: { } command } button)
                command.Execute(button.CommandParameter);
            else if (e.Key == Key.Enter)
                prompt.ConfirmCommand.Execute(null);
            else
                return;
            e.Handled = true;
            return;
        }

        // While renaming, the field handles its own keys.
        if (Keyboard.FocusedElement is TextBox box && !ReferenceEquals(box, SearchBox))
            return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key == Key.N && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ViewModel.NewTile();
            e.Handled = true;
            return;
        }
        if (key == Key.B && modifiers == ModifierKeys.Control)
        {
            ToggleSidebar();
            e.Handled = true;
            return;
        }
        // The canvas: Ctrl+0 shows it whole, Ctrl with + and - zooms, as in a browser.
        if (ViewModel.IsCanvasVisible && modifiers == ModifierKeys.Control && key is Key.D0 or Key.NumPad0 or Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            if (key is Key.D0 or Key.NumPad0)
                CanvasSurface.FitAll();
            else
                CanvasSurface.ZoomBy(key is Key.OemPlus or Key.Add ? 1.25 : 0.8);
            e.Handled = true;
            return;
        }
        if (key == Key.Escape)
        {
            e.Handled = true;
            if (ViewModel.IsSearching)
            {
                ViewModel.Query = "";
                FocusSearch();
            }
            else
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            return;
        }
        // In the search box, keys edit the text. While it is empty, Ctrl+Z and Ctrl+V (of files) act on the board,
        // as they would on the desktop: the panel opens with the focus there.
        if (Keyboard.FocusedElement is TextBox)
        {
            if (SearchBox.Text.Length > 0 || modifiers != ModifierKeys.Control)
                return;
            // Text typed and erased is the box's to bring back first.
            if (key == Key.Z && !SearchBox.CanUndo)
                Observe(ViewModel.Undo());
            else if (key == Key.V && FileClipboard.HasFiles && ViewModel.BoardFolder is { } board)
                Observe(ViewModel.PasteInto(board));
            else
                return;
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Z when modifiers == ModifierKeys.Control:
                Observe(ViewModel.Undo());
                break;
            case Key.F2 when modifiers == ModifierKeys.None:
                RenameFocused();
                break;
            case Key.Delete when modifiers == ModifierKeys.None:
                RecycleFocused();
                break;
            case Key.A when modifiers == ModifierKeys.Control:
                // Everything in the tile with the focus, and nothing else: the list itself selects its items.
                if (FocusedList() is { } list)
                    UnselectAll(except: list);
                return;
            case Key.C when modifiers == ModifierKeys.Control:
                ViewModel.CopyToClipboard(ViewModel.SelectedItems(), cut: false);
                break;
            case Key.X when modifiers == ModifierKeys.Control:
                ViewModel.CopyToClipboard(ViewModel.SelectedItems(), cut: true);
                break;
            case Key.V when modifiers == ModifierKeys.Control:
                if (PasteTarget() is { } folder)
                    Observe(ViewModel.PasteInto(folder));
                break;
            case Key.C when modifiers == (ModifierKeys.Control | ModifierKeys.Shift):
                if (ViewModel.SelectedItems().FirstOrDefault() is { } first)
                    ViewModel.CopyPath(first);
                break;
            case Key.Enter when modifiers == ModifierKeys.Alt:
                PropertiesOfFocused();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (IsAsking || Keyboard.FocusedElement is TextBox ||
            string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
            return;
        e.Handled = true;
        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when ViewModel.IsSearching:
                MoveResultSelection(+1);
                break;
            case Key.Up when ViewModel.IsSearching:
                MoveResultSelection(-1);
                break;
            case Key.Down:
                FocusSelectedTopic();
                break;
            case Key.Enter when ViewModel.IsSearching:
                var selected = ViewModel.SelectedItems();
                if (selected.Count == 0 && ViewModel.Results.FirstOrDefault() is { } first)
                    selected = [first];
                if (selected.Count > 0 && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    ViewModel.Reveal(selected[0]);
                else if (selected.Count > 0)
                    ViewModel.Open(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MoveResultSelection(int delta)
    {
        if (ResultsList.Items.Count == 0)
            return;
        ResultsList.SelectedIndex = Math.Clamp(ResultsList.SelectedIndex + delta, 0, ResultsList.Items.Count - 1);
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void ScrollPickerToSelection()
    {
        if (PickerList.SelectedItem is { } selected)
            PickerList.ScrollIntoView(selected);
    }

    /// <summary>Keeps the first result selected, so Enter in the search box opens it.</summary>
    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (ResultsList.SelectedIndex < 0 && ResultsList.Items.Count > 0)
                ResultsList.SelectedIndex = 0;
        }, DispatcherPriority.Background);

    private void RenameFocused()
    {
        if (FocusedTopic() is { } topic)
        {
            ViewModel.BeginRename(topic);
            return;
        }
        var selected = ViewModel.SelectedItems();
        if (selected.Count == 1)
            ViewModel.BeginRename(selected[0]);
    }

    private void RecycleFocused()
    {
        if (FocusedTopic() is { } topic)
            ViewModel.AskRecycle([topic.Topic.FullPath]);
        else
            ViewModel.AskRecycle(ViewModel.SelectedItems().Select(item => item.Item.FullPath).ToList());
    }

    private void PropertiesOfFocused()
    {
        if (FocusedTopic() is { } topic)
            ViewModel.ShowProperties(topic.Topic.FullPath);
        else if (ViewModel.SelectedItems().FirstOrDefault() is { } item)
            ViewModel.ShowProperties(item.Item.FullPath);
    }

    /// <summary>Where Ctrl+V puts files: the topic with the focus, the tile with the focus, or the board.</summary>
    private string? PasteTarget()
    {
        if (FocusedTopic() is { } topic)
            return topic.Topic.FullPath;
        // The board is hidden behind the results.
        if (ViewModel.IsSearching)
            return null;
        return FocusedList() is { DataContext: TileViewModel tile } ? tile.FolderPath : ViewModel.BoardFolder;
    }

    private static TopicViewModel? FocusedTopic() =>
        Keyboard.FocusedElement is TreeViewItem { DataContext: TopicViewModel topic } ? topic : null;

    private static ListBox? FocusedList() =>
        Keyboard.FocusedElement is DependencyObject focused ? FindAncestor<ListBox>(focused) : null;

    // ---- The filters of the search ----

    private void SearchKind_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<SearchKindFilter>(sender) is not { } filter)
            return;
        ViewModel.ToggleKindFilter(filter.Kind);
        FocusSearchAtEnd();
    }

    /// <summary>Where to look, from the filter above the results: everywhere, or in a topic.</summary>
    private void SearchPlace_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button)
            return;
        var menu = new ContextMenu();
        AddSearchPlaces(menu.Items);
        OpenUnder(menu, button);
    }

    /// <summary>The filters from the search box, also before anything is typed: the kinds, and where to look.</summary>
    private void SearchFilters_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement button)
            return;
        var kinds = new MenuItem { Header = "Type" };
        foreach (var filter in ViewModel.KindFilters)
        {
            var item = new MenuItem { Header = filter.Label, IsChecked = filter.IsChecked, InputGestureText = filter.ToolTip };
            item.Click += (_, _) =>
            {
                ViewModel.ToggleKindFilter(filter.Kind);
                FocusSearchAtEnd();
            };
            kinds.Items.Add(item);
        }
        var places = new MenuItem { Header = "Where" };
        AddSearchPlaces(places.Items);
        OpenUnder(new ContextMenu { Items = { kinds, places } }, button);
    }

    /// <summary>Everywhere, then every topic, those inside another one indented under it, as in the sidebar.</summary>
    private void AddSearchPlaces(ItemCollection items)
    {
        items.Add(SearchPlace("Everywhere", topic: null));
        items.Add(new Separator());
        foreach (var topic in ViewModel.Topics.SelectMany(topic => topic.SelfAndDescendants()))
            items.Add(SearchPlace(topic.Name, topic));
    }

    private MenuItem SearchPlace(string name, TopicViewModel? topic)
    {
        var item = new MenuItem
        {
            Header = new TextBlock { Text = name, Margin = new Thickness(16 * (topic?.Ancestors().Count() ?? 0), 0, 0, 0) },
            IsChecked = ViewModel.IsSearchPlace(topic),
        };
        item.Click += (_, _) =>
        {
            ViewModel.SetSearchPlace(topic);
            FocusSearchAtEnd();
        };
        return item;
    }

    private static void OpenUnder(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Back in the search box after a filter, at the end of what is written there, to go on typing.</summary>
    private void FocusSearchAtEnd()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    // ---- Topics ----

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TopicViewModel topic || ReferenceEquals(topic, ViewModel.SelectedTopic))
            return;
        ViewModel.SelectTopicCommand.Execute(topic);
        // On the canvas the view flies to its zone.
        if (ViewModel.IsCanvasVisible && ViewModel.Canvas.ZoneOf(topic) is { } zone)
            ShowZone(zone);
    }

    // ---- The canvas ----

    /// <summary>The canvas came into view with a size: the first time for an archive, it shows it whole.</summary>
    private void CanvasSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CanvasSurface.ActualWidth <= 0 || string.Equals(_canvasFitFor, ViewModel.Root, StringComparison.OrdinalIgnoreCase))
            return;
        _canvasFitFor = ViewModel.Root;
        // Once the zones are laid out.
        Dispatcher.BeginInvoke(() => CanvasSurface.FitAll(animated: false), DispatcherPriority.Loaded);
    }

    private void CanvasFitAll_Click(object sender, RoutedEventArgs e) => CanvasSurface.FitAll();

    private void CanvasZoomIn_Click(object sender, RoutedEventArgs e) => CanvasSurface.ZoomBy(1.25);

    private void CanvasZoomOut_Click(object sender, RoutedEventArgs e) => CanvasSurface.ZoomBy(0.8);

    /// <summary>A double click on the name of a zone, close up or from afar, shows the zone whole.</summary>
    private void ZoneHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: ZoneViewModel zone })
            return;
        e.Handled = true;
        ShowZone(zone);
    }

    /// <summary>From afar, a double click on a tile flies to it, close enough to work with it.</summary>
    private void TileSummary_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: TileViewModel tile })
            return;
        e.Handled = true;
        ShowTile(tile);
    }

    private void ShowZone(ZoneViewModel zone, bool animated = true)
    {
        if (AllDescendants<Border>(ZonesControl).FirstOrDefault(border => border.Name == "ZoneFrame" && ReferenceEquals(border.DataContext, zone)) is { } frame)
            CanvasSurface.Show(CanvasSurface.BoundsOf(frame), animated, maxZoom: 1);
    }

    private void ShowTile(TileViewModel tile, bool animated = true)
    {
        if (AllDescendants<GroupBox>(ZonesControl).FirstOrDefault(box => ReferenceEquals(box.DataContext, tile)) is { } box)
            CanvasSurface.Show(CanvasSurface.BoundsOf(box), animated, maxZoom: 1);
    }

    /// <summary>
    /// For the previews from the command line, which have no flights: the canvas whole, or around the zone of a topic,
    /// at the zoom asked for.
    /// </summary>
    internal void FrameCanvas(double? zoom, string? topicPath)
    {
        _canvasFitFor = ViewModel.Root;
        CanvasSurface.FitAll(animated: false);
        if (topicPath is not null && FindTopic(topicPath.Replace('\\', '/')) is { } topic && ViewModel.Canvas.ZoneOf(topic) is { } zone)
            ShowZone(zone, animated: false);
        if (zoom is { } wanted)
            CanvasSurface.ZoomBy(wanted / CanvasSurface.Camera.Zoom);
        CanvasSurface.WakeInViewNow();
    }

    /// <summary>«Show» after an operation, on the canvas: the tile where the items went, with them selected.</summary>
    private void OnShowOnCanvasRequested(object? sender, string folder) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (ViewModel.Canvas.TileOf(folder) is { } tile)
                ShowTile(tile);
        }, DispatcherPriority.Loaded);

    /// <summary>On the canvas, a click on a search result goes to it, once it is clear that it is not a double click.</summary>
    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!ViewModel.IsCanvasShown || ItemUnder(ResultsList, e.OriginalSource) is not SearchResultViewModel result)
            return;
        _resultToShow = result;
        _resultTimer.Stop();
        _resultTimer.Start();
    }

    private void ResultTimer_Tick(object? sender, EventArgs e)
    {
        _resultTimer.Stop();
        if (_resultToShow is { } result)
            ShowResultOnCanvas(result);
        _resultToShow = null;
    }

    /// <summary>
    /// The search closes and the view flies to the tile of the result, with it selected. A file deep in a folder of a tile
    /// shows as that folder.
    /// </summary>
    private void ShowResultOnCanvas(SearchResultViewModel result)
    {
        var path = result.Item.FullPath;
        ViewModel.Query = "";
        Dispatcher.BeginInvoke(() =>
        {
            for (var child = path; Path.GetDirectoryName(child) is { } folder; child = folder)
            {
                if (ViewModel.Canvas.TileOf(folder) is not { } tile)
                    continue;
                foreach (var item in ViewModel.ShownTiles.SelectMany(shown => shown.Items))
                    item.IsSelected = string.Equals(item.Item.FullPath, child, StringComparison.OrdinalIgnoreCase);
                ViewModel.UpdateSelectionSummary();
                ShowTile(tile);
                return;
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>A press on a topic, not on its arrow nor on its name being edited: dragged, it changes place.</summary>
    private void TopicTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedTopic = null;
        if (IsInside<ToggleButton>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource) || IsInside<ScrollBar>(e.OriginalSource))
            return;
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject) is { DataContext: TopicViewModel topic })
        {
            _pressedTopic = topic;
            _dragStart = e.GetPosition(this);
        }
    }

    private void TopicTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTopic is not { } topic || e.LeftButton != MouseButtonState.Pressed || !MovedEnough(e))
            return;
        _pressedTopic = null;
        if (topic.IsEditing || FindTopic(topic.Topic.RelativePath) is null)
            return;
        // Only Inlay understands it: a topic is not dragged out to Explorer, where it would leave the archive.
        var data = new DataObject(TopicFormat, topic.Topic.RelativePath);
        ShowDragLabel(topic.Name);
        try
        {
            DragDrop.DoDragDrop(TopicTree, data, DragDropEffects.Move);
        }
        finally
        {
            HideDragLabel();
            HideInsertionLine();
        }
    }

    private TopicViewModel? FindTopic(string relativePath) =>
        ViewModel.Topics.SelectMany(topic => topic.SelfAndDescendants())
            .FirstOrDefault(topic => string.Equals(topic.Topic.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The topic after this one among those next to it; null for the last.</summary>
    private TopicViewModel? NextSibling(TopicViewModel topic)
    {
        var siblings = topic.Parent?.Children ?? ViewModel.Topics;
        var index = siblings.IndexOf(topic);
        return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
    }

    private void FocusSelectedTopic()
    {
        var topic = ViewModel.SelectedTopic ?? ViewModel.Topics.FirstOrDefault();
        if (topic is not null && FindContainer(TopicTree, topic) is { } container)
            container.Focus();
        else
            TopicTree.Focus();
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;
        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is TreeViewItem container &&
                FindContainer(container, item) is { } found)
                return found;
        }
        return null;
    }

    // ---- Items: selection, opening, dragging ----

    /// <summary>
    /// One selection at a time, as on the desktop: choosing in a tile clears the others, while Ctrl adds to it.
    /// Selections put back after a reload come without the focus, and are left alone.
    /// </summary>
    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        QueueSelectionSummary();
        // A rectangle selects in several tiles at once: its selections are not clicks.
        if (_banding)
            return;
        if (e.AddedItems.Count > 0 && sender is ListBox { IsKeyboardFocusWithin: true } source &&
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            UnselectAll(except: source);
    }

    /// <summary>The status bar says how much is selected: once for all the lists that changed together.</summary>
    private void QueueSelectionSummary()
    {
        if (_selectionSummaryQueued)
            return;
        _selectionSummaryQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _selectionSummaryQueued = false;
            ViewModel.UpdateSelectionSummary();
        }, DispatcherPriority.Background);
    }

    /// <param name="except">A list whose selection stays.</param>
    private void UnselectAll(ListBox? except = null)
    {
        var keep = except is null ? null : ItemsOf(except);
        foreach (var tile in ViewModel.ShownTiles.Where(tile => !ReferenceEquals(tile.Items, keep)))
        {
            foreach (var item in tile.Items)
                item.IsSelected = false;
        }
        if (!ReferenceEquals(ViewModel.Results, keep))
        {
            foreach (var item in ViewModel.Results)
                item.IsSelected = false;
        }
    }

    private void ItemList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _renameTimer.Stop();
        _pendingClick = null;
        _pressedItem = null;
        if (sender is not ListBox list || IsInside<TextBox>(e.OriginalSource) || IsInside<ScrollBar>(e.OriginalSource))
            return;
        if (ItemUnder(list, e.OriginalSource) is not { } item)
        {
            // A click on the empty space of a tile clears the selection, as on the desktop; Ctrl+V then pastes there.
            if (Keyboard.Modifiers == ModifierKeys.None)
                UnselectAll();
            list.Focus();
            return;
        }
        _dragStart = e.GetPosition(this);
        _pressedItem = item;
        if (e.ClickCount != 1 || Keyboard.Modifiers != ModifierKeys.None || !item.IsSelected)
            return;
        // Already selected: the whole selection stays, so that it can be dragged; the click counts on release.
        _pendingClick = item;
        // A second click on the only selected item renames it, if the list already had the focus.
        _pendingClickRenames = ViewModel.SelectedItems().Count == 1 && list.IsKeyboardFocusWithin;
        (list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
        e.Handled = true;
    }

    private void ItemList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(sender, ResultsList))
            ResultsList_PreviewMouseLeftButtonUp(sender, e);
        _pressedItem = null;
        if (_pendingClick is not { } item)
            return;
        _pendingClick = null;
        if (_pendingClickRenames)
        {
            _renameCandidate = item;
            _renameTimer.Start();
        }
        else
        {
            UnselectAll();
            item.IsSelected = true;
        }
    }

    private void RenameTimer_Tick(object? sender, EventArgs e)
    {
        _renameTimer.Stop();
        var candidate = _renameCandidate;
        _renameCandidate = null;
        // Still the only selected item, and still on the board.
        if (candidate is not null && !IsAsking &&
            ViewModel.SelectedItems() is [var only] && ReferenceEquals(only, candidate))
            ViewModel.BeginRename(candidate);
    }

    private void ItemList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || IsInside<TextBox>(e.OriginalSource) || IsInside<ScrollBar>(e.OriginalSource))
            return;
        if (ItemUnder(list, e.OriginalSource) is not { } item)
        {
            UnselectAll();
            list.Focus();
            return;
        }
        // As on the desktop, the menu acts on the selection if the item is part of it, otherwise on the item alone.
        if (!item.IsSelected)
        {
            UnselectAll();
            item.IsSelected = true;
        }
        (list.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem)?.Focus();
    }

    private void ItemList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedItem is not { } item || e.LeftButton != MouseButtonState.Pressed || sender is not ListBox list || !MovedEnough(e))
            return;
        _pressedItem = null;
        _pendingClick = null;
        _renameTimer.Stop();
        // The board may have been read again since the press (a name confirmed by that click): its items may be new.
        if (!list.Items.Contains(item))
            return;
        if (!item.IsSelected)
        {
            UnselectAll();
            item.IsSelected = true;
        }
        var selected = ViewModel.SelectedItems();
        var paths = selected.Select(row => row.Item.FullPath).ToArray();
        // A plain list of files: Explorer, mail and other programs understand it too.
        var data = new DataObject(DataFormats.FileDrop, paths.Length > 0 ? paths : [item.Item.FullPath]);
        ShowDragLabel(paths.Length > 1 ? $"{paths.Length} items" : item.Name);
        // Dropped in the same tile, the rows change place there: only those of that tile are in hand.
        _dragSourceTile = list.DataContext as TileViewModel;
        _draggedItems = _dragSourceTile is { } source && selected.All(source.Items.Contains) ? selected : [];
        try
        {
            DragDrop.DoDragDrop(list, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            _dragSourceTile = null;
            _draggedItems = [];
            HideDragLabel();
            HideInsertionLine();
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInside<TextBox>(e.OriginalSource) || ItemUnder(sender, e.OriginalSource) is not { } item)
            return;
        _renameTimer.Stop();
        // A double click on a result opens it, also on the canvas: no flight to it.
        _resultTimer.Stop();
        _resultToShow = null;
        ViewModel.Open(item.IsSelected ? ViewModel.SelectedItems() : [item]);
        e.Handled = true;
    }

    private void ItemList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || ViewModel.SelectedItems() is not { Count: > 0 } selected)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ViewModel.Reveal(selected[0]);
        else
            ViewModel.Open(selected);
        e.Handled = true;
    }

    /// <summary>A tile scrolls its own list; at either end the wheel goes on scrolling the board.</summary>
    private void ItemList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox list || FindDescendants<ScrollViewer>(list).FirstOrDefault() is not { } inner)
            return;
        var canScroll = e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight;
        if (canScroll)
            return;
        e.Handled = true;
        BoardScroller.ScrollToVerticalOffset(BoardScroller.VerticalOffset - e.Delta / 3.0);
    }

    private void BoardArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var additive = Keyboard.Modifiers == ModifierKeys.Control;
        if (Keyboard.Modifiers == ModifierKeys.None)
            UnselectAll();
        // Ctrl+V then pastes where the click was: in that tile, or on the board.
        if (e.OriginalSource is DependencyObject source && FindAncestor<GroupBox>(source) is { } tileBox &&
            FindDescendants<ListBox>(tileBox).FirstOrDefault() is { } list)
            list.Focus();
        else
            BoardArea.Focus();
        // Dragged from here, the press draws a rectangle that selects what it touches, as on the desktop. Not from the
        // title of a tile, which drags the tile, nor from a button or a scrollbar.
        if (_pressedTile is null && e.OriginalSource is DependencyObject pressed &&
            !IsInside<ButtonBase>(pressed) && !IsInside<ScrollBar>(pressed) && !IsInside<TextBox>(pressed))
        {
            _bandStart = e.GetPosition(BoardArea);
            _bandKept = additive ? ViewModel.SelectedItems().ToHashSet() : null;
        }
    }

    // ---- Selecting with a rectangle ----

    private void BoardArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (_bandStart is not { } start)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            EndBand();
            return;
        }
        var current = e.GetPosition(BoardArea);
        if (_band is null)
        {
            var moved = current - start;
            if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            if (AdornerLayer.GetAdornerLayer(BoardArea) is not { } layer)
            {
                _bandStart = null;
                return;
            }
            _band = new SelectionBandAdorner(BoardArea, (Brush)FindResource("Brace.AccentSubtle"), (Brush)FindResource("Brace.Accent"));
            layer.Add(_band);
            BoardArea.CaptureMouse();
        }
        var band = new Rect(start, current);
        _band.Show(band);
        SelectInBand(band);
    }

    private void BoardArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndBand();

    private void BoardArea_LostMouseCapture(object sender, MouseEventArgs e) => EndBand();

    private void EndBand()
    {
        _bandStart = null;
        _bandKept = null;
        if (_band is null)
            return;
        var band = _band;
        _band = null;
        AdornerLayer.GetAdornerLayer(BoardArea)?.Remove(band);
        if (BoardArea.IsMouseCaptured)
            BoardArea.ReleaseMouseCapture();
    }

    /// <summary>Selects the rows the rectangle touches, in every tile; with Ctrl, those selected before stay.</summary>
    private void SelectInBand(Rect band)
    {
        _banding = true;
        try
        {
            foreach (var list in FindDescendants<ListBox>(BoardScroller))
            {
                // A tile scrolls its own rows: those out of its view are not on screen, and the rectangle cannot touch them.
                var visible = new Rect(list.TranslatePoint(new Point(0, 0), BoardArea), new Size(list.ActualWidth, list.ActualHeight));
                foreach (var item in list.Items.OfType<ItemViewModel>())
                {
                    if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row)
                        continue;
                    var bounds = new Rect(row.TranslatePoint(new Point(0, 0), BoardArea), new Size(row.ActualWidth, row.ActualHeight));
                    bounds.Intersect(visible);
                    var touched = !bounds.IsEmpty && band.IntersectsWith(bounds);
                    item.IsSelected = touched || (_bandKept?.Contains(item) ?? false);
                }
            }
        }
        finally
        {
            _banding = false;
        }
    }

    private static ItemViewModel? ItemUnder(object list, object source) =>
        list is ListBox listBox && source is DependencyObject element &&
        ItemsControl.ContainerFromElement(listBox, element) is ListBoxItem { DataContext: ItemViewModel item }
            ? item
            : null;

    /// <summary>The lists of the tiles on screen: on the board, or in every zone of the canvas, those within zones too.</summary>
    private IEnumerable<ListBox> TileLists() =>
        ViewModel.IsCanvasShown ? AllDescendants<TileList>(ZonesControl) : FindDescendants<ListBox>(BoardScroller);

    /// <summary>
    /// The row of the first selected item, on the board, on the canvas, or among the results. On the canvas the tile may
    /// have no rows yet: it gets them now.
    /// </summary>
    private ListBoxItem? FirstSelectedRow()
    {
        IEnumerable<ListBox> lists = ViewModel.IsSearching ? [ResultsList] : TileLists();
        foreach (var list in lists)
        {
            if (ItemsOf(list).OfType<ItemViewModel>().FirstOrDefault(item => item.IsSelected) is not { } selected)
                continue;
            if (list is TileList { IsAsleep: true } sleeping)
            {
                sleeping.Wake();
                sleeping.UpdateLayout();
            }
            if (list.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem row)
                return row;
        }
        return null;
    }

    /// <summary>The items of a list, its rows or not: a tile of the canvas has them before its rows.</summary>
    private static System.Collections.IEnumerable ItemsOf(ListBox list) =>
        (list is TileList tile ? tile.Source : list.ItemsSource) ?? Array.Empty<object>();

    private ListBox? ListOfTile(string? folder) =>
        folder is null
            ? null
            : TileLists().FirstOrDefault(list =>
                list.DataContext is TileViewModel tile && string.Equals(tile.FolderPath, folder, StringComparison.OrdinalIgnoreCase));

    private static void FocusRow(ListBoxItem row)
    {
        row.Focus();
        row.BringIntoView();
    }

    // ---- Keyboard focus across reloads ----

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // The controls of a question do not count: when it closes, the focus goes back to where it was before.
        if (IsAsking || e.NewFocus is not DependencyObject focused)
            return;
        _focusWasInTree = FindAncestor<TreeView>(focused) is not null;
        _focusedTile = FindAncestor<GroupBox>(focused)?.DataContext is TileViewModel tile ? tile.FolderPath : null;
    }

    /// <summary>After a reload the element with the focus may be gone.</summary>
    private void OnReloaded(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RestoreFocusIfLost, DispatcherPriority.Background);

    /// <summary>What was renamed, moved or pasted is selected: the focus goes to it, unless a name is being typed.</summary>
    private void OnSelectionRestored(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (IsVisible && !IsAsking && EditingBox() is null && FirstSelectedRow() is { } row)
            FocusRow(row);
    }, DispatcherPriority.Background);

    private bool HasLostFocus() =>
        Keyboard.FocusedElement is not UIElement { IsVisible: true } focused || focused is Window || ReferenceEquals(focused, this);

    private void RestoreFocusIfLost()
    {
        if (!IsVisible || IsAsking || !HasLostFocus())
            return;
        if (_focusWasInTree)
            FocusSelectedTopic();
        else if (FirstSelectedRow() is { } row)
            FocusRow(row);
        else if (ListOfTile(_focusedTile) is { } list)
            list.Focus();
        else if (ViewModel.IsBoardVisible)
            BoardArea.Focus();
        else if (ViewModel.IsCanvasVisible)
            CanvasSurface.Focus();
        else
            FocusSearch();
    }

    // ---- Tiles: moving them by their title ----

    private void TileHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedTile = null;
        if (sender is not FrameworkElement { DataContext: TileViewModel tile } ||
            IsInside<ButtonBase>(e.OriginalSource) || IsInside<TextBox>(e.OriginalSource))
            return;
        if (e.ClickCount == 2)
        {
            // A double click on the title renames the tile.
            if (tile.IsOwnFolder)
                ViewModel.BeginRename(tile);
            e.Handled = true;
            return;
        }
        if (tile.IsOwnFolder)
        {
            _pressedTile = tile;
            _dragStart = e.GetPosition(this);
        }
    }

    private void TileHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedTile is not { } tile || e.LeftButton != MouseButtonState.Pressed || !MovedEnough(e))
            return;
        _pressedTile = null;
        if (!ViewModel.ShownTiles.Contains(tile))
            return;
        var data = new DataObject();
        data.SetData(TileFormat, tile.FolderPath);
        // As a folder too: dropped on a topic it moves there, dropped on Explorer it moves out of the archive.
        data.SetFileDropList([tile.FolderPath]);
        ShowDragLabel(tile.Title);
        try
        {
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move | DragDropEffects.Copy);
        }
        finally
        {
            HideDragLabel();
            HideInsertionLine();
        }
    }

    private void TileActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindAncestor<GroupBox>(button) is not { ContextMenu: { } menu } tileBox)
            return;
        menu.DataContext = tileBox.DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---- Tiles: their size, from the corner ----

    private void ResizeGrip_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb { DataContext: TileViewModel tile, Parent: FrameworkElement tileRoot })
            return;
        _sizeBefore = tile.Size;
        // In points of the board, or of the canvas, whatever its zoom.
        _resizePanel = FindAncestor<TileGridPanel>(tileRoot);
        _resizeStart = _resizePanel is { } panel ? Mouse.GetPosition(panel) : default;
        _resizeStartWidth = tileRoot.ActualWidth;
        _resizeList = FindDescendants<TileList>(tileRoot).FirstOrDefault();
        _resizeStartListHeight = _resizeList?.ActualHeight ?? 0;
    }

    /// <summary>
    /// The tile follows the corner: in width it snaps to the columns of the board, in height to its rows. A drag that
    /// hardly moves up or down leaves the height as it was, automatic included.
    /// </summary>
    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { DataContext: TileViewModel tile } || _resizeList is not { } list || _resizePanel is not { } board)
            return;
        // From where the drag started: the corner itself jumps along with the tile.
        var moved = Mouse.GetPosition(board) - _resizeStart;
        var columns = (int)Math.Round((_resizeStartWidth + moved.X + board.Spacing) / (board.ColumnWidth + board.Spacing));
        columns = Math.Clamp(columns, 1, Math.Max(1, Math.Min(TileSize.MaxColumns, board.ColumnCount)));
        var rows = _sizeBefore?.Rows;
        if (rows is not null || Math.Abs(moved.Y) >= list.RowPitch / 2)
            rows = Math.Clamp(list.RowsFor(_resizeStartListHeight + moved.Y), TileSize.MinRows, TileSize.MaxRows);
        var size = new TileSize(columns, rows);
        tile.Size = size.IsUsual ? null : size;
    }

    private void ResizeGrip_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is not Thumb { DataContext: TileViewModel tile })
            return;
        if (e.Canceled)
            tile.Size = _sizeBefore;
        else
            ViewModel.ResizeTile(tile, tile.Size);
        _sizeBefore = null;
        _resizeList = null;
        _resizePanel = null;
    }

    /// <summary>A double click on the corner gives the tile back the size that follows its items.</summary>
    private void ResizeGrip_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Thumb { DataContext: TileViewModel tile })
            return;
        ViewModel.ResizeTile(tile, null);
        e.Handled = true;
    }

    private void TileAutoSize_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.ResizeTile(tile, null);
    }

    private void TileSort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<SortKey>(tag, out var key) && DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.SortTile(tile, key);
    }

    private void TileSortDirection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.SetSortDirection(tile, descending: tag == "descending");
    }

    // ---- Dropping ----

    private enum DropAction
    {
        None,
        Transfer,
        MoveTile,

        /// <summary>A topic dragged in the sidebar: another place next to the others, or inside another topic.</summary>
        MoveTopic,

        /// <summary>Rows dragged inside their own tile: they change place there.</summary>
        ArrangeItems,

        /// <summary>Files that are not on disk yet, like a mail attachment: they are saved, a copy of the original.</summary>
        SaveDropped,
    }

    private sealed record DropPlan(DropAction Action)
    {
        public static readonly DropPlan Nothing = new(DropAction.None);

        public string[] Paths { get; init; } = [];

        public string Destination { get; init; } = "";

        public TransferKind Kind { get; init; }

        /// <summary>Folders that land in a topic become rows of its «General»; not a tile dragged by its title, which stays a tile.</summary>
        public bool FoldersAsRows { get; init; } = true;

        /// <summary>The tile dragged by its title, or the one whose rows change place.</summary>
        public TileViewModel? Tile { get; init; }

        /// <summary>The tile it lands before; null for after the last.</summary>
        public TileViewModel? Before { get; init; }

        public TopicViewModel? Topic { get; init; }

        /// <summary>Where the topic goes: inside this topic, or among those in the root when null.</summary>
        public TopicViewModel? TopicParent { get; init; }

        /// <summary>The topic it lands before; null for after the last.</summary>
        public TopicViewModel? TopicBefore { get; init; }

        /// <summary>Into the middle of another topic: the row lights up instead of a line.</summary>
        public bool Inside { get; init; }

        public IReadOnlyList<ItemViewModel> Items { get; init; } = [];

        /// <summary>The row they land before; null for after the last.</summary>
        public ItemViewModel? ItemBefore { get; init; }

        /// <summary>Where the line goes: along the top of this row, or along its bottom.</summary>
        public FrameworkElement? LineRow { get; init; }

        public bool LineAtBottom { get; init; }
    }

    /// <summary>Outside the drop targets nothing can be dropped; a target that wants the line shows it again right after.</summary>
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        HideInsertionLine();
    }

    /// <summary>The drag went out of the window: no topic opens by itself any more, and no line shows where a tile would land.</summary>
    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X > 0 && position.Y > 0 && position.X < ActualWidth && position.Y < ActualHeight)
            return;
        HideInsertionLine();
        DisarmSpring();
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        ArmSpring(sender, e);
        var plan = PlanDrop(sender, e);
        e.Effects = plan.Action switch
        {
            DropAction.MoveTile or DropAction.MoveTopic or DropAction.ArrangeItems => DragDropEffects.Move,
            DropAction.SaveDropped => DragDropEffects.Copy,
            DropAction.Transfer when plan.Kind == TransferKind.Copy => DragDropEffects.Copy,
            DropAction.Transfer when plan.Kind == TransferKind.Link => DragDropEffects.Link,
            DropAction.Transfer => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };
        // Files light up the border of their target, and a topic the row it goes into; a tile, a row or a topic that
        // changes place shows where it will land with a line.
        SetDropHighlight(sender, plan.Action is DropAction.Transfer or DropAction.SaveDropped || plan.Inside);
        switch (plan.Action)
        {
            case DropAction.MoveTile:
                ShowInsertionLine(plan.Tile!, plan.Before);
                break;
            case DropAction.ArrangeItems when plan.LineRow is { } row && TilesHostOf(ViewModel.SiblingsOf(plan.Tile!)) is { } host:
                LineOn(host)?.ShowAt(row, plan.LineAtBottom);
                break;
            case DropAction.MoveTopic when !plan.Inside && plan.LineRow is { } row:
                ShowLine(ref _topicLine, TopicTree, row, plan.LineAtBottom);
                break;
        }
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e)
    {
        if (ReferenceEquals(sender, _springTarget))
            DisarmSpring();
        SetDropHighlight(sender, false);
    }

    private void DropTarget_Drop(object sender, DragEventArgs e)
    {
        DisarmSpring();
        HideInsertionLine();
        var plan = PlanDrop(sender, e);
        SetDropHighlight(sender, false);
        // Inlay does the move or copy itself, so the program the files come from must not delete anything.
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        // Let the drag finish first: a long copy must not keep the other program waiting.
        switch (plan.Action)
        {
            case DropAction.MoveTile:
                Dispatcher.BeginInvoke(() => ViewModel.MoveTile(plan.Tile!, plan.Before));
                break;
            case DropAction.MoveTopic:
                Dispatcher.BeginInvoke(() => Observe(ViewModel.MoveTopic(plan.Topic!, plan.TopicParent, plan.TopicBefore)));
                break;
            case DropAction.ArrangeItems:
                Dispatcher.BeginInvoke(() => ViewModel.MoveItems(plan.Tile!, plan.Items, plan.ItemBefore));
                break;
            case DropAction.Transfer:
                Dispatcher.BeginInvoke(() => Observe(ViewModel.Transfer(plan.Paths, plan.Destination, plan.Kind, plan.FoldersAsRows)));
                break;
            case DropAction.SaveDropped:
                // Now or never: the content is only there while the drop lasts.
                ViewModel.SaveDropped(e.Data, plan.Destination);
                break;
        }
    }

    /// <summary>
    /// Holding a drag over a topic in the sidebar or over a sub-topic button opens it after a moment, so the files can go
    /// straight into one of its tiles.
    /// </summary>
    private void ArmSpring(object target, DragEventArgs e)
    {
        // A tile dragged by its title moves into the topic it is dropped on: its own board must stay. A topic changing
        // place opens nothing either.
        var opens = !e.Data.GetDataPresent(TileFormat) && !e.Data.GetDataPresent(TopicFormat) &&
                    (target as FrameworkElement)?.DataContext is TopicViewModel topic && !ReferenceEquals(topic, ViewModel.SelectedTopic);
        if (!opens)
        {
            if (ReferenceEquals(target, _springTarget))
                DisarmSpring();
            return;
        }
        if (ReferenceEquals(target, _springTarget))
            return;
        _springTarget = target;
        _springTimer.Stop();
        _springTimer.Start();
    }

    private void DisarmSpring()
    {
        _springTimer.Stop();
        _springTarget = null;
    }

    private void SpringTimer_Tick(object? sender, EventArgs e)
    {
        var target = (_springTarget as FrameworkElement)?.DataContext;
        DisarmSpring();
        if (target is not TopicViewModel topic)
            return;
        topic.IsExpanded = true;
        ViewModel.SelectTopicCommand.Execute(topic);
    }

    private DropPlan PlanDrop(object target, DragEventArgs e)
    {
        var onBoard = ReferenceEquals(target, BoardArea);
        var context = (target as FrameworkElement)?.DataContext;

        if (e.Data.GetDataPresent(TopicFormat))
            return PlanTopicDrop(target, e);
        // The empty part of the sidebar takes topics only.
        if (ReferenceEquals(target, TopicTree))
            return DropPlan.Nothing;

        // A tile dragged by its title and dropped among those next to it changes place there: on its board, or in its zone
        // of the canvas. Onto a tile of another zone it goes nowhere: that is a move to another topic, for later.
        if (e.Data.GetDataPresent(TileFormat) && (onBoard || context is TileViewModel) &&
            e.Data.GetData(TileFormat) is string tilePath &&
            ViewModel.ShownTiles.FirstOrDefault(tile => string.Equals(tile.FolderPath, tilePath, StringComparison.OrdinalIgnoreCase)) is { } moving)
        {
            if (onBoard)
                return new DropPlan(DropAction.MoveTile) { Tile = moving };
            return context is TileViewModel { IsOwnFolder: true } before && !ReferenceEquals(before, moving) &&
                   ViewModel.SiblingsOf(moving).Contains(before)
                ? new DropPlan(DropAction.MoveTile) { Tile = moving, Before = before }
                : DropPlan.Nothing;
        }

        // Rows dragged inside their own tile change place there, unless Ctrl or Alt asks for something else.
        if (context is TileViewModel arranged && ReferenceEquals(arranged, _dragSourceTile) && _draggedItems.Count > 0 &&
            !e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) && !e.KeyStates.HasFlag(DragDropKeyStates.AltKey) &&
            target is DependencyObject box && FindDescendants<ListBox>(box).FirstOrDefault() is { } rows)
            return PlanArrangeDrop(arranged, rows, e);

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var destination = onBoard
            ? ViewModel.BoardFolder
            : context switch
            {
                TileViewModel tile => tile.FolderPath,
                TopicViewModel topic => topic.Topic.FullPath,
                _ => null,
            };
        if (destination is null)
            return DropPlan.Nothing;
        // An attachment from Outlook, a link or an image from the browser: no file on disk yet, only its content.
        if (paths.Length == 0 && VirtualFiles.ArePresent(e.Data) && e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            return new DropPlan(DropAction.SaveDropped) { Destination = destination, Kind = TransferKind.Copy };
        if (paths.Length == 0)
            return DropPlan.Nothing;

        var kind = TransferRules.Choose(paths, destination,
            forceCopy: e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            forceMove: e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey),
            forceLink: e.KeyStates.HasFlag(DragDropKeyStates.AltKey),
            preferLink: paths.All(path => TransferRules.HasItsOwnPlace(path, ViewModel.Root)));
        // Some sources only allow copying, others only moving.
        if (kind == TransferKind.Link && !e.AllowedEffects.HasFlag(DragDropEffects.Link))
            kind = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? TransferKind.Copy : TransferKind.None;
        if (kind == TransferKind.Move && !e.AllowedEffects.HasFlag(DragDropEffects.Move))
            kind = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? TransferKind.Copy : TransferKind.None;
        if (kind == TransferKind.Copy && !e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            kind = TransferKind.None;
        return kind == TransferKind.None
            ? DropPlan.Nothing
            : new DropPlan(DropAction.Transfer) { Paths = paths, Destination = destination, Kind = kind, FoldersAsRows = !e.Data.GetDataPresent(TileFormat) };
    }

    /// <summary>
    /// A topic over another one: on the upper part of its row it goes before it, on the lower part after it, in the middle
    /// inside it. Over the empty part of the sidebar it goes last among the topics of the root.
    /// </summary>
    private DropPlan PlanTopicDrop(object target, DragEventArgs e)
    {
        if (e.Data.GetData(TopicFormat) is not string key || FindTopic(key) is not { } moving)
            return DropPlan.Nothing;
        if (ReferenceEquals(target, TopicTree))
        {
            var last = ViewModel.Topics.LastOrDefault() is { } lastTopic ? FindContainer(TopicTree, lastTopic) : null;
            return new DropPlan(DropAction.MoveTopic) { Topic = moving, LineRow = last, LineAtBottom = true };
        }
        if (target is not TreeViewItem { DataContext: TopicViewModel over } item || item.Template?.FindName("Bd", item) is not FrameworkElement row ||
            ReferenceEquals(over, moving))
            return DropPlan.Nothing;

        var y = e.GetPosition(row).Y;
        var plan = y < row.ActualHeight * 0.3
            ? new DropPlan(DropAction.MoveTopic) { Topic = moving, TopicParent = over.Parent, TopicBefore = over, LineRow = row }
            : y > row.ActualHeight * 0.7
                // Under a topic and everything open inside it.
                ? new DropPlan(DropAction.MoveTopic) { Topic = moving, TopicParent = over.Parent, TopicBefore = NextSibling(over), LineRow = item, LineAtBottom = true }
                : new DropPlan(DropAction.MoveTopic) { Topic = moving, TopicParent = over, Inside = true };
        return MainViewModel.CanGoUnder(moving, plan.TopicParent) ? plan : DropPlan.Nothing;
    }

    /// <summary>Rows over their own tile: they go before the row under the pointer, or after the last one in view.</summary>
    private DropPlan PlanArrangeDrop(TileViewModel tile, ListBox list, DragEventArgs e)
    {
        var pointer = e.GetPosition(list).Y;
        FrameworkElement? lastVisible = null;
        foreach (var item in list.Items.OfType<ItemViewModel>())
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row)
                continue;
            var top = row.TranslatePoint(new Point(0, 0), list).Y;
            // A tile scrolls its own rows: those above its view are not on screen, and the first one below it comes after
            // the last one in view.
            if (top + row.ActualHeight <= 0)
                continue;
            if (top >= list.ActualHeight)
                return Arranged(item, lastVisible, atBottom: true);
            if (pointer < top + row.ActualHeight / 2)
                return Arranged(item, row, atBottom: false);
            lastVisible = row;
        }
        return Arranged(null, lastVisible, atBottom: true);

        DropPlan Arranged(ItemViewModel? before, FrameworkElement? line, bool atBottom) =>
            new(DropAction.ArrangeItems) { Tile = tile, Items = _draggedItems, ItemBefore = before, LineRow = line, LineAtBottom = atBottom };
    }

    private void SetDropHighlight(object target, bool on)
    {
        // Clearing the local value brings back the template's own look, selection included.
        switch (target)
        {
            case TreeViewItem item when item.Template?.FindName("Bd", item) is Border row:
                if (on)
                    row.Background = (Brush)FindResource("Brace.AccentSubtle");
                else
                    row.ClearValue(Border.BackgroundProperty);
                break;
            case Control control:
                if (on)
                    control.BorderBrush = (Brush)FindResource("Brace.Accent");
                else
                    control.ClearValue(BorderBrushProperty);
                break;
        }
    }

    // ---- What a drag shows: the line where a tile will land, the label of what is in hand ----

    /// <summary>
    /// The line where the dragged tile will land among those next to it, on the board or in its zone of the canvas:
    /// before <paramref name="before"/>, or after the last tile.
    /// </summary>
    private void ShowInsertionLine(TileViewModel moving, TileViewModel? before)
    {
        var siblings = ViewModel.SiblingsOf(moving);
        if (TilesHostOf(siblings) is not { } host || LineOn(host) is not { } line)
            return;
        var target = before is null ? null : host.ItemContainerGenerator.ContainerFromItem(before) as FrameworkElement;
        var last = siblings.Count == 0 ? null : host.ItemContainerGenerator.ContainerFromItem(siblings[^1]) as FrameworkElement;
        var spacing = FindDescendants<TileGridPanel>(host).FirstOrDefault()?.Spacing ?? 16;
        line.ShowBefore(target, last, spacing);
    }

    /// <summary>The control that shows these tiles: the board, or a zone of the canvas.</summary>
    private ItemsControl? TilesHostOf(IReadOnlyList<TileViewModel> tiles) =>
        ReferenceEquals(tiles, ViewModel.Tiles)
            ? TilesControl
            : AllDescendants<ItemsControl>(ZonesControl).FirstOrDefault(control => ReferenceEquals(control.ItemsSource, tiles));

    /// <summary>The line of the drag, on the layer of <paramref name="host"/>: made again when the drag goes to another control.</summary>
    private InsertionAdorner? LineOn(UIElement host)
    {
        if (_insertionLine is { } current && ReferenceEquals(current.AdornedElement, host))
            return current;
        if (_insertionLine is { } old)
            AdornerLayer.GetAdornerLayer(old.AdornedElement)?.Remove(old);
        _insertionLine = null;
        if (AdornerLayer.GetAdornerLayer(host) is not { } layer)
            return null;
        _insertionLine = new InsertionAdorner(host, (Brush)FindResource("Brace.Accent"));
        layer.Add(_insertionLine);
        return _insertionLine;
    }

    /// <summary>The line along a topic of the sidebar, on the adorner layer of the tree.</summary>
    private void ShowLine(ref InsertionAdorner? line, UIElement host, FrameworkElement row, bool atBottom)
    {
        if (line is null)
        {
            if (AdornerLayer.GetAdornerLayer(host) is not { } layer)
                return;
            line = new InsertionAdorner(host, (Brush)FindResource("Brace.Accent"));
            layer.Add(line);
        }
        line.ShowAt(row, atBottom);
    }

    private void HideInsertionLine()
    {
        _insertionLine?.Hide();
        _topicLine?.Hide();
    }

    private void ShowDragLabel(string text)
    {
        if (AdornerLayer.GetAdornerLayer(this) is not { } layer)
            return;
        HideDragLabel();
        _dragLabel = new DragLabelAdorner(this, text);
        layer.Add(_dragLabel);
    }

    private void HideDragLabel()
    {
        if (_dragLabel is null)
            return;
        AdornerLayer.GetAdornerLayer(this)?.Remove(_dragLabel);
        _dragLabel = null;
    }

    /// <summary>Keeps the label next to the pointer while the drag lasts; Windows chooses the cursor as usual.</summary>
    private void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (_dragLabel is not null && NativeMethods.GetCursorPos(out var cursor))
            _dragLabel.MoveTo(PointFromScreen(new Point(cursor.X, cursor.Y)));
    }

    // ---- Renaming in place ----

    /// <summary>The field of the name being edited, when it has the focus.</summary>
    private static TextBox? EditingBox() =>
        Keyboard.FocusedElement is TextBox { DataContext: RenameableViewModel { IsEditing: true } } box ? box : null;

    /// <summary>A click anywhere outside the name being edited confirms it, as on the desktop.</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _renameTimer.Stop();
        if (EditingBox() is { DataContext: RenameableViewModel target } box &&
            !ReferenceEquals(FindAncestor<TextBox>(e.OriginalSource as DependencyObject), box))
            CommitAfterInput(target);
    }

    private void EditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box)
            Dispatcher.BeginInvoke(() => FocusEditBox(box), DispatcherPriority.Input);
    }

    /// <summary>
    /// Puts the cursor in a name being edited, selected as in Explorer. If the board was still being built and the field
    /// could not take the focus, it tries once more a moment later.
    /// </summary>
    private void FocusEditBox(TextBox box, bool retry = true)
    {
        if (!box.IsVisible || box.DataContext is not RenameableViewModel { IsEditing: true } target)
            return;
        box.Focus();
        Keyboard.Focus(box);
        box.Select(0, target.EditSelectionLength);
        if (!box.IsKeyboardFocused && retry)
            Dispatcher.BeginInvoke(() => FocusEditBox(box, retry: false), DispatcherPriority.Background);
    }

    /// <summary>The field of a name being edited, if one is on screen.</summary>
    private TextBox? OpenEditBox() =>
        FindDescendants<TextBox>(this).FirstOrDefault(box => box.IsVisible && box.DataContext is RenameableViewModel { IsEditing: true });

    private void EditBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: RenameableViewModel target })
            return;
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            // A name that cannot be used leaves the field open, with the reason in the status bar.
            if (ViewModel.CommitRename(target) is { } problem)
                ViewModel.Tell(ToastKind.Info, problem);
            else
                FocusAfterEdit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ViewModel.CancelRename(target);
            FocusAfterEdit();
        }
    }

    private void EditBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: RenameableViewModel { IsEditing: true } target })
            return;
        // The field's own menu (cut, copy, paste) does not end the edit.
        if (e.NewFocus is DependencyObject next && FindAncestor<ContextMenu>(next) is not null)
            return;
        CommitAfterInput(target);
    }

    /// <summary>
    /// Confirms a name once the click that ended the edit has done its work: the rename reads the board again,
    /// and the click must not get lost in it.
    /// </summary>
    private void CommitAfterInput(RenameableViewModel target) => Dispatcher.BeginInvoke(() =>
    {
        if (!target.IsEditing)
            return;
        if (ViewModel.CommitRename(target) is { } problem)
        {
            ViewModel.CancelRename(target);
            ViewModel.Tell(ToastKind.Info, problem);
        }
    }, DispatcherPriority.Input);

    /// <summary>The field closed: the focus goes back to the item, the tile or the topic.</summary>
    private void FocusAfterEdit() => Dispatcher.BeginInvoke(RestoreFocusIfLost, DispatcherPriority.Background);

    // ---- Questions over the board: confirmations and «Move to…» ----

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainViewModel.Prompt) or nameof(MainViewModel.Picker)))
            return;
        if (IsAsking)
            _focusBeforePrompt ??= Keyboard.FocusedElement;
        // A question that just opened settles into place, the way Brace shows things.
        if (e.PropertyName == nameof(MainViewModel.Prompt) && ViewModel.Prompt is not null)
            Motion.Enter(PromptBox, slide: 6, scaleFrom: 0.97);
        if (e.PropertyName == nameof(MainViewModel.Picker) && ViewModel.Picker is not null)
            Motion.Enter(PickerBox, slide: 6, scaleFrom: 0.97);
        Dispatcher.BeginInvoke(FocusAfterPromptChange, DispatcherPriority.Input);
    }

    private void FocusAfterPromptChange()
    {
        if (ViewModel.Picker is not null)
        {
            PickerFilter.Focus();
            PickerFilter.SelectAll();
            return;
        }
        if (ViewModel.Prompt is { } prompt)
        {
            if (prompt.HasInput)
            {
                PromptInput.Focus();
                PromptInput.Select(0, prompt.SelectionLength);
            }
            else
            {
                (prompt.IsDanger ? PromptConfirmDanger : PromptConfirm).Focus();
            }
            return;
        }
        var previous = _focusBeforePrompt;
        _focusBeforePrompt = null;
        // A name ready to type, like that of a new shortcut, comes first.
        if (OpenEditBox() is { } box)
        {
            FocusEditBox(box);
            return;
        }
        // Back where the user was, if that is still there; the board may have been read again meanwhile.
        if (HasLostFocus() && !(previous is UIElement { IsVisible: true } element && element.Focus()))
            RestoreFocusIfLost();
    }

    private void PickerList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(PickerList, source) is ListBoxItem)
            ViewModel.Picker?.ConfirmCommand.Execute(null);
    }

    // ---- Menus and buttons ----

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            SystemCommands.MinimizeWindow(window);
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window)
            return;
        if (window.WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(window);
        else
            SystemCommands.MaximizeWindow(window);
    }

    private void Archives_Click(object sender, RoutedEventArgs e) => ArchivesRequested?.Invoke(this, EventArgs.Empty);

    private void NewTopic_Click(object sender, RoutedEventArgs e) => ViewModel.NewTopic(null);

    private void NewTile_Click(object sender, RoutedEventArgs e) => ViewModel.NewTile();

    private void Undo_Click(object sender, RoutedEventArgs e) => Observe(ViewModel.Undo());

    // ---- The topics on the left ----

    private void SidebarToggle_Click(object sender, RoutedEventArgs e) => ToggleSidebar();

    /// <summary>Closes or opens the topics on the left; closing them, the focus they had goes to the board or the canvas.</summary>
    private void ToggleSidebar()
    {
        var focusInside = Sidebar.IsKeyboardFocusWithin;
        ViewModel.IsSidebarOpen = !ViewModel.IsSidebarOpen;
        if (!focusInside || ViewModel.IsSidebarOpen)
            return;
        if (ViewModel.IsCanvasVisible)
            CanvasSurface.Focus();
        else
            BoardArea.Focus();
    }

    // ---- The status bar and the toasts ----

    /// <summary>The archive in the status bar: its folder in Explorer.</summary>
    private void StatusArchive_Click(object sender, RoutedEventArgs e) => ViewModel.OpenFolder(ViewModel.Root);

    private void RestartToUpdate_Click(object sender, RoutedEventArgs e) => ViewModel.RestartToUpdate();

    private void ShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        // A click on the «?» with the keys open closes them on its way down: on its way up it does not open them again.
        if (DateTime.UtcNow - _shortcutsClosedAt < TimeSpan.FromMilliseconds(300))
            return;
        ShortcutsPopup.IsOpen = true;
    }

    /// <summary>
    /// The keys worth knowing open above the «?», their right edge on that of the button. The frame of Brace keeps a
    /// margin around itself for the shadow (Brace.PopupFrame), taken back here; the sizes come in pixels of the screen.
    /// </summary>
    private CustomPopupPlacement[] AboveRightEdge(Size popup, Size target, Point offset)
    {
        var dpi = VisualTreeHelper.GetDpi(ShortcutsButton);
        var frame = new Thickness(16, 8, 16, 24);
        return [new CustomPopupPlacement(
            new Point(target.Width - popup.Width + frame.Right * dpi.DpiScaleX, -popup.Height + frame.Bottom * dpi.DpiScaleY),
            PopupPrimaryAxis.Horizontal)];
    }

    /// <summary>A toast comes in as Brace wants, and the news and results close by themselves after a few seconds.</summary>
    private void Toast_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded comes again when the toast is laid out anew: one clock is enough.
        if (sender is not FrameworkElement { DataContext: ToastViewModel toast, Tag: null } element)
            return;
        element.Tag = toast;
        Motion.Enter(element, slide: 8);
        if (toast.ClosesByItself)
            ToastClock.Start(element, () => ViewModel.DismissToast(toast));
    }

    private void ToastDismiss_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToastViewModel toast })
            ViewModel.DismissToast(toast);
    }

    private void ToastUndo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToastViewModel toast })
            Observe(ViewModel.UndoFrom(toast));
    }

    private void ToastShow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ToastViewModel toast })
            ViewModel.ShowFrom(toast);
    }

    private void BoardPaste_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            Observe(ViewModel.PasteInto(folder));
    }

    private void BoardPasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            Observe(ViewModel.PasteLinksInto(folder));
    }

    private void BoardNewLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            ViewModel.AskNewLink(folder);
    }

    private void BoardOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            ViewModel.OpenFolder(folder);
    }

    /// <summary>"Paste" is available only when the clipboard holds files.</summary>
    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
            return;
        var canPaste = FileClipboard.HasFiles;
        foreach (var item in menu.Items.OfType<MenuItem>().Where(item => item.Tag is "paste"))
            item.IsEnabled = canPaste;
    }

    /// <summary>A menu opened on a selected item acts on the whole selection, as on the desktop.</summary>
    private IReadOnlyList<ItemViewModel> TargetsOf(ItemViewModel item)
    {
        var selected = ViewModel.SelectedItems();
        return item.IsSelected && selected.Count > 0 ? selected : [item];
    }

    private void ItemOpen_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.Open(TargetsOf(item));
    }

    private void ItemOpenWith_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.OpenWith(item);
    }

    private void ItemReveal_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.Reveal(item);
    }

    private void ItemMoveTo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.AskDestination(TargetsOf(item).Select(target => target.Item.FullPath).ToList());
    }

    private void ItemCut_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.CopyToClipboard(TargetsOf(item), cut: true);
    }

    private void ItemCopy_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.CopyToClipboard(TargetsOf(item), cut: false);
    }

    private void ItemCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.CopyPath(item);
    }

    private void ItemRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.BeginRename(item);
    }

    private void ItemPromote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.PromoteItem(item);
    }

    private void ItemProperties_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.ShowProperties(item.Item.FullPath);
    }

    private void ItemRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.AskRecycle(TargetsOf(item).Select(target => target.Item.FullPath).ToList());
    }

    private void TilePaste_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            Observe(ViewModel.PasteInto(tile.FolderPath));
    }

    private void TilePasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            Observe(ViewModel.PasteLinksInto(tile.FolderPath));
    }

    private void TileNewLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.AskNewLink(tile.FolderPath);
    }

    private void TileNewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.NewFolder(tile);
    }

    private void TileOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.OpenFolder(tile.FolderPath);
    }

    private void TileRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { IsOwnFolder: true } tile)
            ViewModel.BeginRename(tile);
    }

    private void TilePromote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.PromoteTile(tile);
    }

    private void TileDemote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.DemoteTile(tile);
    }

    /// <summary>A color of the palette from the tile's menu; the entry without one takes the color away.</summary>
    private void TopicColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.SetTopicColor(topic, tag == TopicViewModel.AutomaticChoice ? null : tag);
    }

    private void TileColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && DataContextOf<TileViewModel>(sender) is { IsOwnFolder: true } tile)
            ViewModel.SetTileColor(tile, tag.Length > 0 ? tag : null);
    }

    private void TileRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { IsOwnFolder: true } tile)
            ViewModel.AskRecycle([tile.FolderPath]);
    }

    private void TopicOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.OpenFolder(topic.Topic.FullPath);
    }

    private void TopicPaste_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            Observe(ViewModel.PasteInto(topic.Topic.FullPath));
    }

    private void TopicPasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            Observe(ViewModel.PasteLinksInto(topic.Topic.FullPath));
    }

    private void TopicNewTile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.NewTile(topic);
    }

    private void TopicNewTopic_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.NewTopic(topic);
    }

    private void TopicRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.BeginRename(topic);
    }

    private void TopicDemote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.DemoteTopic(topic);
    }

    private void TopicRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.AskRecycle([topic.Topic.FullPath]);
    }

    // ---- Helpers ----

    /// <summary>An operation that goes on after the click or the key: an unexpected error still reaches the handler of the app.</summary>
    private static async void Observe(Task operation) => await operation;

    private bool MovedEnough(MouseEventArgs e)
    {
        var moved = e.GetPosition(this) - _dragStart;
        return Math.Abs(moved.X) >= SystemParameters.MinimumHorizontalDragDistance ||
               Math.Abs(moved.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    private static T? DataContextOf<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

    private static bool IsInside<T>(object source) where T : DependencyObject =>
        source is DependencyObject element && FindAncestor<T>(element) is not null;

    /// <summary>The element itself or its nearest ancestor of type <typeparamref name="T"/>.</summary>
    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    /// <summary>Every descendant of type <typeparamref name="T"/>, those inside one another too, in visual order.</summary>
    private static IEnumerable<T> AllDescendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T found)
                yield return found;
            foreach (var deeper in AllDescendants<T>(child))
                yield return deeper;
        }
    }

    /// <summary>The outermost descendants of type <typeparamref name="T"/>, in visual order.</summary>
    private static IEnumerable<T> FindDescendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T found)
            {
                yield return found;
                continue;
            }
            foreach (var deeper in FindDescendants<T>(child))
                yield return deeper;
        }
    }
}
