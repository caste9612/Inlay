using System.Windows;
using System.Windows.Interop;
using Quiver.App.Interop;
using Quiver.App.ViewModels;

namespace Quiver.App;

/// <summary>
/// The window of the settings, with the archives managed as Obsidian's vaults. The first time it greets the user and
/// closes as soon as an archive is chosen; afterwards it stays open, with its pages.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = ViewModel = viewModel;
        if (viewModel.IsWelcome)
        {
            Title = "Benvenuto in Quiver";
            viewModel.Archives.ArchiveOpened += (_, _) => Close();
        }
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            viewModel.Archives.OwnerWindow = handle;
            WindowCorners.MakeSmall(handle);
        };
        Loaded += (_, _) => View.FocusStart();
        // A check under way must not go on after the window: the archive stays where it was.
        Closing += (_, _) => ViewModel.Archives.CancelCheck();
    }

    public SettingsViewModel ViewModel { get; }

    /// <summary>Brings the window forward on the page asked for.</summary>
    public void Show(SettingsSection section)
    {
        ViewModel.Section = section;
        Show();
        Activate();
        View.FocusStart();
    }
}
