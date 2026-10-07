using System.Runtime.ExceptionServices;
using Quiver.App.Interop;

namespace Quiver.App.Tests;

/// <summary>
/// Runs the shell operations for real in a temporary folder, without any dialog.
/// Deleting is not tested here: it would fill the Recycle Bin.
/// </summary>
public sealed class ShellFileOperationsTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Move_PutsTheItemsInTheDestination()
    {
        var file = AddFile("A/uno.txt");
        var folder = AddFile("A/Sotto/due.txt");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;

        Assert.True(OnStaThread(() => ShellFileOperations.Move([file, Path.GetDirectoryName(folder)!], destination, 0, interactive: false)));

        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(destination, "uno.txt")));
        Assert.True(File.Exists(Path.Combine(destination, "Sotto", "due.txt")));
    }

    [Fact]
    public void Copy_KeepsTheOriginal()
    {
        var file = AddFile("A/uno.txt", "contenuto");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "B")).FullName;

        Assert.True(OnStaThread(() => ShellFileOperations.Copy([file], destination, 0, interactive: false)));

        Assert.True(File.Exists(file));
        Assert.Equal("contenuto", File.ReadAllText(Path.Combine(destination, "uno.txt")));
    }

    [Fact]
    public void Rename_WorksForFilesAndFolders_EvenWhenOnlyTheCaseChanges()
    {
        var file = AddFile("A/vecchio.txt");
        var folder = Path.Combine(_root, "A");

        Assert.True(OnStaThread(() => ShellFileOperations.Rename(file, Path.Combine(folder, "nuovo.txt"), 0, interactive: false)));
        Assert.True(OnStaThread(() => ShellFileOperations.Rename(folder, Path.Combine(_root, "a"), 0, interactive: false)));

        Assert.Equal(new[] { "a" }, Directory.GetDirectories(_root).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_root, "a", "nuovo.txt")));
    }

    private string AddFile(string relativePath, string content = "")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The shell wants a single-threaded apartment, like the UI thread of the app.</summary>
    private static T OnStaThread<T>(Func<T> action)
    {
        T result = default!;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception e)
            {
                error = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }
}
