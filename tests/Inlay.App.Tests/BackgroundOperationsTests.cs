using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App.Tests;

/// <summary>
/// The file operations as the app runs them, on their own thread, with a dispatcher like that of the window: the methods
/// return before the files go, the window gets its messages meanwhile, and what happens to the files is the same.
/// </summary>
public sealed class BackgroundOperationsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

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

    [Fact]
    public void OperationsOnOtherFiles_RunTogether_ThoseOnTheSameFiles_WaitTheirTurn() => RunOnDispatcher(async () =>
    {
        using var viewModel = Open();
        var shell = new HeldShell();
        viewModel.ShellRunner = shell.Run;
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        var casa = Directory.CreateDirectory(Path.Combine(_root, "Personale", "Casa")).FullName;

        var first = viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Move);
        var second = viewModel.Transfer([Path.Combine(documents, "b.txt")], casa, TransferKind.Move);
        // From where the first one puts it, to where the second one puts things: it waits for both.
        var third = viewModel.Transfer([Path.Combine(archive, "a.txt")], casa, TransferKind.Move);

        // The first two touch other files: they are with the shell at the same time.
        Assert.Equal(2, shell.Count);
        Assert.Equal("Sposto «b.txt» in «Casa»… · un'altra in corso · 1 in attesa", viewModel.Activity);
        Assert.Equal(["Sposto «a.txt» in «Archivio»…", "Sposto «b.txt» in «Casa»…", "In attesa: Sposto «a.txt» in «Casa»…"],
            viewModel.ActivityDetails.Split(Environment.NewLine));

        shell.Release(0);
        await first;
        Assert.Equal(2, shell.Count);
        shell.Release(1);
        await second;
        await shell.WaitFor(3);
        Assert.Equal("Sposto «a.txt» in «Casa»…", viewModel.Activity);
        shell.Release(2);
        await third;
        await viewModel.WhenIdle();

        Assert.Equal("a.txt", File.ReadAllText(Path.Combine(casa, "a.txt")));
        Assert.Equal("b.txt", File.ReadAllText(Path.Combine(casa, "b.txt")));
        Assert.False(viewModel.IsBusy);
    });

    [Fact]
    public void CtrlZ_WaitsForEveryOperationUnderWay_AndThoseAskedAfterIt_WaitForIt() => RunOnDispatcher(async () =>
    {
        using var viewModel = Open();
        var shell = new HeldShell();
        viewModel.ShellRunner = shell.Run;
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        var casa = Directory.CreateDirectory(Path.Combine(_root, "Personale", "Casa")).FullName;

        var move = viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Move);
        var undo = viewModel.Undo();
        // Other files, but asked after Ctrl+Z.
        var after = viewModel.Transfer([Path.Combine(documents, "b.txt")], casa, TransferKind.Move);

        Assert.Equal(1, shell.Count);
        Assert.Equal("Sposto «a.txt» in «Archivio»… · 2 in attesa", viewModel.Activity);

        shell.Release(0);
        await move;
        // Ctrl+Z puts the file back through the shell too, while the move asked after it still waits.
        await shell.WaitFor(2);
        Assert.Equal("Rimetto «a.txt» dov'era… · 1 in attesa", viewModel.Activity);
        shell.Release(1);
        await undo;
        await shell.WaitFor(3);
        shell.Release(2);
        await after;

        Assert.True(File.Exists(Path.Combine(documents, "a.txt")));
        Assert.False(File.Exists(Path.Combine(archive, "a.txt")));
        Assert.True(File.Exists(Path.Combine(casa, "b.txt")));
        Assert.Equal("Annulla lo spostamento", viewModel.UndoLabel);
    });

    [Fact]
    public void CtrlZ_TakesBackTheOperationAskedForLast_EvenIfAnOlderOneEndsAfterIt() => RunOnDispatcher(async () =>
    {
        using var viewModel = Open();
        var shell = new HeldShell();
        viewModel.ShellRunner = shell.Run;
        var documents = Path.Combine(_root, "Lavoro", "Documenti");
        var archive = Path.Combine(_root, "Lavoro", "Archivio");
        var casa = Directory.CreateDirectory(Path.Combine(_root, "Personale", "Casa")).FullName;

        // A long copy, then a quick move of other files that ends first.
        var copy = viewModel.Transfer([Path.Combine(documents, "a.txt")], archive, TransferKind.Copy);
        await shell.WaitFor(1);
        var move = viewModel.Transfer([Path.Combine(documents, "b.txt")], casa, TransferKind.Move);
        await shell.WaitFor(2);
        shell.Release(1);
        await move;
        Assert.Equal("Annulla lo spostamento", viewModel.UndoLabel);

        // Ctrl+Z while the copy still runs: it waits for it, and then takes back the move all the same.
        var undo = viewModel.Undo();
        shell.Release(0);
        await copy;
        await shell.WaitFor(3);
        shell.Release(2);
        await undo;

        Assert.True(File.Exists(Path.Combine(documents, "b.txt")));
        Assert.False(File.Exists(Path.Combine(casa, "b.txt")));
        Assert.True(File.Exists(Path.Combine(archive, "a.txt")));
        Assert.Equal("Annulla la copia", viewModel.UndoLabel);
    });

    [Fact]
    public void TwoCopiesThatFitOneAtATime_DoNotRunTogether_WhenTogetherTheyWouldNot() => RunOnDispatcher(async () =>
    {
        var outside = Directory.CreateDirectory(_root + "-fuori").FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(outside, "uno.bin"), new byte[64 * 1024]);
            File.WriteAllBytes(Path.Combine(outside, "due.bin"), new byte[64 * 1024]);
            using var viewModel = Open();
            var shell = new HeldShell();
            viewModel.ShellRunner = shell.Run;
            // A disk with 100 KB left: room for one file, not for both.
            viewModel.DiskOf = _ => new DiskSpace("Z:", 100 * 1024, 100L * 1024 * 1024 * 1024);
            var archive = Path.Combine(_root, "Lavoro", "Archivio");
            var documents = Path.Combine(_root, "Lavoro", "Documenti");

            var first = viewModel.Transfer([Path.Combine(outside, "uno.bin")], archive, TransferKind.Copy);
            await shell.WaitFor(1);
            await viewModel.Transfer([Path.Combine(outside, "due.bin")], documents, TransferKind.Copy);

            Assert.Equal(1, shell.Count);
            var toast = Assert.Single(viewModel.Toasts);
            Assert.Equal("Su Z: non c'è abbastanza spazio", toast.Title);
            Assert.StartsWith("Servono 64,0 KB e, tolto quello che serve alle copie in corso, ne restano 36,0 KB.", toast.Body);
            shell.Release(0);
            await first;
            Assert.True(File.Exists(Path.Combine(archive, "uno.bin")));
            Assert.False(File.Exists(Path.Combine(documents, "due.bin")));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    });

    /// <summary>Live: on a thread of its own for the files, with the recent places of the test, never those of the PC.</summary>
    private MainViewModel Open() => new(_root, live: true, recent: RecentDestinations.Load(_root + "-recenti.json"));

    /// <summary>Holds each operation of the shell until the test lets it go, to see which ones are there at the same time.</summary>
    private sealed class HeldShell
    {
        private readonly List<TaskCompletionSource> _gates = [];

        /// <summary>How many operations reached the shell so far.</summary>
        public int Count => _gates.Count;

        public async Task<bool> Run(Func<bool> operation)
        {
            var gate = new TaskCompletionSource();
            _gates.Add(gate);
            await gate.Task;
            return operation();
        }

        public void Release(int index) => _gates[index].SetResult();

        /// <summary>Lets the window work until so many operations reached the shell.</summary>
        public async Task WaitFor(int count)
        {
            for (var waited = 0; Count < count; waited += 10)
            {
                if (waited > 10_000)
                    throw new TimeoutException($"{Count} operations reached the shell, not {count}.");
                await Task.Delay(10);
            }
        }
    }

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
