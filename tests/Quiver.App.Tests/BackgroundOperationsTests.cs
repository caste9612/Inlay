using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Tests;

/// <summary>
/// The file operations as the app runs them, on their own thread, with a dispatcher like that of the window: the methods
/// return before the files go, the window gets its messages meanwhile, and what happens to the files is the same.
/// </summary>
public sealed class BackgroundOperationsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public BackgroundOperationsTests()
    {
        foreach (var name in new[] { "a.txt", "b.txt" })
        {
            var path = Path.Combine(_root, "Lavoro", "Documenti", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, name);
        }
        Directory.CreateDirectory(Path.Combine(_root, "Lavoro", "Archivio"));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        File.Delete(_root + "-recenti.json");
    }

    [Fact]
    public void AMove_LeavesTheWindowFree_AndCtrlZGivenMeanwhile_WaitsAndTakesItBack() => RunOnDispatcher(async () =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        var busy = new List<bool>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsBusy))
                busy.Add(viewModel.IsBusy);
        };

        var move = viewModel.Transfer([Path.Combine(documents, "a.txt"), Path.Combine(documents, "b.txt")], archive, TransferKind.Move);
        var undo = viewModel.Undo();
        // Both came back at once: the move goes on its own thread, and the undo waits for it.
        Assert.False(move.IsCompleted);
        Assert.False(undo.IsCompleted);
        Assert.True(viewModel.IsBusy);
        Assert.StartsWith("Sposto 2 elementi", viewModel.Status);
        // Meanwhile the window gets its messages.
        var heard = await Dispatcher.CurrentDispatcher.InvokeAsync(() => viewModel.IsBusy);
        Assert.True(heard);

        await move;
        await undo;
        await viewModel.WhenIdle();

        Assert.True(File.Exists(Path.Combine(documents, "a.txt")));
        Assert.True(File.Exists(Path.Combine(documents, "b.txt")));
        Assert.Empty(Directory.GetFileSystemEntries(archive));
        Assert.StartsWith("Spostamento annullato", viewModel.Status);
        Assert.False(viewModel.IsBusy);
        Assert.Equal([true, false], busy);
        Assert.False(viewModel.CanUndo);
    });

    [Fact]
    public void TwoMovesInARow_GoOneAfterTheOther_EachFromWhereThePreviousLeftTheFiles() => RunOnDispatcher(async () =>
    {
        using var viewModel = Open();
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        var personale = Directory.CreateDirectory(Path.Combine(_root, "Personale", "Casa")).FullName;

        var first = viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Move);
        var second = viewModel.Transfer([Path.Combine(archive, "a.txt")], personale, TransferKind.Move);
        await Task.WhenAll(first, second);

        Assert.Equal("a.txt", File.ReadAllText(Path.Combine(personale, "a.txt")));
        Assert.False(File.Exists(Path.Combine(archive, "a.txt")));
        // Each move is a step of its own: Ctrl+Z takes back the second, then the first.
        await viewModel.Undo();
        Assert.True(File.Exists(Path.Combine(archive, "a.txt")));
        await viewModel.Undo();
        Assert.True(File.Exists(Path.Combine(documents, "a.txt")));
    });

    /// <summary>Live: on a thread of its own for the files, with the recent places of the test, never those of the PC.</summary>
    private MainViewModel Open() => new(_root, live: true, recent: RecentDestinations.Load(_root + "-recenti.json"));

    /// <summary>Runs the test on an STA thread with a running dispatcher, as the window's, and gives back its failure.</summary>
    private static void RunOnDispatcher(Func<Task> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await test();
                }
                catch (Exception e)
                {
                    failure = e;
                }
                finally
                {
                    frame.Continue = false;
                }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(1)))
            throw new TimeoutException("The test did not end within a minute.");
        if (failure is not null)
            ExceptionDispatchInfo.Throw(failure);
    }
}
