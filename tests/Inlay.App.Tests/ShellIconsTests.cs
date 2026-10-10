using System.Windows.Media;
using Inlay.App.Services;

namespace Inlay.App.Tests;

/// <summary>The icons of Windows are read on a thread of their own and delivered on the thread that asked.</summary>
public sealed class ShellIconsTests
{
    [Fact]
    public void AProgramsIcon_ArrivesOnTheThreadThatAsked_FrozenAndThenKnown() => StaThread.RunAsync(async () =>
    {
        var program = Environment.ProcessPath!;
        var asked = Environment.CurrentManagedThreadId;
        var delivered = new TaskCompletionSource<(ImageSource? Icon, int Thread)>();
        ShellIcons.Load(program, isFolder: false, icon => delivered.SetResult((icon, Environment.CurrentManagedThreadId)));

        var (icon, thread) = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(icon);
        Assert.True(icon.IsFrozen);
        Assert.Equal(asked, thread);
        Assert.True(ShellIcons.TryGet(program, isFolder: false, out var known));
        Assert.Same(icon, known);
    });

    [Fact]
    public void FilesOfAKind_ShareOneIcon_ReadOnce() => StaThread.RunAsync(async () =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // An extension Windows knows, so that it has an icon for it, asked for two files at once.
            var first = Path.Combine(folder, "uno.inlaytest.txt");
            var second = Path.Combine(folder, "due.inlaytest.txt");
            var icons = new TaskCompletionSource<ImageSource?>[] { new(), new() };
            ShellIcons.Load(first, isFolder: false, icon => icons[0].SetResult(icon));
            ShellIcons.Load(second, isFolder: false, icon => icons[1].SetResult(icon));

            var both = await Task.WhenAll(icons[0].Task, icons[1].Task).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(both[0]);
            Assert.Same(both[0], both[1]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });
}
