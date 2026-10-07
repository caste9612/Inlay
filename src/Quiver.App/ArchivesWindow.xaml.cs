using System.Windows;
using System.Windows.Interop;
using Quiver.App.ViewModels;

namespace Quiver.App;

/// <summary>
/// The window of the archives, as Obsidian's window of its vaults. The first time it greets the user and closes as soon as
/// an archive is chosen; afterwards it stays open, to switch, rename or move archives.
/// </summary>
public partial class ArchivesWindow : Window
{
    private readonly ArchivesViewModel _viewModel;

    public ArchivesWindow(ArchivesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        if (viewModel.IsWelcome)
        {
            Title = "Benvenuto in Quiver";
            viewModel.ArchiveOpened += (_, _) => Close();
        }
        SourceInitialized += (_, _) => viewModel.OwnerWindow = new WindowInteropHelper(this).Handle;
        Loaded += (_, _) => View.FocusStart();
        // A check under way must not go on after the window: the archive stays where it was.
        Closing += (_, _) => _viewModel.CancelCheck();
    }
}
