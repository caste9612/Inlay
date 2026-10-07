using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Views;

/// <summary>
/// Everything inside the panel. Keyboard: typing anywhere searches, arrows move through the results while the cursor
/// stays in the search box, Enter opens, Ctrl+Enter shows the item in Explorer, F2 renames, Canc moves to the Recycle Bin,
/// Ctrl+Shift+N creates a tile, Esc goes back. Files can be dragged onto tiles, topics and the inbox.
/// </summary>
public partial class PanelView : UserControl
{
    private Point _dragStart;
    private ItemViewModel? _dragCandidate;
    private IInputElement? _focusBeforePrompt;

    public PanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Esc with nothing left to cancel.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Something dragged from the panel went outside it: the panel should make way for the drop target.</summary>
    public event EventHandler? DragLeftPanel;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public void FocusSearch()
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel old)
        {
            old.Results.CollectionChanged -= OnResultsChanged;
            old.PropertyChanged -= OnViewModelPropertyChanged;
        }
        if (e.NewValue is MainViewModel current)
        {
            current.Results.CollectionChanged += OnResultsChanged;
            current.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>Keeps the first result selected, so Enter in the search box opens it.</summary>
    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (ResultsList.SelectedIndex < 0 && ResultsList.Items.Count > 0)
                ResultsList.SelectedIndex = 0;
        }, DispatcherPriority.Background);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.Prompt))
            return;
        if (ViewModel.Prompt is not null)
            _focusBeforePrompt ??= Keyboard.FocusedElement;
        Dispatcher.BeginInvoke(FocusAfterPromptChange, DispatcherPriority.Input);
    }

    private void FocusAfterPromptChange()
    {
        if (ViewModel.Prompt is { } prompt)
        {
            if (prompt.HasInput)
            {
                PromptInput.Focus();
                PromptInput.Select(0, prompt.SelectionLength);
            }
            else
            {
                PromptConfirm.Focus();
            }
            return;
        }
        // Back where the user was. The tree is rebuilt after a change, so a topic gets the focus back through
        // the selection, which follows renamed topics; any other element that disappeared hands over to the search box.
        var previous = _focusBeforePrompt;
        _focusBeforePrompt = null;
        if (previous is TreeViewItem)
            FocusSelectedTopic();
        else if (previous is not UIElement { IsVisible: true } element || !element.Focus())
            FocusSearch();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
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

        if (e.Key == Key.N && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ViewModel.AskNewTile(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
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

        // In a text box F2 and Canc keep their usual meaning.
        if (Keyboard.FocusedElement is TextBox || FocusedPath() is not { } path)
            return;
        if (e.Key == Key.F2)
            ViewModel.AskRename(path);
        else if (e.Key == Key.Delete)
            ViewModel.AskRecycle(path);
        else
            return;
        e.Handled = true;
    }

    /// <summary>The item or topic that has the keyboard focus.</summary>
    private static string? FocusedPath() => Keyboard.FocusedElement switch
    {
        ListBoxItem { DataContext: ItemViewModel item } => item.Item.FullPath,
        TreeViewItem { DataContext: TopicViewModel topic } => topic.Topic.FullPath,
        _ => null,
    };

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (ViewModel.Prompt is not null || Keyboard.FocusedElement is TextBox ||
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
                if ((ResultsList.SelectedItem ?? ViewModel.Results.FirstOrDefault()) is ItemViewModel item)
                    OpenOrReveal(item);
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

    private void TopicTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TopicViewModel topic && !ReferenceEquals(topic, ViewModel.SelectedTopic))
            ViewModel.SelectTopicCommand.Execute(topic);
    }

    private void Entry_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ItemUnder(sender, e.OriginalSource) is { } item)
        {
            ViewModel.Open(item.Item);
            e.Handled = true;
        }
    }

    private void Entry_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is ListBox { SelectedItem: ItemViewModel item })
        {
            OpenOrReveal(item);
            e.Handled = true;
        }
    }

    private void OpenOrReveal(ItemViewModel item)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            ViewModel.Reveal(item.Item);
        else
            ViewModel.Open(item.Item);
    }

    /// <summary>The item at the mouse, ignoring clicks on the scrollbar or on the empty space below the items.</summary>
    private static ItemViewModel? ItemUnder(object list, object source) =>
        list is ListBox listBox && source is DependencyObject element &&
        ItemsControl.ContainerFromElement(listBox, element) is ListBoxItem { DataContext: ItemViewModel item }
            ? item
            : null;

    // ---- Dragging items out of the lists ----

    private void Entry_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        _dragCandidate = ItemUnder(sender, e.OriginalSource);
    }

    private void Entry_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is not { } item || e.LeftButton != MouseButtonState.Pressed)
            return;
        var moved = e.GetPosition(this) - _dragStart;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragCandidate = null;
        // A plain file list: other programs (Explorer, mail, browsers) understand it too.
        var data = new DataObject(DataFormats.FileDrop, new[] { item.Item.FullPath });
        DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
    }

    // ---- Dropping files onto tiles, topics and the inbox ----

    /// <summary>Outside the drop targets nothing can be dropped.</summary>
    private void OnPreviewDragOver(object sender, DragEventArgs e) => e.Effects = DragDropEffects.None;

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(this);
        if (position.X <= 0 || position.Y <= 0 || position.X >= ActualWidth || position.Y >= ActualHeight)
            DragLeftPanel?.Invoke(this, EventArgs.Empty);
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        var kind = PlanDrop(sender, e, out _, out _);
        e.Effects = kind switch
        {
            TransferKind.Move => DragDropEffects.Move,
            TransferKind.Copy => DragDropEffects.Copy,
            _ => DragDropEffects.None,
        };
        SetDropHighlight(sender, kind != TransferKind.None);
        e.Handled = true;
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e) => SetDropHighlight(sender, false);

    private void DropTarget_Drop(object sender, DragEventArgs e)
    {
        var kind = PlanDrop(sender, e, out var paths, out var destination);
        SetDropHighlight(sender, false);
        // Quiver does the move or copy itself, so the program the files come from must not delete anything.
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (kind == TransferKind.None)
            return;
        // Let the drag finish first: a long copy must not keep the other program waiting.
        Dispatcher.BeginInvoke(() => ViewModel.Transfer(paths, destination, kind));
    }

    private TransferKind PlanDrop(object target, DragEventArgs e, out string[] paths, out string destination)
    {
        paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? [];
        destination = (target as FrameworkElement)?.DataContext switch
        {
            TileViewModel tile => tile.FolderPath,
            TopicViewModel topic => topic.Topic.FullPath,
            MainViewModel main => main.InboxPath,
            _ => "",
        };
        if (paths.Length == 0 || destination.Length == 0)
            return TransferKind.None;

        var kind = TransferRules.Choose(paths, destination,
            forceCopy: e.KeyStates.HasFlag(DragDropKeyStates.ControlKey),
            forceMove: e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey));
        // Some sources only allow copying, others only moving.
        if (kind == TransferKind.Move && !e.AllowedEffects.HasFlag(DragDropEffects.Move))
            kind = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? TransferKind.Copy : TransferKind.None;
        if (kind == TransferKind.Copy && !e.AllowedEffects.HasFlag(DragDropEffects.Copy))
            kind = TransferKind.None;
        return kind;
    }

    private void SetDropHighlight(object target, bool on)
    {
        // Clearing the local value brings back the template's own look, selection included.
        switch (target)
        {
            case TreeViewItem item when item.Template?.FindName("Row", item) is Border row:
                if (on)
                    row.Background = (System.Windows.Media.Brush)FindResource("AccentSoftBrush");
                else
                    row.ClearValue(Border.BackgroundProperty);
                break;
            case Border tile:
                if (on)
                    tile.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
                else
                    tile.ClearValue(Border.BorderBrushProperty);
                break;
            case Control control:
                if (on)
                    control.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
                else
                    control.ClearValue(BorderBrushProperty);
                break;
        }
    }

    // ---- Menus ----

    private void NewTopic_Click(object sender, RoutedEventArgs e) => ViewModel.AskNewTopic(null);

    private void NewTile_Click(object sender, RoutedEventArgs e) => ViewModel.AskNewTile(null);

    private void ItemOpen_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.Open(item.Item);
    }

    private void ItemReveal_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.Reveal(item.Item);
    }

    private void ItemCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.CopyPath(item.Item);
    }

    private void ItemRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.AskRename(item.Item.FullPath);
    }

    private void ItemRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<ItemViewModel>(sender) is { } item)
            ViewModel.AskRecycle(item.Item.FullPath);
    }

    private void TileOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.OpenFolder(tile.FolderPath);
    }

    private void TileRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { IsOwnFolder: true } tile)
            ViewModel.AskRename(tile.FolderPath);
    }

    private void TileRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { IsOwnFolder: true } tile)
            ViewModel.AskRecycle(tile.FolderPath);
    }

    private void TilePromote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TileViewModel>(sender) is { } tile)
            ViewModel.PromoteTile(tile);
    }

    private void TopicOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.OpenFolder(topic.Topic.FullPath);
    }

    private void TopicNewTile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.AskNewTile(topic);
    }

    private void TopicNewTopic_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.AskNewTopic(topic);
    }

    private void TopicRename_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.AskRename(topic.Topic.FullPath);
    }

    private void TopicRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.AskRecycle(topic.Topic.FullPath);
    }

    private void TopicDemote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContextOf<TopicViewModel>(sender) is { } topic)
            ViewModel.DemoteTopic(topic);
    }

    private static T? DataContextOf<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;
}
