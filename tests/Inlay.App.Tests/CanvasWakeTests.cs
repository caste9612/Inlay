using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.App.Views;

namespace Inlay.App.Tests;

/// <summary>The canvas as the window shows it, without a window: laid out, then left to its dispatcher for a while.</summary>
[Collection(AppViews.Name)]
public sealed class CanvasWakeTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public CanvasWakeTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Microscopi Test", "Report", "Report-Anomalie-Microscopio"));
        foreach (var name in new[] { "Campagna.html", "report.md", "Riassunto.html", "Sessione.html" })
            File.WriteAllText(Path.Combine(_root, "Microscopi Test", "Report", name), name);
        Directory.CreateDirectory(Path.Combine(_root, "Alkeria", "Questioni aperte"));
        File.WriteAllText(Path.Combine(_root, "Alkeria", "Questioni aperte", "Da comunicare.txt"), "x");
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        File.Delete(_root + "-recenti.json");
    }

    [Fact]
    public void OpenedOnTheCanvas_TheTilesInViewGetTheirRows_ByThemselves() => StaThread.Run(() =>
    {
        AppResources.Load();
        using var viewModel = new MainViewModel(_root, live: false, recent: RecentDestinations.Load(_root + "-recenti.json"))
        {
            IsCanvasShown = true,
        };
        var view = new PanelView { DataContext = viewModel };
        var size = new Size(2560, 1400);
        view.Measure(size);
        view.Arrange(new Rect(size));
        // The rows come after a pause, longer when the PC is busy with other tests: waited for, up to a limit.
        PumpUntil(() => Descendants<TileList>(view).Any() && Descendants<TileList>(view).All(list => !list.IsAsleep),
            TimeSpan.FromSeconds(10));

        var lists = Descendants<TileList>(view).ToList();
        Assert.NotEmpty(lists);
        Assert.All(lists, list => Assert.False(list.IsAsleep,
            $"“{(list.DataContext as TileViewModel)?.Title}” without rows: zoom {viewModel.Canvas.Camera.Zoom:P0}, level {viewModel.Canvas.Level}"));
        var report = lists.Single(list => (list.DataContext as TileViewModel)?.Title == "Report");
        Assert.Equal(5, report.Items.Count);
    });

    /// <summary>Lets the dispatcher run as that of the window would, layout, the fit of the canvas and timers, until done.</summary>
    private static void PumpUntil(Func<bool> done, TimeSpan limit)
    {
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (!done() && DateTime.UtcNow - started < limit)
                return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T found)
                yield return found;
            foreach (var deeper in Descendants<T>(child))
                yield return deeper;
        }
    }
}
