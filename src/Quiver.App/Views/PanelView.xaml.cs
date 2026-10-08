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
using Quiver.App.Interop;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Views;

/// <summary>
/// Everything inside the panel, used like the desktop: a click selects (Ctrl and Maiusc for more), a double-click or Invio
/// opens, files and tiles are dragged where they belong, F2 or a slow second click renames in place, Canc moves to the
/// Recycle Bin, Ctrl+C, Ctrl+X and Ctrl+V work with Explorer too. Typing anywhere searches the whole archive.
/// </summary>
public partial class PanelView : UserControl
{
    /// <summary>Data of a tile dragged by its title: its folder.</summary>
    private const string TileFormat = "Quiver.Tile";

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
    private IInputElement? _focusBeforePrompt;
    private DragLabelAdorner? _dragLabel;
    private InsertionAdorner? _insertionLine;

    // Where the keyboard focus was, to put it back when a reload replaces the element that had it.
    private bool _focusWasInTree;
    private string? _focusedTile;

    public PanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        _renameTimer.Tick += RenameTimer_Tick;
        _springTimer.Tick += SpringTimer_Tick;
    }

    /// <summary>Esc with nothing left to cancel, or the close button.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Something dragged from the panel went outside it: the panel should make way for the drop target.</summary>
    public event EventHandler? DragLeftPanel;

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
        }
        if (e.NewValue is MainViewModel current)
        {
            current.Results.CollectionChanged += OnResultsChanged;
            current.PropertyChanged += OnViewModelPropertyChanged;
            current.SelectionRestored += OnSelectionRestored;
            current.Reloaded += OnReloaded;
        }
    }

    // ---- Keyboard ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _renameTimer.Stop();
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
                ViewModel.Undo();
            else if (key == Key.V && FileClipboard.HasFiles && ViewModel.BoardFolder is { } board)
                ViewModel.PasteInto(board);
            else
                return;
            e.Handled = true;
            return;
        }

        switch (key)
        {
            case Key.Z when modifiers == ModifierKeys.Control:
                ViewModel.Undo();
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
                    ViewModel.PasteInto(folder);
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

    /// <summary>Keeps the first result selected, so Invio in the search box opens it.</summary>
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

    // ---- Topics ----

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TopicViewModel topic && !ReferenceEquals(topic, ViewModel.SelectedTopic))
            ViewModel.SelectTopicCommand.Execute(topic);
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
        if (e.AddedItems.Count > 0 && sender is ListBox { IsKeyboardFocusWithin: true } source &&
            !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            UnselectAll(except: source);
    }

    /// <param name="except">A list whose selection stays.</param>
    private void UnselectAll(ListBox? except = null)
    {
        var keep = except?.ItemsSource;
        foreach (var tile in ViewModel.Tiles.Where(tile => !ReferenceEquals(tile.Items, keep)))
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
        var paths = ViewModel.SelectedItems().Select(selected => selected.Item.FullPath).ToArray();
        // A plain list of files: Explorer, mail and other programs understand it too.
        var data = new DataObject(DataFormats.FileDrop, paths.Length > 0 ? paths : [item.Item.FullPath]);
        ShowDragLabel(paths.Length > 1 ? $"{paths.Length} elementi" : item.Name);
        try
        {
            DragDrop.DoDragDrop(list, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        }
        finally
        {
            HideDragLabel();
        }
    }

    private void ItemList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInside<TextBox>(e.OriginalSource) || ItemUnder(sender, e.OriginalSource) is not { } item)
            return;
        _renameTimer.Stop();
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
        if (Keyboard.Modifiers == ModifierKeys.None)
            UnselectAll();
        // Ctrl+V then pastes where the click was: in that tile, or on the board.
        if (e.OriginalSource is DependencyObject source && FindAncestor<GroupBox>(source) is { } tileBox &&
            FindDescendants<ListBox>(tileBox).FirstOrDefault() is { } list)
            list.Focus();
        else
            BoardArea.Focus();
    }

    private static ItemViewModel? ItemUnder(object list, object source) =>
        list is ListBox listBox && source is DependencyObject element &&
        ItemsControl.ContainerFromElement(listBox, element) is ListBoxItem { DataContext: ItemViewModel item }
            ? item
            : null;

    /// <summary>The row of the first selected item, on the board or among the results.</summary>
    private ListBoxItem? FirstSelectedRow()
    {
        IEnumerable<ListBox> lists = ViewModel.IsSearching ? [ResultsList] : FindDescendants<ListBox>(BoardScroller);
        foreach (var list in lists)
        {
            if (list.Items.OfType<ItemViewModel>().FirstOrDefault(item => item.IsSelected) is { } selected &&
                list.ItemContainerGenerator.ContainerFromItem(selected) is ListBoxItem row)
                return row;
        }
        return null;
    }

    private ListBox? ListOfTile(string? folder) =>
        folder is null
            ? null
            : FindDescendants<ListBox>(BoardScroller).FirstOrDefault(list =>
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
        if (!ViewModel.Tiles.Contains(tile))
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

    // ---- Dropping ----

    private enum DropAction
    {
        None,
        Transfer,
        MoveTile,

        /// <summary>Files that are not on disk yet, like a mail attachment: they are saved, a copy of the original.</summary>
        SaveDropped,
    }

    private sealed record DropPlan(DropAction Action, string[] Paths, string Destination, TransferKind Kind, TileViewModel? Tile, TileViewModel? Before)
    {
        public static readonly DropPlan Nothing = new(DropAction.None, [], "", TransferKind.None, null, null);
    }

    /// <summary>Outside the drop targets nothing can be dropped; a target that wants the line shows it again right after.</summary>
    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        HideInsertionLine();
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X > 0 && position.Y > 0 && position.X < ActualWidth && position.Y < ActualHeight)
            return;
        // Windows also says the drag left when another window comes between, like a tooltip or the progress of a file
        // operation: only the pointer tells whether it really went out of the panel.
        if (Window.GetWindow(this) is { } window && WindowInfo.IsCursorOn(new WindowInteropHelper(window).Handle))
        {
            Log.Write($"Drag over another window, still on the panel: {WindowInfo.UnderCursor()}");
            return;
        }
        HideInsertionLine();
        DisarmSpring();
        DragLeftPanel?.Invoke(this, EventArgs.Empty);
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        ArmSpring(sender, e);
        var plan = PlanDrop(sender, e);
        e.Effects = plan.Action switch
        {
            DropAction.MoveTile => DragDropEffects.Move,
            DropAction.SaveDropped => DragDropEffects.Copy,
            DropAction.Transfer when plan.Kind == TransferKind.Copy => DragDropEffects.Copy,
            DropAction.Transfer when plan.Kind == TransferKind.Link => DragDropEffects.Link,
            DropAction.Transfer => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };
        // Files light up the border of their target; a tile on its way shows where it will land with a line.
        SetDropHighlight(sender, plan.Action is DropAction.Transfer or DropAction.SaveDropped);
        if (plan.Action == DropAction.MoveTile)
            ShowInsertionLine(plan.Before);
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
        // Quiver does the move or copy itself, so the program the files come from must not delete anything.
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        // Let the drag finish first: a long copy must not keep the other program waiting.
        switch (plan.Action)
        {
            case DropAction.MoveTile:
                Dispatcher.BeginInvoke(() => ViewModel.MoveTile(plan.Tile!, plan.Before));
                break;
            case DropAction.Transfer:
                Dispatcher.BeginInvoke(() => ViewModel.Transfer(plan.Paths, plan.Destination, plan.Kind));
                break;
            case DropAction.SaveDropped:
                // Now or never: the content is only there while the drop lasts.
                ViewModel.SaveDropped(e.Data, plan.Destination);
                break;
        }
    }

    /// <summary>
    /// Holding a drag over a topic in the sidebar, over a sub-topic button or over "Da smistare" opens it after a moment,
    /// so the files can go straight into one of its tiles.
    /// </summary>
    private void ArmSpring(object target, DragEventArgs e)
    {
        // A tile dragged by its title moves into the topic it is dropped on: its own board must stay. The board's
        // background shares the panel's data with the "Da smistare" row, and opens nothing.
        var opens = !e.Data.GetDataPresent(TileFormat) && !ReferenceEquals(target, BoardArea) &&
                    ((target as FrameworkElement)?.DataContext switch
                    {
                        TopicViewModel topic => !ReferenceEquals(topic, ViewModel.SelectedTopic),
                        MainViewModel => !ViewModel.IsInboxShown,
                        _ => false,
                    });
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
        switch (target)
        {
            case TopicViewModel topic:
                topic.IsExpanded = true;
                ViewModel.SelectTopicCommand.Execute(topic);
                break;
            case MainViewModel:
                ViewModel.ShowInboxCommand.Execute(null);
                break;
        }
    }

    private DropPlan PlanDrop(object target, DragEventArgs e)
    {
        var onBoard = ReferenceEquals(target, BoardArea);
        var context = (target as FrameworkElement)?.DataContext;

        // A tile dragged by its title and dropped on its board changes place there.
        if (e.Data.GetDataPresent(TileFormat) && (onBoard || context is TileViewModel) &&
            e.Data.GetData(TileFormat) is string tilePath &&
            ViewModel.Tiles.FirstOrDefault(tile => string.Equals(tile.FolderPath, tilePath, StringComparison.OrdinalIgnoreCase)) is { } moving)
        {
            if (onBoard)
                return new DropPlan(DropAction.MoveTile, [], "", TransferKind.None, moving, null);
            return context is TileViewModel { IsOwnFolder: true } before && !ReferenceEquals(before, moving)
                ? new DropPlan(DropAction.MoveTile, [], "", TransferKind.None, moving, before)
                : DropPlan.Nothing;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        var destination = onBoard
            ? ViewModel.BoardFolder
            : context switch
            {
                TileViewModel tile => tile.FolderPath,
                TopicViewModel topic => topic.Topic.FullPath,
                MainViewModel main => main.InboxPath,
                _ => null,
            };
        if (destination is null)
            return DropPlan.Nothing;
        // An attachment from Outlook, a link or an image from the browser: no file on disk yet, only its content.
        if (paths.Length == 0 && VirtualFiles.ArePresent(e.Data) && e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            return new DropPlan(DropAction.SaveDropped, [], destination, TransferKind.Copy, null, null);
        if (paths.Length == 0)
            return DropPlan.Nothing;

        var kind = TransferRules.Choose(paths, destination,
            forceCopy: e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            forceMove: e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey),
            forceLink: e.KeyStates.HasFlag(DragDropKeyStates.AltKey),
            // What waits to be sorted is moved even to another drive, the Desktop's files too: sorting it is the point.
            preferMove: paths.All(ViewModel.IsInInbox),
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
            : new DropPlan(DropAction.Transfer, paths, destination, kind, null, null);
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

    /// <summary>The line where the dragged tile will land: before <paramref name="before"/>, or after the last tile.</summary>
    private void ShowInsertionLine(TileViewModel? before)
    {
        if (_insertionLine is null)
        {
            if (AdornerLayer.GetAdornerLayer(TilesControl) is not { } layer)
                return;
            _insertionLine = new InsertionAdorner(TilesControl, (Brush)FindResource("Brace.Accent"));
            layer.Add(_insertionLine);
        }
        var target = before is null ? null : TilesControl.ItemContainerGenerator.ContainerFromItem(before) as FrameworkElement;
        var last = ViewModel.Tiles.Count == 0 ? null : TilesControl.ItemContainerGenerator.ContainerFromItem(ViewModel.Tiles[^1]) as FrameworkElement;
        var spacing = FindDescendants<TileGridPanel>(TilesControl).FirstOrDefault()?.Spacing ?? 16;
        _insertionLine.ShowBefore(target, last, spacing);
    }

    private void HideInsertionLine() => _insertionLine?.Hide();

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
                ViewModel.Status = problem;
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
            ViewModel.Status = problem;
        }
    }, DispatcherPriority.Input);

    /// <summary>The field closed: the focus goes back to the item, the tile or the topic.</summary>
    private void FocusAfterEdit() => Dispatcher.BeginInvoke(RestoreFocusIfLost, DispatcherPriority.Background);

    // ---- Questions over the board: confirmations and «Sposta in…» ----

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

    private void Archives_Click(object sender, RoutedEventArgs e) => ArchivesRequested?.Invoke(this, EventArgs.Empty);

    private void NewTopic_Click(object sender, RoutedEventArgs e) => ViewModel.NewTopic(null);

    private void NewTile_Click(object sender, RoutedEventArgs e) => ViewModel.NewTile();

    private void Undo_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();

    private void NoticeUndo_Click(object sender, RoutedEventArgs e) => ViewModel.Undo();

    private void NoticeShow_Click(object sender, RoutedEventArgs e) => ViewModel.ShowNotice();

    private void BoardPaste_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            ViewModel.PasteInto(folder);
    }

    private void BoardPasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BoardFolder is { } folder)
            ViewModel.PasteLinksInto(folder);
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

    /// <summary>"Incolla" is available only when the clipboard holds files.</summary>
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
            ViewModel.PasteInto(tile.FolderPath);
    }

    private void TilePasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.PasteLinksInto(tile.FolderPath);
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
            ViewModel.PasteInto(topic.Topic.FullPath);
    }

    private void TopicPasteLink_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.PasteLinksInto(topic.Topic.FullPath);
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
