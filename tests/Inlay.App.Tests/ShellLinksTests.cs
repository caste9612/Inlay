using System.Diagnostics;
using Inlay.App.Interop;
using Inlay.Core;

namespace Inlay.App.Tests;

/// <summary>Shortcuts made for real in a temporary folder.</summary>
public sealed class ShellLinksTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AShortcut_PointsToItsTarget_AndIsNamedAfterIt() => StaThread.Run(() =>
    {
        var target = AddFile("Server/Offerta.pdf");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Riquadro")).FullName;

        var link = ShellLinks.CreateIn(folder, target);

        Assert.Equal("Offerta.pdf.lnk", Path.GetFileName(link));
        Assert.Equal("Offerta.pdf", new ArchiveItem(link, Path.GetFileName(link), IsFolder: false).DisplayName);
        Assert.Equal(target, ShellLinks.TargetOf(link), StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(target));
    });

    [Fact]
    public void ShortcutsToFolders_Work_AndNamesDoNotCollide() => StaThread.Run(() =>
    {
        var target = Directory.CreateDirectory(Path.Combine(_root, "Server", "Commessa 2026")).FullName;
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Riquadro")).FullName;

        ShellLinks.CreateIn(folder, target);
        var second = ShellLinks.CreateIn(folder, target);

        Assert.Equal("Commessa 2026 (2).lnk", Path.GetFileName(second));
        Assert.Equal(target, ShellLinks.TargetOf(second), StringComparer.OrdinalIgnoreCase);
    });

    [Fact]
    public void ShortcutsToADriveOrAShare_AreNamedAfterThem_AndStayInTheFolder() => StaThread.Run(() =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Riquadro")).FullName;
        var drive = Path.GetPathRoot(_root)!;

        var link = ShellLinks.CreateIn(folder, drive);

        Assert.Equal(folder, Path.GetDirectoryName(link));
        Assert.EndsWith($"({drive[0]}).lnk", Path.GetFileName(link));
        Assert.Equal("commesse su server", ShellLinks.NameFor(@"\\server\commesse\"));
    });

    [Fact]
    public void AProgram_IsNamedAsItPresentsItself() => StaThread.Run(() =>
    {
        var program = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Programmi")).FullName;

        var link = ShellLinks.CreateIn(folder, program);

        var description = FileVersionInfo.GetVersionInfo(program).FileDescription;
        var expected = string.IsNullOrWhiteSpace(description) ? "notepad" : description.Trim();
        Assert.Equal(expected + ".lnk", Path.GetFileName(link));
    });

    [Fact]
    public void AShortcut_IsCopied_NotLinkedAgain() => StaThread.Run(() =>
    {
        var program = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var original = Path.Combine(_root, "Desktop", "Note.lnk");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        ShellLinks.Create(original, program);
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Programmi")).FullName;

        var copy = ShellLinks.CreateIn(folder, original);

        Assert.Equal("Note.lnk", Path.GetFileName(copy));
        Assert.Equal(program, ShellLinks.TargetOf(copy), StringComparer.OrdinalIgnoreCase);
    });

    [Fact]
    public void AShortcut_CanCarryArguments_AndADescription() => StaThread.Run(() =>
    {
        var plain = Path.Combine(_root, "Semplice.lnk");
        var withArguments = Path.Combine(_root, "Manda.lnk");
        ShellLinks.Create(plain, Environment.ProcessPath!);
        ShellLinks.Create(withArguments, Environment.ProcessPath!, "--sort", "Manda i file a Inlay");

        Assert.Equal("", ShellLinks.ArgumentsOf(plain));
        Assert.Equal("--sort", ShellLinks.ArgumentsOf(withArguments));
        Assert.Equal(Environment.ProcessPath, ShellLinks.TargetOf(withArguments), StringComparer.OrdinalIgnoreCase);
    });

    private string AddFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }
}
