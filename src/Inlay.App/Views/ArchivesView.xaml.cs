using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Inlay.App.ViewModels;

namespace Inlay.App.Views;

/// <summary>The archives of this PC: the list on the left, a new archive or a folder to open on the right.</summary>
public partial class ArchivesView : UserControl
{
    public ArchivesView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ArchivesViewModel old)
                old.PropertyChanged -= OnViewModelPropertyChanged;
            if (e.NewValue is ArchivesViewModel current)
                current.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    private ArchivesViewModel ViewModel => (ArchivesViewModel)DataContext;

    /// <summary>Where the keyboard starts: the name of a new archive the first time, the list afterwards.</summary>
    public void FocusStart()
    {
        if (ViewModel.HasArchives && !ViewModel.IsWelcome)
            ArchiveList.Focus();
        else
        {
            NewNameBox.Focus();
            NewNameBox.SelectAll();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel.Prompt is not { } prompt)
            return;
        if (e.Key == Key.Escape)
            prompt.CancelCommand.Execute(null);
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
            prompt.ConfirmCommand.Execute(null);
        else
            return;
        e.Handled = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ArchivesViewModel.Prompt))
            return;
        Dispatcher.BeginInvoke(() =>
        {
            if (ViewModel.Prompt is not { } prompt)
                FocusStart();
            else if (prompt.HasInput)
            {
                PromptInput.Focus();
                PromptInput.Select(0, prompt.SelectionLength);
            }
            else
                (prompt.IsDanger ? PromptConfirmDanger : PromptConfirm).Focus();
        }, DispatcherPriority.Input);
    }

    private void Create_Click(object sender, RoutedEventArgs e) => ViewModel.CreateNew();

    private void BrowseParent_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("Dove creare l'archivio", ViewModel.NewParent) is { } folder)
            ViewModel.NewParent = folder;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder("La cartella da usare come archivio", null) is { } folder)
            ViewModel.OpenFolder(folder);
    }

    private void ArchiveList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ArchiveList, (DependencyObject)e.OriginalSource) is ListBoxItem { DataContext: ArchiveEntryViewModel entry })
            ViewModel.Open(entry);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } entry)
            ViewModel.Open(entry);
    }

    private void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } entry)
            ViewModel.Reveal(entry);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } entry)
            ViewModel.AskRename(entry);
    }

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } entry && PickFolder($"Dove spostare «{entry.Name}»", Path.GetDirectoryName(entry.Path)) is { } folder)
            ViewModel.AskMove(entry, folder);
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Selected is { } entry)
            ViewModel.Forget(entry);
    }

    private void CancelCheck_Click(object sender, RoutedEventArgs e) => ViewModel.CancelCheck();

    private string? PickFolder(string title, string? start)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (start is not null && Directory.Exists(start))
            dialog.InitialDirectory = start;
        return dialog.ShowDialog(Window.GetWindow(this)) == true ? dialog.FolderName : null;
    }
}
