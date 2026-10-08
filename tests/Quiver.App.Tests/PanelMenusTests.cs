using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.App.Views;

namespace Quiver.App.Tests;

/// <summary>
/// The menus of the panel, built as the right click builds them, with their bindings attached: a menu that cannot bind
/// stops the click with an error. Nothing is shown on screen.
/// </summary>
public sealed class PanelMenusTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public PanelMenusTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Lavoro", "Documenti"));
        File.WriteAllText(Path.Combine(_root, "Lavoro", "Documenti", "a.txt"), "a");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        File.Delete(_root + "-recenti.json");
    }

    [Fact]
    public void EveryMenu_BindsToWhatItsItemsCanRead() => StaThread.Run(() =>
    {
        AppResources.Load();
        using var viewModel = new MainViewModel(_root, live: false, recent: RecentDestinations.Load(_root + "-recenti.json"));
        var view = new PanelView { DataContext = viewModel };
        var tile = viewModel.Tiles.Single(candidate => candidate.Title == "Documenti");
        (string Key, object Context)[] menus =
        [
            ("TopicMenu", viewModel.Topics.Single()),
            ("TileMenu", tile),
            ("ItemMenu", tile.Items.Single()),
            ("BoardMenu", viewModel),
        ];

        foreach (var (key, context) in menus)
        {
            var menu = (ContextMenu)view.FindResource(key);
            menu.DataContext = context;
            // The bindings attach when the data arrives, some of them once the dispatcher gets to them.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            foreach (var item in Items(menu))
                item.ApplyTemplate();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        }
    });

    private static IEnumerable<MenuItem> Items(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var inner in Items(item))
                yield return inner;
        }
    }
}
