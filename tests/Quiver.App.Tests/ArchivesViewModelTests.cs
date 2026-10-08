using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Tests;

/// <summary>The archives managed as Obsidian's vaults, on real folders in a temporary place, with settings of their own.</summary>
public sealed class ArchivesViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    private readonly FakeHost _host = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Desktop => Path.Combine(_root, "Desktop");

    [Fact]
    public void TheFirstTime_ANewArchiveIsMade_RememberedAndOpened() => StaThread.Run(() =>
    {
        var settings = Settings();
        var viewModel = Open(settings);
        var opened = false;
        viewModel.ArchiveOpened += (_, _) => opened = true;
        Assert.True(viewModel.IsWelcome);

        viewModel.NewParent = _root;
        viewModel.NewName = "Archivio";
        Assert.Null(viewModel.CreateNew());

        var archive = Path.Combine(_root, "Archivio");
        Assert.True(ArchiveFolders.IsArchive(archive));
        Assert.Equal(archive, _host.Used);
        Assert.True(opened);
        Assert.Equal([archive], AppSettings.Load(settings.SavePath).Archives);
        Assert.Equal(archive, AppSettings.Load(settings.SavePath).ArchiveRoot);
    });

    [Fact]
    public void AFolderYouHave_BecomesAnArchive_AndKeepsItsFiles() => StaThread.Run(() =>
    {
        var folder = AddFile("Documenti/Lavoro/offerta.pdf");
        var viewModel = Open(Settings());

        Assert.Null(viewModel.OpenFolder(Path.Combine(_root, "Documenti")));

        Assert.True(File.Exists(folder));
        Assert.True(ArchiveFolders.IsArchive(Path.Combine(_root, "Documenti")));
        Assert.Equal(Path.Combine(_root, "Documenti"), _host.Used);
    });

    [Fact]
    public void TheDesktop_CannotBeAnArchive() => StaThread.Run(() =>
    {
        Directory.CreateDirectory(Desktop);
        var viewModel = Open(Settings());

        Assert.Equal("desktop", viewModel.OpenFolder(Desktop));

        Assert.False(ArchiveFolders.IsArchive(Desktop));
        Assert.Null(_host.Used);
    });

    [Fact]
    public void RenamingTheArchiveInUse_LetsItGoFirst_ThenOpensItFromItsNewName() => StaThread.Run(() =>
    {
        var archive = MakeArchive("Archivio");
        var settings = Settings(archive);
        var viewModel = Open(settings);

        Assert.Null(viewModel.Rename(viewModel.Archives.Single(), "Scrivania"));

        var renamed = Path.Combine(_root, "Scrivania");
        Assert.True(_host.Released);
        Assert.Equal(renamed, _host.Used);
        Assert.True(File.Exists(Path.Combine(renamed, "Lavoro", "offerta.pdf")));
        Assert.Equal([renamed], AppSettings.Load(settings.SavePath).Archives);
        Assert.Equal("Scrivania", Assert.Single(viewModel.Archives).Name);
    });

    [Fact]
    public void OnTheSameDrive_AMoveIsARenameOfTheFolder() => StaThread.Run(() =>
    {
        var archive = MakeArchive("Archivio");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Altrove")).FullName;
        var viewModel = Open(Settings(archive));

        viewModel.AskMove(viewModel.Archives.Single(), destination);
        Assert.NotNull(viewModel.Prompt);
        Assert.Contains("un attimo", viewModel.Prompt.Message);
        viewModel.Prompt.ConfirmCommand.Execute(null);

        Assert.False(Directory.Exists(archive));
        Assert.True(File.Exists(Path.Combine(destination, "Archivio", "Lavoro", "offerta.pdf")));
        Assert.Equal(Path.Combine(destination, "Archivio"), _host.Used);
    });

    [Fact]
    public void ToAnotherDrive_TheArchiveIsCopied_Checked_AndOnlyThenUsed_TheOldOneStaysUntilYouSaySo() => StaThread.RunAsync(async () =>
    {
        var archive = MakeArchive("Archivio");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Altro disco")).FullName;
        var settings = Settings(archive);
        // As if the destination were on another drive.
        var viewModel = Open(settings, sameDrive: (_, _) => false);
        var entry = viewModel.Archives.Single();
        viewModel.AskMove(entry, destination);
        Assert.Contains("file per file", viewModel.Prompt!.Message);

        await viewModel.MoveToOtherDriveAsync(entry, Path.Combine(destination, "Archivio"));

        var copy = Path.Combine(destination, "Archivio");
        Assert.True(File.Exists(Path.Combine(copy, "Lavoro", "offerta.pdf")));
        Assert.True(ArchiveFolders.IsArchive(copy));
        Assert.Equal(copy, _host.Used);
        Assert.Equal([copy], AppSettings.Load(settings.SavePath).Archives);
        // The old folder is still there: the question about the Recycle Bin waits for an answer.
        Assert.True(Directory.Exists(archive));
        Assert.Equal("Spostare la cartella vecchia nel Cestino?", viewModel.Prompt?.Title);
        Assert.Equal(1, viewModel.Progress);
    });

    [Fact]
    public void ToAnotherDrive_AnArchiveThatWouldNotFit_IsNotCopied() => StaThread.RunAsync(async () =>
    {
        var archive = MakeArchive("Archivio");
        var destination = Directory.CreateDirectory(Path.Combine(_root, "Altro disco")).FullName;
        var settings = Settings(archive);
        // Another drive, with less room than the 9 bytes of the archive.
        var viewModel = Open(settings, sameDrive: (_, _) => false, diskOf: _ => new DiskSpace("Z:", 4, 1000));

        await viewModel.MoveToOtherDriveAsync(viewModel.Archives.Single(), Path.Combine(destination, "Archivio"));

        Assert.StartsWith("Su Z: non c'è abbastanza spazio per l'archivio", viewModel.Status);
        Assert.False(Directory.Exists(Path.Combine(destination, "Archivio")));
        Assert.Equal([archive], settings.Archives);
        Assert.False(viewModel.IsBusy);
    });

    [Fact]
    public void EachArchive_SaysWhatItTakes_AndTheRoomLeftOnItsDisk() => StaThread.RunAsync(async () =>
    {
        var archive = MakeArchive("Archivio");
        const long gb = 1024L * 1024 * 1024;
        var viewModel = Open(Settings(archive), diskOf: _ => new DiskSpace("E:", 120 * gb, 465 * gb));
        var entry = viewModel.Archives.Single();

        for (var wait = 0; entry.Details is null && wait < 100; wait++)
            await Task.Delay(20);

        // Everything in it counts, its hidden metadata included.
        Assert.Matches(@"^\d+ byte · disco E: 120 GB liberi su 465 GB$", entry.Details);
    });

    [Fact]
    public void ForgettingAnArchive_LeavesItsFolder_ButNotTheOneInUse() => StaThread.Run(() =>
    {
        var current = MakeArchive("Archivio");
        var other = MakeArchive("Vecchio");
        var settings = Settings(current);
        settings.Archives.Add(other);
        var viewModel = Open(settings);

        viewModel.Forget(viewModel.Archives.Single(entry => entry.IsCurrent));
        Assert.Equal(2, viewModel.Archives.Count);

        viewModel.Forget(viewModel.Archives.Single(entry => !entry.IsCurrent));
        Assert.Single(viewModel.Archives);
        Assert.True(Directory.Exists(other));
    });

    [Fact]
    public void AnArchiveMovedFromExplorer_IsShownAsMissing() => StaThread.Run(() =>
    {
        var settings = Settings(MakeArchive("Archivio"));
        settings.Archives.Add(Path.Combine(_root, "Sparito"));

        var viewModel = Open(settings);

        Assert.Equal("Non trovato", viewModel.Archives.Single(entry => entry.Name == "Sparito").Badge);
    });

    private ArchivesViewModel Open(AppSettings settings, Func<string, string, bool>? sameDrive = null, Func<string, DiskSpace?>? diskOf = null) =>
        new(settings, _host, [new ReservedFolder(Desktop, "desktop")], sameDrive, diskOf);

    /// <summary>Settings of the test, saved next to its folders; with <paramref name="current"/> as the archive in use.</summary>
    private AppSettings Settings(string? current = null)
    {
        var settings = AppSettings.Load(Path.Combine(_root, "settings.json"));
        if (current is not null)
        {
            settings.UseArchive(current);
            _host.CurrentRoot = current;
        }
        return settings;
    }

    private string MakeArchive(string name)
    {
        AddFile($"{name}/Lavoro/offerta.pdf");
        ArchiveFolders.Adopt(Path.Combine(_root, name));
        return Path.Combine(_root, name);
    }

    private string AddFile(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "contenuto");
        return path;
    }

    private sealed class FakeHost : IArchiveHost
    {
        public string? CurrentRoot { get; set; }

        public string? Used { get; private set; }

        public bool Released { get; private set; }

        public void Use(string root)
        {
            Used = root;
            CurrentRoot = root;
        }

        public void Release() => Released = true;
    }
}
