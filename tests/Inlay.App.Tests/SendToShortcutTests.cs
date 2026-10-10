using Inlay.App.Interop;
using Inlay.App.Services;

namespace Inlay.App.Tests;

/// <summary>«Inlay» in the «Invia a» menu, made in a folder of the test, never in the real SendTo folder.</summary>
public sealed class SendToShortcutTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Installed_ItStartsThisInlayWithSort_AndCanBeRemoved() => StaThread.Run(() =>
    {
        Assert.False(SendToShortcut.IsPresent(_folder));
        Assert.False(SendToShortcut.IsInstalled(_folder));

        Assert.True(SendToShortcut.TryInstall(_folder));

        var link = Path.Combine(_folder, "Inlay.lnk");
        Assert.True(SendToShortcut.IsPresent(_folder));
        Assert.True(SendToShortcut.IsInstalled(_folder));
        Assert.Equal(Environment.ProcessPath, ShellLinks.TargetOf(link), StringComparer.OrdinalIgnoreCase);
        Assert.Equal(SendToShortcut.Arguments, ShellLinks.ArgumentsOf(link));

        Assert.True(SendToShortcut.TryRemove(_folder));
        Assert.False(File.Exists(link));
        Assert.True(SendToShortcut.TryRemove(_folder));
    });

    [Fact]
    public void AShortcutToAnotherProgram_DoesNotCountAsInstalled() => StaThread.Run(() =>
    {
        Directory.CreateDirectory(_folder);
        ShellLinks.Create(Path.Combine(_folder, "Inlay.lnk"), Path.Combine(Environment.SystemDirectory, "notepad.exe"), "--sort");

        Assert.True(SendToShortcut.IsPresent(_folder));
        Assert.False(SendToShortcut.IsInstalled(_folder));
    });
}
