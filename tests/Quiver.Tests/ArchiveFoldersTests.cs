using Quiver.Core;

namespace Quiver.Tests;

public sealed class ArchiveFoldersTests : IDisposable
{
    private readonly TempArchive _temp = new();

    public void Dispose() => _temp.Dispose();

    private ReservedFolder[] Reserved => [
        new(Path.Combine(_temp.Root, "Utente"), "utente", InsideAllowed: true),
        new(Path.Combine(_temp.Root, "Utente", "Desktop"), "desktop"),
        new(Path.Combine(_temp.Root, "Windows"), "windows"),
    ];

    [Fact]
    public void AFolderAnywhereElse_CanBeAnArchive() =>
        Assert.Null(ArchiveFolders.Check(Path.Combine(_temp.Root, "Utente", "Archivio"), Reserved, []));

    [Fact]
    public void AWholeDrive_CannotBeAnArchive() =>
        Assert.Contains("disco intero", ArchiveFolders.Check(Path.GetPathRoot(_temp.Root)!, Reserved, []));

    [Fact]
    public void TheReservedFolders_TheirInsides_AndWhatHoldsThem_CannotBe()
    {
        Assert.Equal("utente", ArchiveFolders.Check(Path.Combine(_temp.Root, "Utente"), Reserved, []));
        Assert.Equal("desktop", ArchiveFolders.Check(Path.Combine(_temp.Root, "Utente", "Desktop", "Archivio"), Reserved, []));
        Assert.Equal("windows", ArchiveFolders.Check(Path.Combine(_temp.Root, "Windows", "Temp"), Reserved, []));
        // A folder holding the user's own folder holds the Desktop too.
        Assert.Equal("utente", ArchiveFolders.Check(_temp.Root, Reserved, []));
    }

    [Fact]
    public void AnArchiveCannotBeInsideAnother_NorContainOne()
    {
        var known = Path.Combine(_temp.Root, "Utente", "Archivio");

        Assert.Contains("dentro l'archivio «Archivio»", ArchiveFolders.Check(Path.Combine(known, "Lavoro"), Reserved, [known]));
        Assert.Contains("contiene l'archivio «Archivio»", ArchiveFolders.Check(Path.Combine(_temp.Root, "Utente", "Documenti"), Reserved,
            [Path.Combine(_temp.Root, "Utente", "Documenti", "Archivio")]));
        Assert.Null(ArchiveFolders.Check(known, Reserved, [known]));
    }

    [Fact]
    public void AdoptingAFolder_LeavesItsFiles_AndMarksIt()
    {
        var file = _temp.AddFile("Vecchia/Lavoro/offerta.pdf", "pdf");
        var folder = Path.Combine(_temp.Root, "Vecchia");

        ArchiveFolders.Adopt(folder);

        Assert.True(ArchiveFolders.IsArchive(folder));
        Assert.Equal("pdf", File.ReadAllText(file));
    }

    [Fact]
    public void ANewArchive_NeedsAFreeName()
    {
        var parent = _temp.AddFolder("Utente");

        var archive = ArchiveFolders.Create(parent, "Archivio");

        Assert.True(ArchiveFolders.IsArchive(archive));
        Assert.Throws<IOException>(() => ArchiveFolders.Create(parent, "Archivio"));
    }
}

public sealed class CopyCheckTests : IDisposable
{
    private readonly TempArchive _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AnIdenticalCopy_HasNoDifferences()
    {
        _temp.AddFile("A/Lavoro/offerta.pdf", "contenuto");
        _temp.AddFile("A/.quiver/archive.json", "{}");
        _temp.AddFile("A/vuoto.txt", "");
        CopyTree("A", "B");

        Assert.Empty(CopyCheck.Differences(Path.Combine(_temp.Root, "A"), Path.Combine(_temp.Root, "B")));
    }

    [Fact]
    public void AChangedByte_OrAMissingFile_IsFound()
    {
        _temp.AddFile("A/Lavoro/offerta.pdf", "contenuto");
        _temp.AddFile("A/Lavoro/verbale.txt", "verbale");
        CopyTree("A", "B");
        File.WriteAllText(Path.Combine(_temp.Root, "B", "Lavoro", "offerta.pdf"), "contenutO");
        File.Delete(Path.Combine(_temp.Root, "B", "Lavoro", "verbale.txt"));

        var differences = CopyCheck.Differences(Path.Combine(_temp.Root, "A"), Path.Combine(_temp.Root, "B"));

        Assert.Equal([Path.Combine("Lavoro", "offerta.pdf"), Path.Combine("Lavoro", "verbale.txt")], differences.Order());
    }

    [Fact]
    public void TheProgress_CountsTheBytes()
    {
        _temp.AddFile("A/uno.txt", "12345");
        _temp.AddFile("A/due.txt", "123");
        CopyTree("A", "B");
        var reports = new List<CopyCheck.Progress>();

        CopyCheck.Differences(Path.Combine(_temp.Root, "A"), Path.Combine(_temp.Root, "B"), new SyncProgress(reports.Add));

        Assert.Equal(new CopyCheck.Progress(8, 8), reports[^1]);
    }

    private void CopyTree(string from, string to)
    {
        var source = Path.Combine(_temp.Root, from);
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            var target = Path.Combine(_temp.Root, to, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>Reports right away, unlike <see cref="Progress{T}"/>, which posts to a thread-pool thread.</summary>
    private sealed class SyncProgress(Action<CopyCheck.Progress> report) : IProgress<CopyCheck.Progress>
    {
        public void Report(CopyCheck.Progress value) => report(value);
    }
}
