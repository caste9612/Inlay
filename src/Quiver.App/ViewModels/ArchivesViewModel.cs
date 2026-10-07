using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.App.Interop;
using Quiver.App.Services;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>What the window of the archives asks of the app: which archive is in use, and how to change it.</summary>
public interface IArchiveHost
{
    string? CurrentRoot { get; }

    /// <summary>Opens the archive in Quiver: it becomes the one in use.</summary>
    void Use(string root);

    /// <summary>Lets go of the archive in use, so that its folder can be renamed or moved.</summary>
    void Release();
}

/// <summary>One archive of the list.</summary>
public sealed class ArchiveEntryViewModel(string path, bool isCurrent)
{
    public string Path { get; } = path;

    public string Name => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));

    public bool IsCurrent { get; } = isCurrent;

    /// <summary>Its folder is gone: moved or renamed from Explorer, or on a drive not connected now.</summary>
    public bool IsMissing { get; } = !Directory.Exists(path);

    public string Badge => IsCurrent ? "In uso" : IsMissing ? "Non trovato" : "";
}

/// <summary>
/// The archives of this PC, managed as Obsidian manages its vaults: create one, open any folder as one, switch, rename,
/// move elsewhere, forget. On the same drive a move is a rename of the folder. On another drive Quiver copies the archive,
/// compares the copy with the original byte by byte, and only then switches to it; the old folder goes to the Recycle Bin
/// only if the user says so.
/// </summary>
public sealed partial class ArchivesViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IArchiveHost _host;
    private readonly IReadOnlyList<ReservedFolder> _reserved;
    private readonly Func<string, string, bool> _sameDrive;
    private CancellationTokenSource? _checking;

    [ObservableProperty]
    private ArchiveEntryViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewPath))]
    private string _newName = "Archivio";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewPath))]
    private string _newParent;

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    private PromptViewModel? _prompt;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    /// <summary>From 0 to 1, while a copy is being checked.</summary>
    [ObservableProperty]
    private double _progress;

    /// <param name="reserved">Folders of Windows that cannot be archives.</param>
    /// <param name="sameDrive">Whether a move is on the same drive; tests can say otherwise.</param>
    internal ArchivesViewModel(AppSettings settings, IArchiveHost host, IReadOnlyList<ReservedFolder> reserved,
        Func<string, string, bool>? sameDrive = null)
    {
        _settings = settings;
        _host = host;
        _reserved = reserved;
        _sameDrive = sameDrive ?? ArchiveFolders.IsSameDrive;
        // On the same drive as the Desktop, moving its files into the archive is instant.
        _newParent = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        IsWelcome = host.CurrentRoot is null;
        Refresh();
    }

    /// <summary>An archive was opened: the first time, the window has done its job.</summary>
    public event EventHandler? ArchiveOpened;

    public ObservableCollection<ArchiveEntryViewModel> Archives { get; } = [];

    /// <summary>The first time, before any archive: the window greets and helps to start.</summary>
    public bool IsWelcome { get; }

    public bool HasArchives => Archives.Count > 0;

    public bool IsIdle => !IsBusy;

    /// <summary>Where the new archive will be.</summary>
    public string NewPath => Path.Combine(NewParent, NewName.Trim());

    /// <summary>The window, owner of the progress and questions of the shell.</summary>
    public nint OwnerWindow { get; set; }

    public void Refresh()
    {
        var selected = Selected?.Path;
        Archives.Clear();
        foreach (var path in _settings.Archives)
            Archives.Add(new ArchiveEntryViewModel(path, _host.CurrentRoot is { } current && SamePath(current, path)));
        Selected = Archives.FirstOrDefault(entry => selected is not null && SamePath(entry.Path, selected))
                   ?? Archives.FirstOrDefault(entry => entry.IsCurrent)
                   ?? Archives.FirstOrDefault();
        OnPropertyChanged(nameof(HasArchives));
    }

    // ---- New archives ----

    /// <returns>Null when done; otherwise why not, also in the status.</returns>
    public string? CreateNew()
    {
        var name = NewName.Trim();
        if (ArchiveNames.Validate(name) is { } problem)
            return Fail(problem);
        if (!Directory.Exists(NewParent))
            return Fail($"La cartella «{NewParent}» non c'è: scegline un'altra.");
        var folder = Path.Combine(NewParent, name);
        if (Check(folder) is { } reason)
            return Fail(reason);
        try
        {
            ArchiveFolders.Create(NewParent, name);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail($"Impossibile creare l'archivio: {e.Message}");
        }
        Use(folder);
        return null;
    }

    /// <summary>A folder the user already has becomes an archive: its subfolders become topics, its files stay where they are.</summary>
    public string? OpenFolder(string folder)
    {
        if (Check(folder) is { } reason)
            return Fail(reason);
        try
        {
            ArchiveFolders.Adopt(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail($"Impossibile usare «{folder}» come archivio: {e.Message}");
        }
        Use(folder);
        return null;
    }

    // ---- The archives of the list ----

    public void Open(ArchiveEntryViewModel entry)
    {
        if (entry.IsMissing)
        {
            Status = $"«{entry.Path}» non c'è più: forse è stato spostato o rinominato da Explorer. " +
                     "Riaprilo con «Apri una cartella», oppure toglilo dall'elenco.";
            return;
        }
        Use(entry.Path);
    }

    public void Reveal(ArchiveEntryViewModel entry)
    {
        try
        {
            ShellLauncher.Open(entry.Path);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Status = $"Impossibile aprire «{entry.Path}»: {e.Message}";
        }
    }

    /// <summary>Out of the list: its folder, with everything in it, stays where it is.</summary>
    public void Forget(ArchiveEntryViewModel entry)
    {
        if (entry.IsCurrent)
        {
            Status = "È l'archivio in uso: per toglierlo dall'elenco, prima aprine un altro.";
            return;
        }
        _settings.ForgetArchive(entry.Path);
        Save();
        Refresh();
        Status = $"«{entry.Name}» non è più nell'elenco. La sua cartella è rimasta dov'era: {entry.Path}";
    }

    public void AskRename(ArchiveEntryViewModel entry)
    {
        if (entry.IsMissing)
        {
            Open(entry);
            return;
        }
        Ask(new PromptViewModel($"Rinominare «{entry.Name}»?", "Rinomina", name => Rename(entry, name), initialText: entry.Name,
            message: "Cambia il nome della cartella dell'archivio, anche in Explorer."));
    }

    /// <returns>Why the name cannot be used, or null when done.</returns>
    internal string? Rename(ArchiveEntryViewModel entry, string newName)
    {
        newName = newName.Trim();
        if (newName == entry.Name)
            return null;
        if (ArchiveNames.Validate(newName) is { } problem)
            return problem;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(entry.Path))!;
        var target = Path.Combine(parent, newName);
        if (!newName.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) && Path.Exists(target))
            return $"In «{parent}» c'è già «{newName}».";
        return Relocate(entry, target, $"L'archivio ora si chiama «{newName}».");
    }

    /// <summary>After the user has chosen where: a question that says what is going to happen, then the move.</summary>
    public void AskMove(ArchiveEntryViewModel entry, string destinationParent)
    {
        if (entry.IsMissing)
        {
            Open(entry);
            return;
        }
        var target = Path.Combine(destinationParent, entry.Name);
        if (Path.Exists(target))
        {
            Status = $"In «{destinationParent}» c'è già «{entry.Name}»: scegli un'altra cartella, o rinomina prima l'archivio.";
            return;
        }
        if (Check(target, except: entry.Path) is { } reason)
        {
            Status = reason;
            return;
        }
        var sameDrive = _sameDrive(entry.Path, destinationParent);
        var message = sameDrive
            ? "Sullo stesso disco è un attimo: cambia solo il percorso della cartella, i file restano dove sono sul disco."
            : "È un altro disco: Quiver copia l'archivio, confronta la copia con l'originale file per file e passa alla copia " +
              "solo se sono identiche. La cartella vecchia resta, finché non decidi tu.";
        Ask(new PromptViewModel($"Spostare «{entry.Name}» in «{destinationParent}»?", "Sposta", confirmed =>
        {
            if (sameDrive)
                return Relocate(entry, target, $"L'archivio ora è in «{destinationParent}».");
            // It goes on in the background, with its progress in the window; this question closes.
            _ = MoveToOtherDriveAsync(entry, target);
            return null;
        }, message: message));
    }

    /// <summary>Stops checking a copy: the archive stays where it was.</summary>
    public void CancelCheck() => _checking?.Cancel();

    /// <summary>
    /// The move to another drive: the shell copies, as Explorer would, then every file of the copy is compared with the
    /// original. Only an identical copy takes the place of the archive.
    /// </summary>
    internal async Task MoveToOtherDriveAsync(ArchiveEntryViewModel entry, string target)
    {
        IsBusy = true;
        Progress = 0;
        try
        {
            Status = $"Copia di «{entry.Name}» in corso…";
            if (!ShellFileOperations.Copy([entry.Path], Path.GetDirectoryName(target)!, OwnerWindow) || !Directory.Exists(target))
            {
                Status = "La copia non è stata completata: l'archivio resta dov'era.";
                return;
            }
            Status = "Controllo della copia, file per file…";
            _checking = new CancellationTokenSource();
            var token = _checking.Token;
            var progress = new Progress<CopyCheck.Progress>(step => Progress = step.Total == 0 ? 1 : (double)step.Done / step.Total);
            var differences = await Task.Run(() => CopyCheck.Differences(entry.Path, target, progress, token), token);
            if (differences.Count > 0)
            {
                Status = $"La copia non è uguale all'originale: {differences.Count} file diversi, per esempio «{differences[0]}». " +
                         $"L'archivio resta in «{entry.Path}»; la copia è in «{target}».";
                return;
            }
            var wasCurrent = entry.IsCurrent;
            _settings.ReplaceArchive(entry.Path, target);
            Save();
            if (wasCurrent)
                _host.Use(target);
            Refresh();
            Status = $"L'archivio ora è in «{Path.GetDirectoryName(target)}». La copia è identica all'originale.";
            Ask(new PromptViewModel("Spostare la cartella vecchia nel Cestino?", "Sposta nel Cestino",
                _ => ShellFileOperations.Recycle([entry.Path], OwnerWindow) ? null : "Lo spostamento nel Cestino non è riuscito.",
                message: $"Quiver ora usa «{target}», controllata file per file. La cartella vecchia, «{entry.Path}», " +
                         "se la tieni resta come copia in più.",
                isDanger: true));
        }
        catch (OperationCanceledException)
        {
            Status = $"Controllo interrotto: l'archivio resta in «{entry.Path}»; la copia è in «{target}».";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Il controllo della copia non è riuscito: {e.Message} L'archivio resta in «{entry.Path}».";
        }
        finally
        {
            IsBusy = false;
            _checking = null;
        }
    }

    /// <summary>
    /// Renames or moves an archive on the same drive: one rename of its folder. The archive in use is let go first, then
    /// opened again from the new place.
    /// </summary>
    private string? Relocate(ArchiveEntryViewModel entry, string target, string done)
    {
        var wasCurrent = entry.IsCurrent;
        if (wasCurrent)
            _host.Release();
        try
        {
            Directory.Move(entry.Path, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (wasCurrent)
                _host.Use(entry.Path);
            return $"Impossibile spostare «{entry.Name}»: forse un suo file è aperto in un altro programma. ({e.Message})";
        }
        _settings.ReplaceArchive(entry.Path, target);
        Save();
        if (wasCurrent)
            _host.Use(target);
        Refresh();
        Status = done;
        return null;
    }

    private void Use(string folder)
    {
        _settings.UseArchive(folder);
        Save();
        _host.Use(folder);
        Refresh();
        Status = $"Ora usi «{Path.GetFileName(folder)}».";
        ArchiveOpened?.Invoke(this, EventArgs.Empty);
    }

    private string? Check(string folder, string? except = null) =>
        ArchiveFolders.Check(folder, _reserved, _settings.Archives.Where(known => except is null || !SamePath(known, except)));

    private void Ask(PromptViewModel prompt)
    {
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(Prompt, prompt))
                Prompt = null;
        };
        Prompt = prompt;
    }

    private string Fail(string problem)
    {
        Status = problem;
        return problem;
    }

    private void Save()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save the settings: {e.Message}");
            Status = $"Impossibile salvare le impostazioni: {e.Message}";
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
