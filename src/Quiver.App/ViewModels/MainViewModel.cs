using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Quiver.App.Interop;
using Quiver.App.Services;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>State of the panel: topic tree, current board, search, and what can be done with files, as on the desktop.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxResults = 60;

    private readonly bool _live;
    private readonly IReadOnlyList<InboxSource> _inboxSources;
    private readonly RecycleBin _recycleBin;
    private readonly RecentDestinations _recent;
    private readonly UndoHistory _history = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    private string _root;
    private ArchiveReader _reader;
    private ArchiveWatcher? _watcher;
    private SearchIndex? _index;
    private CancellationTokenSource? _indexing;
    private TopicViewModel? _selectedTopic;
    private string? _selectedTopicPath;
    private HashSet<string>? _selectAfterReload;
    private string _summary = "";
    private bool _isPanelVisible;
    private bool _reloadPending;
    private bool _noticeFollows;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(IsBoardVisible))]
    private string _query = "";

    /// <summary>Where the board's topic sits, like "Lavoro › Clienti"; empty for topics in the root.</summary>
    [ObservableProperty]
    private string _boardPath = "";

    [ObservableProperty]
    private string _boardTitle = "";

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _resultsHeader = "";

    [ObservableProperty]
    private string _status = "";

    /// <summary>What the buttons next to the status message can do: take the last operation back, or go and see.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowNotice))]
    private StatusNotice? _notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInboxItems))]
    private int _inboxCount;

    [ObservableProperty]
    private bool _isInboxShown;

    [ObservableProperty]
    private PromptViewModel? _prompt;

    /// <summary>«Sposta in…», open over the board.</summary>
    [ObservableProperty]
    private DestinationPickerViewModel? _picker;

    /// <param name="live">
    /// Indexes the archive in the background and follows its changes. Without it (previews, tests) the board is read
    /// only when asked, and the search needs <see cref="BuildIndexNow"/>.
    /// </param>
    /// <param name="inboxSources">Places outside the archive shown in "Da smistare", like the Desktop.</param>
    /// <param name="recycleBin">Where Ctrl+Z finds what went to the Recycle Bin; by default, the one of Windows.</param>
    /// <param name="recent">Where files were sent last, offered first by «Sposta in…»; by default, those of this PC.</param>
    internal MainViewModel(string root, bool live = true, IReadOnlyList<InboxSource>? inboxSources = null, RecycleBin? recycleBin = null,
        RecentDestinations? recent = null)
    {
        _live = live;
        _inboxSources = inboxSources ?? [];
        _recycleBin = recycleBin ?? new RecycleBin(WindowsFolders.RecycleBinOf);
        _recent = recent ?? RecentDestinations.Load();
        LoadArchive(root);
    }

    /// <summary>Raised after something was opened, so the panel can get out of the way.</summary>
    public event EventHandler? ItemOpened;

    /// <summary>
    /// Raised when a file operation ends. The shell's own windows take the focus during the operation
    /// and do not give it back, so the panel has to take it again.
    /// </summary>
    public event EventHandler? FileOperationCompleted;

    /// <summary>Raised when items were selected after a reload (renamed, moved, pasted): the view focuses them.</summary>
    public event EventHandler? SelectionRestored;

    /// <summary>
    /// Raised after the topics and the board were read again. Their elements are new, so the view puts back the
    /// keyboard focus if it was on one of the old ones.
    /// </summary>
    public event EventHandler? Reloaded;

    public ObservableCollection<TopicViewModel> Topics { get; } = [];

    public ObservableCollection<TopicViewModel> SubTopics { get; } = [];

    public ObservableCollection<TileViewModel> Tiles { get; } = [];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public string Root => _root;

    /// <summary>The name of the archive in use: that of its folder.</summary>
    public string ArchiveName => Path.GetFileName(_root);

    public string InboxPath => _reader.InboxPath;

    /// <summary>The folder the board shows: files pasted or dropped on its background go there.</summary>
    public string? BoardFolder => IsInboxShown ? _reader.InboxPath : _selectedTopic?.Topic.FullPath;

    public TopicViewModel? SelectedTopic => _selectedTopic;

    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    public bool IsBoardVisible => !IsSearching;

    public bool HasInboxItems => InboxCount > 0;

    public bool CanUndo => _history.Last is not null;

    /// <summary>«Mostra» has somewhere to go: the items of the last operation are in a folder.</summary>
    public bool CanShowNotice => Notice?.ShowFolder is not null;

    /// <summary>For the tests: what Ctrl+Z would undo.</summary>
    internal UndoStep? LastUndoStep => _history.Last;

    /// <summary>What Ctrl+Z would undo, for the menu.</summary>
    public string UndoLabel => _history.Last switch
    {
        MoveStep => "Annulla lo spostamento",
        CopyStep => "Annulla la copia",
        LinkStep => "Annulla il collegamento",
        RenameStep => "Annulla la rinomina",
        CreateStep => "Annulla la creazione",
        RecycleStep => "Annulla lo spostamento nel Cestino",
        TileOrderStep => "Annulla lo spostamento del riquadro",
        TopicStep { WasTopic: false } => "Annulla «Promuovi ad argomento»",
        TopicStep => "Annulla «Riporta a riquadro»",
        _ => "Annulla",
    };

    /// <summary>The panel window, owner of the progress and conflict dialogs of file operations.</summary>
    public nint OwnerWindow { get; set; }

    /// <summary>True while a file operation runs: its dialogs take the focus, and the panel must not hide meanwhile.</summary>
    public bool IsBusy { get; private set; }

    [MemberNotNull(nameof(_root), nameof(_reader))]
    public void LoadArchive(string root)
    {
        _watcher?.Dispose();
        _watcher = null;
        _root = Path.GetFullPath(root);
        _reader = new ArchiveReader(_root, new ArchiveMetadata(), _inboxSources);
        _index = null;
        _selectedTopic = null;
        _selectedTopicPath = null;
        // What was done in another archive cannot be undone from this one.
        _history.Clear();
        NotifyHistoryChanged();
        IsInboxShown = false;
        OnPropertyChanged(nameof(ArchiveName));
        UpdateStatus();
        Reload();
        if (!_live)
            return;
        RebuildIndex();
        StartWatching();
    }

    /// <summary>
    /// Lets go of the archive's folder, no longer watched nor indexed, so that it can be renamed or moved.
    /// <see cref="LoadArchive"/> opens it again, from wherever it is.
    /// </summary>
    public void ReleaseArchive()
    {
        _indexing?.Cancel();
        _watcher?.Dispose();
        _watcher = null;
    }

    public void SelectTopicByPath(string relativePath)
    {
        if (FindTopic(relativePath.Replace('\\', '/')) is { } topic)
            SelectTopic(topic);
    }

    public void OnPanelShowing()
    {
        _isPanelVisible = true;
        Query = "";
        Prompt = null;
        Picker = null;
        // The message about the last operation was for the previous time.
        Status = _summary;
        // Read again every time: the watcher reports names only, so a file saved meanwhile would show its old date,
        // and on a drive it cannot watch nothing else would notice the changes. A board is read in a moment.
        Reload();
    }

    public void OnPanelHidden()
    {
        _isPanelVisible = false;
        Prompt = null;
        Picker = null;
        // A name being typed is kept, as on the desktop when you click elsewhere.
        CommitAllEdits();
    }

    public void ReportOpenTime(TimeSpan elapsed) => Log.Write($"Panel shown in {elapsed.TotalMilliseconds:N0} ms");

    /// <summary>Builds the search index synchronously, for preview rendering.</summary>
    public void BuildIndexNow()
    {
        _index = SearchIndex.Build(_root, _reader.InboxSources);
        UpdateStatus();
    }

    /// <summary>Whether a file or folder waits to be sorted: dragged away from there, it is moved even to another drive.</summary>
    public bool IsInInbox(string path) => _reader.IsInInbox(path);

    /// <summary>The selected items of the board, or of the search results while searching.</summary>
    public IReadOnlyList<ItemViewModel> SelectedItems() =>
        (IsSearching ? Results.Cast<ItemViewModel>() : Tiles.SelectMany(tile => tile.Items)).Where(item => item.IsSelected).ToList();

    // ---- Opening ----

    public void Open(IReadOnlyList<ItemViewModel> items)
    {
        var opened = items.Count(item => TryShell(() => ShellLauncher.Open(item.Item.FullPath), item.Name));
        if (opened > 0)
            ItemOpened?.Invoke(this, EventArgs.Empty);
    }

    public void Reveal(ItemViewModel item)
    {
        if (TryShell(() => ShellLauncher.Reveal(item.Item.FullPath), item.Name))
            ItemOpened?.Invoke(this, EventArgs.Empty);
    }

    public void OpenFolder(string path)
    {
        if (TryShell(() => ShellLauncher.Open(path), Path.GetFileName(path)))
            ItemOpened?.Invoke(this, EventArgs.Empty);
    }

    public void OpenWith(ItemViewModel item) => TryShell(() => ShellLauncher.OpenWith(item.Item.FullPath), item.Name);

    public void ShowProperties(string path) => TryShell(() => ShellLauncher.ShowProperties(path), Path.GetFileName(path));

    public void CopyPath(ItemViewModel item)
    {
        try
        {
            Clipboard.SetText(item.Item.FullPath);
            Status = $"Percorso copiato: {item.Item.FullPath}";
        }
        catch (ExternalException)
        {
            Status = "Gli appunti sono occupati da un altro programma. Riprova tra un attimo.";
        }
    }

    // ---- Clipboard ----

    public void CopyToClipboard(IReadOnlyList<ItemViewModel> items, bool cut)
    {
        if (items.Count == 0)
            return;
        var paths = items.Select(item => item.Item.FullPath).ToList();
        if (!FileClipboard.Put(paths, cut))
        {
            Status = "Gli appunti sono occupati da un altro programma. Riprova tra un attimo.";
            return;
        }
        _cutPaths.Clear();
        if (cut)
            _cutPaths.UnionWith(paths);
        foreach (var item in Tiles.SelectMany(tile => tile.Items).Concat(Results))
            item.IsCut = _cutPaths.Contains(item.Item.FullPath);
        Status = cut
            ? $"{Count(items.Count, "elemento pronto", "elementi pronti")} da spostare: incolla con Ctrl+V dove vuoi."
            : $"{Count(items.Count, "elemento copiato", "elementi copiati")}.";
    }

    public void PasteInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Status = "Negli appunti non ci sono file da incollare.";
            return;
        }
        var destination = Path.GetFullPath(folder);
        if (clipboard.Paths.Any(path => TransferRules.IsSameOrInside(destination, Path.GetFullPath(path))))
        {
            Status = "Una cartella non si può incollare dentro se stessa.";
            return;
        }
        if (clipboard.Cut)
        {
            if (clipboard.Paths.All(path => SamePath(Path.GetDirectoryName(path) ?? "", destination)))
            {
                Status = "I file sono già in questa cartella.";
                return;
            }
            _cutPaths.Clear();
            Transfer(clipboard.Paths, destination, TransferKind.Move);
            FileClipboard.Clear();
        }
        else
        {
            Transfer(clipboard.Paths, destination, TransferKind.Copy);
        }
    }

    /// <summary>Like "Nuovo › Collegamento" in Explorer: a website or a path, typed or pasted, in <paramref name="folder"/>.</summary>
    public void AskNewLink(string folder) =>
        ShowPrompt(new PromptViewModel("Nuovo collegamento", "Crea collegamento", text => CreateTypedLink(folder, text),
            initialText: "",
            message: "Scrivi l'indirizzo di un sito, oppure il percorso di un programma, di un file o di una cartella."));

    /// <returns>Why no shortcut was made, shown under the field; null when it is done.</returns>
    private string? CreateTypedLink(string folder, string text)
    {
        var typed = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"').Trim());
        if (typed.Length == 0)
            return "Scrivi un indirizzo o un percorso.";
        if (!EnsureFolder(folder))
            return Status;
        string link;
        try
        {
            if (Path.IsPathFullyQualified(typed) && Path.Exists(typed))
                link = ShellLinks.CreateIn(folder, typed);
            else if (WebShortcut.Parse(typed) is { } address)
                link = WebShortcut.Create(folder, address);
            else
                return "Non trovo questo percorso, e non sembra l'indirizzo di un sito.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            Log.Write($"Could not make a shortcut to {typed}: {e}");
            return $"Impossibile creare il collegamento: {e.Message}";
        }
        Remember(new LinkStep([link]));
        ClearSelection();
        Reload();
        // Like a new folder, it starts with its name ready to type over.
        if (FindItem(link) is { } item)
        {
            item.IsSelected = true;
            BeginRename(item);
        }
        return null;
    }

    /// <summary>"Incolla collegamento", as in Explorer: shortcuts to the files on the clipboard, which stay where they are.</summary>
    public void PasteLinksInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Status = "Negli appunti non ci sono file da collegare.";
            return;
        }
        Transfer(clipboard.Paths, folder, TransferKind.Link);
    }

    // ---- Creating ----

    /// <summary>Like "New folder" on the desktop: a tile named "Nuovo riquadro", with its name ready to type over.</summary>
    /// <param name="topic">Null for the topic on the board.</param>
    public void NewTile(TopicViewModel? topic = null)
    {
        topic ??= _selectedTopic;
        if (topic is null)
        {
            Status = "Per creare un riquadro, scegli prima un argomento.";
            return;
        }
        var folder = topic.Topic.FullPath;
        if (CreateFolder(folder, ArchiveNames.Unique(folder, "Nuovo riquadro")) is not { } path)
            return;
        Remember(new CreateStep(path));
        _selectedTopicPath = topic.Topic.RelativePath;
        IsInboxShown = false;
        Reload();
        if (Tiles.FirstOrDefault(tile => SamePath(tile.FolderPath, path)) is { } tile)
            BeginRename(tile);
    }

    /// <param name="parent">Null for a topic in the root of the archive.</param>
    public void NewTopic(TopicViewModel? parent)
    {
        var folder = parent?.Topic.FullPath ?? _root;
        if (CreateFolder(folder, ArchiveNames.Unique(folder, "Nuovo argomento")) is not { } path)
            return;
        Remember(new CreateStep(path));
        // Folders in the root are topics anyway; deeper ones have to be marked.
        if (parent is not null)
            UpdateMetadata(metadata => metadata.SetTopic(KeyOf(path), true));
        _selectedTopicPath = KeyOf(path);
        IsInboxShown = false;
        Reload();
        if (FindTopic(_selectedTopicPath) is { } topic)
            BeginRename(topic);
    }

    public void NewFolder(TileViewModel tile)
    {
        // "Generale" holds the topic's own files: a folder there is a new tile of the topic.
        if (tile.IsGeneral)
        {
            NewTile();
            return;
        }
        if (CreateFolder(tile.FolderPath, ArchiveNames.Unique(tile.FolderPath, "Nuova cartella")) is not { } path)
            return;
        Remember(new CreateStep(path));
        ClearSelection();
        Reload();
        if (FindItem(path) is { } item)
        {
            item.IsSelected = true;
            BeginRename(item);
        }
    }

    // ---- Renaming ----

    public void BeginRename(RenameableViewModel target)
    {
        // Search results show no field for the name.
        if (target is SearchResultViewModel)
        {
            Status = "Dai risultati della ricerca non si rinomina: premi Esc e apri l'argomento che lo contiene.";
            return;
        }
        CancelAllEdits();
        target.BeginEdit();
    }

    public void CancelRename(RenameableViewModel target) => target.IsEditing = false;

    /// <returns>Why the new name cannot be used, or null when the rename is done (or there was nothing to do).</returns>
    public string? CommitRename(RenameableViewModel target)
    {
        if (!target.IsEditing)
            return null;
        var path = target.RenamePath;
        var oldName = Path.GetFileName(path);
        var newName = target.NewFileName;
        if (newName == oldName)
        {
            target.IsEditing = false;
            return null;
        }
        if (ArchiveNames.Validate(newName) is { } problem)
            return problem;
        var newPath = Path.Combine(Path.GetDirectoryName(path)!, newName);
        var onlyTheCaseChanges = newName.Equals(oldName, StringComparison.OrdinalIgnoreCase);
        if (!onlyTheCaseChanges && Path.Exists(newPath))
            return $"In questa cartella c'è già «{target.EditName.Trim()}». Scegli un altro nome.";
        var isFolder = Directory.Exists(path);
        if (!RunFileOperation(() => ShellFileOperations.Rename(path, newPath, OwnerWindow)))
            return $"Impossibile rinominare «{oldName}»: forse è aperto in un altro programma.";
        target.IsEditing = false;
        Remember(new RenameStep(path, newPath));
        if (isFolder && IsInsideArchive(path))
            FollowFolder(KeyOf(path), KeyOf(newPath));
        // A selected item stays selected under its new name. After a click on another item it is no longer selected.
        if (target is ItemViewModel { IsSelected: true })
            SelectAfterReload([newPath]);
        Reload();
        return null;
    }

    // ---- Deleting ----

    /// <summary>Files go straight to the Recycle Bin, as on the desktop; folders, with everything in them, after a confirmation.</summary>
    public void AskRecycle(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return;
        if (!paths.Any(Directory.Exists))
        {
            Recycle(paths);
            return;
        }
        var title = paths.Count == 1
            ? $"Spostare «{Path.GetFileName(paths[0])}» nel Cestino?"
            : $"Spostare {paths.Count} elementi nel Cestino?";
        var label = paths.Count == 1 ? "Sposta nel Cestino" : $"Sposta {paths.Count} elementi nel Cestino";
        ShowPrompt(new PromptViewModel(title, label,
            _ => Recycle(paths) ? null : "Lo spostamento nel Cestino non è riuscito.",
            message: "Le cartelle ci vanno con tutto il loro contenuto. Si possono recuperare dal Cestino.",
            isDanger: true));
    }

    public bool Recycle(IReadOnlyList<string> paths)
    {
        var folders = paths.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        var forgotten = SnapshotOf(paths);
        // The Recycle Bin dates what it receives: right after the deletion, it tells where each item went.
        var since = DateTime.UtcNow.AddSeconds(-2);
        var done = RunFileOperation(() => ShellFileOperations.Recycle(paths, OwnerWindow));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        var gone = paths.Where(path => !Path.Exists(path)).ToList();
        if (gone.Count > 0)
        {
            var inBin = _recycleBin.FindDeleted(gone, since);
            Remember(new RecycleStep(inBin, gone.Count - inBin.Count, forgotten.Restrict(ArchiveKeys(gone))));
            var where = inBin.Count == gone.Count ? "nel Cestino" : "stat{0} eliminat{0} definitivamente, senza passare dal Cestino";
            Report(gone.Count == 1
                    ? $"«{DisplayNameOf(gone[0])}» è {string.Format(where, "o")}."
                    : $"{gone.Count} elementi sono {string.Format(where, "i")}.",
                new StatusNotice(null, gone));
        }
        Reload();
        return done;
    }

    // ---- «Sposta in…» ----

    /// <summary>
    /// Asks where the files go, among the tiles and topics of the archive, the recent ones first. What has a place of
    /// its own, like a program or a folder on the server, enters as a shortcut; everything else is moved, even from
    /// another drive: sending it to the archive is the point.
    /// </summary>
    public void AskDestination(IReadOnlyList<string> paths)
    {
        var sources = paths.Select(Path.GetFullPath).ToList();
        if (sources.Count == 0)
            return;
        var link = sources.All(path => TransferRules.HasItsOwnPlace(path, _root) && !IsInInbox(path));
        var destinations = _reader.ReadDestinations()
            .Where(destination => KindFor(sources, destination.FullPath, link) != TransferKind.None)
            .ToList();
        var name = DisplayNameOf(sources[0]);
        var title = (sources.Count, link) switch
        {
            (1, false) => $"Sposta «{name}» in…",
            (_, false) => $"Sposta {sources.Count} elementi in…",
            (1, true) => $"Crea un collegamento a «{name}» in…",
            _ => $"Crea {sources.Count} collegamenti in…",
        };
        ShowPicker(new DestinationPickerViewModel(title, link ? "Collega" : "Sposta", destinations, _recent.For(_root),
            destination => SendTo(sources, destination, link)));
    }

    private static TransferKind KindFor(IReadOnlyList<string> sources, string destination, bool link) =>
        TransferRules.Choose(sources, destination, forceCopy: false, forceMove: !link, forceLink: link);

    /// <returns>Why nothing was done, for the picker; null when the files went.</returns>
    private string? SendTo(IReadOnlyList<string> sources, Destination destination, bool link)
    {
        var kind = KindFor(sources, destination.FullPath, link);
        if (kind == TransferKind.None)
            return "I file sono già lì.";
        _recent.Remember(_root, destination.RelativeKey);
        Transfer(sources, destination.FullPath, kind);
        return null;
    }

    /// <summary>«Mostra», next to the message of the last operation: the board goes where the items went, with them selected.</summary>
    public void ShowNotice()
    {
        if (Notice is not { ShowFolder: { } folder } notice)
            return;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (SamePath(full, _reader.InboxPath) || !IsInsideArchive(full))
        {
            IsInboxShown = true;
        }
        else
        {
            IsInboxShown = false;
            _selectedTopicPath = KeyOf(full);
        }
        SelectAfterReload(notice.Paths);
        Reload();
    }

    // ---- Moving ----

    /// <summary>Moves or copies files dropped or pasted onto a folder of the archive, or makes shortcuts to them there.</summary>
    public void Transfer(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind)
    {
        if (kind == TransferKind.None || paths.Count == 0 || !EnsureFolder(destinationFolder))
            return;
        if (kind == TransferKind.Link)
        {
            CreateLinks(paths, destinationFolder);
            return;
        }

        List<string> movedFolders = kind == TransferKind.Move
            ? paths.Where(Directory.Exists).Where(IsInsideArchive).ToList()
            : [];
        // Where each item lands if its name is free there. When it is taken, the user chooses in the shell's dialog
        // what happens, so where the item ends up is not known, and Ctrl+Z leaves it alone.
        var landings = paths
            .Select(path => new MovedItem(path, Path.Combine(destinationFolder, Path.GetFileName(path))))
            .Where(item => !Path.Exists(item.To))
            .ToList();
        // Folders leaving the archive lose their metadata: Ctrl+Z gives it back.
        var leavesArchive = !TransferRules.IsSameOrInside(Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationFolder)), _root);
        var forgotten = kind == TransferKind.Move && leavesArchive ? SnapshotOf(paths) : MetadataSnapshot.Empty;
        var namesBefore = kind == TransferKind.Copy ? EntryNames(destinationFolder) : null;
        // A copy next to the original gets a "- Copia" name, as in Explorer.
        var nextToOriginal = paths.Any(path => SamePath(Path.GetDirectoryName(path) ?? "", destinationFolder));
        var done = RunFileOperation(() => kind == TransferKind.Copy
            ? ShellFileOperations.Copy(paths, destinationFolder, OwnerWindow, renameOnCollision: nextToOriginal)
            : ShellFileOperations.Move(paths, destinationFolder, OwnerWindow));
        // Folders keep their metadata (topic, place on the board…) where they went.
        foreach (var folder in movedFolders.Where(folder => !Directory.Exists(folder)))
        {
            var newPath = Path.Combine(destinationFolder, Path.GetFileName(folder));
            if (IsInsideArchive(newPath))
                FollowFolder(KeyOf(folder), KeyOf(newPath));
            else
                ForgetFolder(KeyOf(folder));
        }

        var arrived = paths.Select(path => Path.Combine(destinationFolder, Path.GetFileName(path))).ToList();
        if (kind == TransferKind.Move)
        {
            var moved = landings.Where(item => !Path.Exists(item.From) && Path.Exists(item.To)).ToList();
            if (moved.Count > 0)
                Remember(new MoveStep(moved, forgotten));
        }
        // The copies are the new names, each starting like an original: "a.txt", or "a - Copia.txt" next to it.
        // Without the names from before, the copies cannot be told from what was already there: no undo then.
        else if (namesBefore is not null && EntryNames(destinationFolder) is { } namesAfter)
        {
            arrived = namesAfter
                .Where(name => !namesBefore.Contains(name) &&
                               paths.Any(path => name.StartsWith(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
                .Select(name => Path.Combine(destinationFolder, name))
                .ToList();
            if (arrived.Count > 0)
                Remember(new CopyStep(arrived));
        }

        var present = arrived.Where(Path.Exists).ToList();
        if (!done)
            Status = kind == TransferKind.Copy ? "La copia non è stata completata." : "Lo spostamento non è stato completato.";
        else if (present.Count > 0)
            Report(Done(present, kind == TransferKind.Copy ? ("copiato", "copiati") : ("spostato", "spostati"), destinationFolder),
                new StatusNotice(destinationFolder, present));
        // What arrived is selected, as in Explorer.
        SelectAfterReload(arrived);
        Reload();
    }

    /// <summary>
    /// Saves what another program drops without it being a file yet, like a mail attachment or a link from the browser.
    /// Must be called during the drop: afterwards the program may no longer give the content.
    /// </summary>
    public void SaveDropped(System.Windows.IDataObject data, string destinationFolder)
    {
        if (!EnsureFolder(destinationFolder))
            return;
        var created = new List<string>();
        try
        {
            VirtualFiles.SaveTo(data, destinationFolder, created);
        }
        catch (Exception e)
        {
            // Whatever the other program does wrong (COM errors come in many forms), the files saved so far keep their undo.
            Log.Write($"Could not save what was dropped: {e}");
            Status = $"Non sono riuscito a salvare quello che hai trascinato: {e.Message}";
        }
        if (created.Count > 0)
        {
            Remember(new CopyStep(created));
            SelectAfterReload(created);
            Report(Done(created, ("salvato", "salvati"), destinationFolder), new StatusNotice(destinationFolder, created));
        }
        Reload();
    }

    /// <summary>The folder where something is about to land; the inbox folder only exists once something lands in it.</summary>
    private bool EnsureFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Impossibile usare la cartella {folder}: {e.Message}";
            return false;
        }
    }

    private void CreateLinks(IReadOnlyList<string> targets, string destinationFolder)
    {
        var links = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                links.Add(ShellLinks.CreateIn(destinationFolder, target));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
            {
                Log.Write($"Could not make a shortcut to {target}: {e}");
                Status = $"Impossibile creare il collegamento a «{Path.GetFileName(target)}»: {e.Message}";
            }
        }
        if (links.Count > 0)
        {
            Remember(new LinkStep(links));
            SelectAfterReload(links);
            Report(links.Count == 1
                    ? $"Collegamento «{DisplayNameOf(links[0])}» creato in «{Path.GetFileName(destinationFolder)}»."
                    : $"{links.Count} collegamenti creati in «{Path.GetFileName(destinationFolder)}».",
                new StatusNotice(destinationFolder, links));
        }
        Reload();
    }

    /// <summary>"«Offerta.pdf» spostato in «Documenti»", or "3 elementi spostati in «Documenti»".</summary>
    private static string Done(IReadOnlyList<string> paths, (string One, string Many) verb, string folder) =>
        paths.Count == 1
            ? $"«{DisplayNameOf(paths[0])}» {verb.One} in «{Path.GetFileName(folder)}»."
            : $"{paths.Count} elementi {verb.Many} in «{Path.GetFileName(folder)}».";

    /// <summary>Puts a tile in another place on its board, before <paramref name="before"/> (null: at the end).</summary>
    public void MoveTile(TileViewModel tile, TileViewModel? before)
    {
        if (!tile.IsOwnFolder || ReferenceEquals(tile, before))
            return;
        var order = Tiles.Where(t => t.IsOwnFolder).Select(t => t.RelativePath!).ToList();
        var metadata = LoadMetadata();
        var previousOrder = order.ToDictionary(key => key, metadata.OrderOf, StringComparer.OrdinalIgnoreCase);
        order.Remove(tile.RelativePath!);
        var index = before?.RelativePath is { } target ? order.IndexOf(target) : -1;
        order.Insert(index < 0 ? order.Count : index, tile.RelativePath!);
        ChangeMetadata(m => m.SetTileOrder(order), new TileOrderStep(previousOrder));
    }

    // ---- Topics ----

    public void PromoteTile(TileViewModel tile)
    {
        if (tile.RelativePath is not { } path)
            return;
        ChangeMetadata(metadata => metadata.SetTopic(path, true), new TopicStep(path, tile.Title, WasTopic: false));
    }

    public void DemoteTopic(TopicViewModel topic)
    {
        if (!topic.CanDemote)
            return;
        // A selection inside the demoted topic falls back to its parent.
        if (_selectedTopicPath is { } selected && ArchiveLayout.IsKeyAtOrBelow(selected, topic.Topic.RelativePath))
            _selectedTopicPath = topic.Parent.Topic.RelativePath;
        ChangeMetadata(metadata => metadata.SetTopic(topic.Topic.RelativePath, false),
            new TopicStep(topic.Topic.RelativePath, topic.Name, WasTopic: true));
    }

    // ---- Undo ----

    /// <summary>Takes back the last thing done in Quiver, as Ctrl+Z does in Explorer.</summary>
    public void Undo()
    {
        CommitAllEdits();
        if (_history.Pop() is not { } step)
        {
            Status = "Non c'è niente da annullare.";
            return;
        }
        NotifyHistoryChanged();
        Status = step switch
        {
            MoveStep move => UndoMove(move),
            CopyStep copy => RecycleCreated(copy, copy.Copies, "Copia annullata", "le copie", "copie"),
            LinkStep link => RecycleCreated(link, link.Links, "Collegamento annullato", "i collegamenti", "collegamenti"),
            RenameStep rename => UndoRename(rename),
            CreateStep create => UndoCreate(create),
            RecycleStep recycle => UndoRecycle(recycle),
            TileOrderStep order => UndoTileOrder(order),
            TopicStep topic => UndoTopic(topic),
            _ => throw new InvalidOperationException($"Unknown undo step {step.GetType().Name}"),
        };
        Reload();
    }

    /// <summary>The undo did not happen, most likely because a file is open in another program: Ctrl+Z can try again.</summary>
    private void KeepForRetry(UndoStep step) => Remember(step);

    private string UndoMove(MoveStep step)
    {
        // Only what is still where it went goes back, and only to a place that is still free.
        var items = step.Items.Where(item => Path.Exists(item.To) && !Path.Exists(item.From)).ToList();
        if (items.Count == 0)
            return "Lo spostamento non si può annullare: nel frattempo gli elementi sono stati spostati o rinominati.";
        RunFileOperation(() => ShellFileOperations.MoveEach(
            items.Select(item => item.To).ToList(), items.Select(item => item.From).ToList(), OwnerWindow));
        var back = items.Where(item => Path.Exists(item.From) && !Path.Exists(item.To)).ToList();
        if (back.Count == 0)
        {
            KeepForRetry(step);
            return "Lo spostamento non è stato annullato: forse un elemento è aperto in un altro programma. Riprova con Ctrl+Z.";
        }
        FollowBack(back, step.Forgotten);
        SelectAfterReload(back.Select(item => item.From));
        var done = back.Count == 1
            ? $"Spostamento annullato: «{DisplayNameOf(back[0].From)}» è di nuovo in «{FolderName(back[0].From)}»."
            : $"Spostamento annullato: {back.Count} elementi sono tornati dov'erano.";
        return done + LeftAlone(step.Items.Count - back.Count);
    }

    /// <summary>
    /// The metadata of folders put back where they were follows them, as it did when they were moved. A folder that had
    /// left the archive gets back what it had there; one that had come in leaves behind what it got.
    /// </summary>
    private void FollowBack(IReadOnlyList<MovedItem> items, MetadataSnapshot forgotten)
    {
        UpdateMetadata(metadata =>
        {
            var changed = false;
            foreach (var item in items)
            {
                var wasInside = IsInsideArchive(item.From);
                var wentInside = IsInsideArchive(item.To);
                if (wasInside && wentInside)
                    changed |= metadata.MoveFolder(KeyOf(item.To), KeyOf(item.From));
                else if (wentInside)
                    changed |= metadata.RemoveFolder(KeyOf(item.To));
                else if (wasInside)
                    changed |= metadata.Restore(forgotten.Restrict([KeyOf(item.From)]));
            }
            return changed;
        });
        foreach (var item in items.Where(item => IsInsideArchive(item.To)))
        {
            if (IsInsideArchive(item.From))
                FollowSelection(KeyOf(item.To), KeyOf(item.From));
            else
                ForgetSelection(KeyOf(item.To));
        }
    }

    /// <summary>Undoes copies and shortcuts made by Quiver: they go to the Recycle Bin, never deleted for good.</summary>
    /// <param name="them">With the article, as in "le copie".</param>
    /// <param name="many">As in "3 copie sono nel Cestino".</param>
    private string RecycleCreated(UndoStep step, IReadOnlyList<string> created, string undone, string them, string many)
    {
        var existing = created.Where(Path.Exists).ToList();
        if (existing.Count == 0)
            return $"Non c'è niente da annullare: {them} non ci sono più.";
        // Without a Recycle Bin, deleting is for good, changes made since included: not something an undo may do.
        if (!existing.All(RecycleBin.IsAvailableFor))
            return $"Annullamento non fatto: su questo disco non c'è il Cestino, quindi non si potrebbe più recuperare niente. Se {them} non servono, usa Canc.";
        var folders = existing.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        RunFileOperation(() => ShellFileOperations.Recycle(existing, OwnerWindow));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        var removed = existing.Where(path => !Path.Exists(path)).ToList();
        if (removed.Count == 0)
        {
            KeepForRetry(step);
            return $"Annullamento non riuscito: {them} sono ancora lì. Riprova con Ctrl+Z.";
        }
        var done = removed.Count == 1
            ? $"{undone}: «{DisplayNameOf(removed[0])}» è nel Cestino."
            : $"{undone}: {removed.Count} {many} sono nel Cestino.";
        return done + LeftAlone(created.Count - removed.Count);
    }

    private string UndoRename(RenameStep step)
    {
        var newName = DisplayNameOf(step.To);
        if (!Path.Exists(step.To))
            return $"La rinomina non si può annullare: «{newName}» non c'è più.";
        var onlyTheCaseChanged = string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase);
        if (!onlyTheCaseChanged && Path.Exists(step.From))
            return $"La rinomina non si può annullare: nella cartella c'è già «{DisplayNameOf(step.From)}».";
        if (!RunFileOperation(() => ShellFileOperations.Rename(step.To, step.From, OwnerWindow)))
        {
            KeepForRetry(step);
            return $"Impossibile rinominare «{newName}»: forse è aperto in un altro programma. Riprova con Ctrl+Z.";
        }
        // The metadata follows the folder back, and so does the selection of a topic.
        if (IsInsideArchive(step.From))
            FollowFolder(KeyOf(step.To), KeyOf(step.From));
        SelectAfterReload([step.From]);
        return $"Rinomina annullata: si chiama di nuovo «{DisplayNameOf(step.From)}».";
    }

    private string UndoCreate(CreateStep step)
    {
        var name = Path.GetFileName(step.Folder);
        try
        {
            if (!Directory.Exists(step.Folder))
                return $"«{name}» non c'è più.";
            // Only an empty folder goes away for good: anything put in it since stays.
            if (Directory.EnumerateFileSystemEntries(step.Folder).Any())
                return $"«{name}» non è più vuota, quindi resta. Se non ti serve, spostala nel Cestino con Canc.";
            Directory.Delete(step.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            KeepForRetry(step);
            return $"Impossibile togliere «{name}»: {e.Message}";
        }
        // What it got meanwhile, like the mark of a topic, goes with it.
        if (IsInsideArchive(step.Folder))
            ForgetFolder(KeyOf(step.Folder));
        return $"Annullata la creazione di «{name}».";
    }

    private string UndoRecycle(RecycleStep step)
    {
        if (step.Items.Count == 0)
            return "Non si può ripristinare: Windows l'ha eliminato definitivamente, senza passare dal Cestino.";
        var restored = new List<RecycledItem>();
        var failed = new List<RecycledItem>();
        // A folder before what was inside it, in case both were deleted together.
        foreach (var item in step.Items.OrderBy(item => item.OriginalPath.Length))
        {
            try
            {
                if (RecycleBin.TryRestore(item))
                {
                    restored.Add(item);
                    ShellNotify.Moved(item.ContentPath, item.OriginalPath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Write($"Could not restore {item.OriginalPath} from the Recycle Bin: {e}");
                failed.Add(item);
            }
        }
        if (failed.Count > 0)
            KeepForRetry(step with { Items = failed, NotInBin = 0 });
        if (restored.Count == 0)
        {
            return failed.Count > 0
                ? "Il ripristino non è riuscito: forse una cartella è aperta in un altro programma. Riprova con Ctrl+Z."
                : "Nel Cestino non c'è più, oppure al suo posto c'è già un altro elemento.";
        }
        ShellNotify.RecycleBinChanged();
        RestoreMetadata(step.Forgotten.Restrict(ArchiveKeys(restored.Select(item => item.OriginalPath))));
        SelectAfterReload(restored.Select(item => item.OriginalPath));
        var done = restored.Count == 1
            ? $"«{DisplayNameOf(restored[0].OriginalPath)}» è tornato dal Cestino in «{FolderName(restored[0].OriginalPath)}»."
            : $"{restored.Count} elementi sono tornati dal Cestino.";
        var lost = step.NotInBin switch
        {
            0 => "",
            1 => " Un elemento era stato eliminato definitivamente.",
            var count => $" {count} elementi erano stati eliminati definitivamente.",
        };
        return done + LeftAlone(step.Items.Count - restored.Count - failed.Count) + lost;
    }

    private string UndoTileOrder(TileOrderStep step)
    {
        // A tile renamed or removed since has no folder to take the place.
        var existing = step.PreviousOrder.Where(entry => Directory.Exists(Path.Combine(_root, entry.Key))).ToList();
        var done = UpdateMetadata(metadata =>
        {
            var changed = false;
            foreach (var (key, order) in existing)
                changed |= metadata.SetOrder(key, order);
            return changed;
        });
        return done ? "I riquadri sono tornati nell'ordine di prima." : Status;
    }

    private string UndoTopic(TopicStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return $"«{step.Name}» non c'è più.";
        if (!UpdateMetadata(metadata => metadata.SetTopic(step.Key, step.WasTopic)))
            return Status;
        return step.WasTopic ? $"«{step.Name}» è di nuovo un argomento." : $"«{step.Name}» è di nuovo un riquadro.";
    }

    /// <summary>For the items an undo could not put back: something else took their place, or they are gone.</summary>
    private static string LeftAlone(int count) => count switch
    {
        0 => "",
        1 => " Un elemento è cambiato nel frattempo ed è rimasto com'era.",
        _ => $" {count} elementi sono cambiati nel frattempo e sono rimasti com'erano.",
    };

    private static string FolderName(string path) => Path.GetFileName(Path.GetDirectoryName(path)) ?? "";

    /// <summary>The name as the board shows it: shortcuts without .lnk and .url.</summary>
    private static string DisplayNameOf(string path) =>
        new ArchiveItem(path, Path.GetFileName(path), Directory.Exists(path)).DisplayName;

    public void Dispose()
    {
        _indexing?.Cancel();
        _watcher?.Dispose();
    }

    [RelayCommand]
    private void SelectTopic(TopicViewModel? topic)
    {
        if (topic is null)
            return;
        IsInboxShown = false;
        if (_selectedTopic is not null && !ReferenceEquals(_selectedTopic, topic))
            _selectedTopic.IsSelected = false;
        _selectedTopic = topic;
        _selectedTopicPath = topic.Topic.RelativePath;
        foreach (var ancestor in topic.Ancestors())
            ancestor.IsExpanded = true;
        topic.IsSelected = true;

        var board = _reader.ReadBoard(topic.Topic);
        BoardTitle = topic.Name;
        BoardPath = string.Join(ArchiveReader.PathSeparator, topic.Ancestors().Reverse().Select(ancestor => ancestor.Name));
        Reconciler.Reconcile(SubTopics, topic.Children, child => child.Topic.RelativePath, child => child.Topic.RelativePath,
            child => child, (_, _) => { });
        Reconciler.Reconcile(Tiles, board.Tiles, tile => TileViewModel.KeyOf(tile.FolderPath, tile.IsGeneral), tile => tile.Key,
            tile => new TileViewModel(tile, _root, _cutPaths), (viewModel, tile) => viewModel.Update(tile));
        EmptyMessage = board.Tiles.Count == 0 && topic.Children.Count == 0
            ? "Questo argomento è vuoto. Trascina qui dei file, oppure crea un riquadro con Ctrl+Maiusc+N."
            : "";
        OnPropertyChanged(nameof(BoardFolder));
        OnPropertyChanged(nameof(SelectedTopic));
    }

    [RelayCommand]
    private void ShowInbox()
    {
        IsInboxShown = true;
        if (_selectedTopic is not null)
            _selectedTopic.IsSelected = false;
        _selectedTopic = null;

        var tiles = _reader.ReadInbox();
        InboxCount = tiles.Sum(tile => tile.Items.Count);
        BoardTitle = "Da smistare";
        BoardPath = "";
        SubTopics.Clear();
        Reconciler.Reconcile(Tiles, tiles, tile => TileViewModel.KeyOf(tile.FolderPath, isGeneral: false), tile => tile.Key,
            tile => new TileViewModel(tile.Title, tile.FolderPath, tile.Items, _cutPaths), (viewModel, tile) => viewModel.Update(tile));
        EmptyMessage = InboxCount == 0 ? InboxEmptyMessage() : "";
        OnPropertyChanged(nameof(BoardFolder));
        OnPropertyChanged(nameof(SelectedTopic));
    }

    private string InboxEmptyMessage()
    {
        var places = _reader.InboxSources
            .Select(source => source.Title switch
            {
                "Desktop" => "sul Desktop",
                "Download" => "in Download",
                var title => $"in «{title}»",
            })
            .Append("nella radice dell'archivio")
            .ToList();
        return $"Niente da smistare. Qui compare quello che arriva {string.Join(", ", places)} " +
               $"o nella cartella «{ArchiveLayout.InboxFolderName}».";
    }

    partial void OnQueryChanged(string value) => RunSearch();

    /// <summary>A new message means the buttons of the last operation no longer apply, unless they come with it.</summary>
    partial void OnStatusChanged(string value)
    {
        if (!_noticeFollows)
            Notice = null;
    }

    /// <summary>Reads topics and the current board again, keeping the selection if that topic still exists.</summary>
    private void Reload()
    {
        _reloadPending = false;
        _reader = new ArchiveReader(_root, LoadMetadata(), _inboxSources);
        // The topics, tiles and items still there keep their view models, with their selection, the name being typed
        // and the branches left open; the view hears only about what moved, arrived or went away.
        Reconciler.Reconcile(Topics, _reader.ReadTopics(), topic => topic.RelativePath, topic => topic.Topic.RelativePath,
            topic => new TopicViewModel(topic, parent: null), (viewModel, topic) => viewModel.Update(topic));
        InboxCount = _reader.ReadInbox().Sum(tile => tile.Items.Count);

        if (IsInboxShown)
            ShowInbox();
        else if ((FindTopicOrParent(_selectedTopicPath) ?? Topics.FirstOrDefault()) is { } topic)
            SelectTopic(topic);
        else
            ShowEmptyArchive();

        // What was just renamed, moved or pasted takes the place of the selection.
        ApplyPendingSelection();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    private void ShowEmptyArchive()
    {
        BoardTitle = "Archivio vuoto";
        BoardPath = "";
        SubTopics.Clear();
        Tiles.Clear();
        EmptyMessage = "Crea il primo argomento con il pulsante + accanto ad «Argomenti».";
        OnPropertyChanged(nameof(BoardFolder));
        OnPropertyChanged(nameof(SelectedTopic));
    }

    private void SelectAfterReload(IEnumerable<string> paths) => _selectAfterReload = ToPathSet(paths);

    private void ApplyPendingSelection()
    {
        if (_selectAfterReload is not { } paths)
            return;
        _selectAfterReload = null;
        var found = Tiles.SelectMany(tile => tile.Items).Where(item => paths.Contains(item.Item.FullPath)).ToList();
        if (found.Count == 0)
            return;
        ClearSelection();
        foreach (var item in found)
            item.IsSelected = true;
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<ItemViewModel> SelectedBoardItems() => Tiles.SelectMany(tile => tile.Items).Where(item => item.IsSelected);

    private void ClearSelection()
    {
        foreach (var item in SelectedBoardItems().ToList())
            item.IsSelected = false;
    }

    /// <summary>Everything whose name can be edited in place: topics, tiles and items.</summary>
    private IEnumerable<RenameableViewModel> EditTargets() =>
        Topics.SelectMany(topic => topic.SelfAndDescendants())
            .Concat<RenameableViewModel>(Tiles)
            .Concat(Tiles.SelectMany(tile => tile.Items));

    private void CancelAllEdits()
    {
        foreach (var target in EditTargets())
            target.IsEditing = false;
    }

    private void CommitAllEdits()
    {
        foreach (var target in EditTargets().Where(target => target.IsEditing).ToList())
        {
            if (CommitRename(target) is { } problem)
            {
                target.IsEditing = false;
                Status = problem;
            }
        }
    }

    private TopicViewModel? FindTopic(string? relativePath) =>
        relativePath is null
            ? null
            : Topics.SelectMany(topic => topic.SelfAndDescendants())
                .FirstOrDefault(topic => string.Equals(topic.Topic.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>The topic, or else the nearest of its parents that is still a topic.</summary>
    private TopicViewModel? FindTopicOrParent(string? relativePath)
    {
        for (var key = relativePath; key is not null;)
        {
            if (FindTopic(key) is { } topic)
                return topic;
            var slash = key.LastIndexOf('/');
            key = slash > 0 ? key[..slash] : null;
        }
        return null;
    }

    private ItemViewModel? FindItem(string path) =>
        Tiles.SelectMany(tile => tile.Items).FirstOrDefault(item => SamePath(item.Item.FullPath, path));

    private string? CreateFolder(string parentFolder, string name)
    {
        var path = Path.Combine(parentFolder, name);
        try
        {
            Directory.CreateDirectory(path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Impossibile creare la cartella «{name}»: {e.Message}";
            return null;
        }
    }

    /// <summary>A folder was renamed or moved: its metadata and the selection follow it.</summary>
    private void FollowFolder(string oldKey, string newKey)
    {
        UpdateMetadata(metadata => metadata.MoveFolder(oldKey, newKey));
        FollowSelection(oldKey, newKey);
    }

    private void FollowSelection(string oldKey, string newKey)
    {
        if (_selectedTopicPath is { } selected && ArchiveLayout.IsKeyAtOrBelow(selected, oldKey))
            _selectedTopicPath = newKey + selected[oldKey.Length..];
    }

    /// <summary>A folder was deleted: its metadata goes, and a selection inside it moves to its parent.</summary>
    private void ForgetFolder(string key)
    {
        UpdateMetadata(metadata => metadata.RemoveFolder(key));
        ForgetSelection(key);
    }

    private void ForgetSelection(string key)
    {
        if (_selectedTopicPath is { } selected && ArchiveLayout.IsKeyAtOrBelow(selected, key))
        {
            var slash = key.LastIndexOf('/');
            _selectedTopicPath = slash > 0 ? key[..slash] : null;
        }
    }

    // ---- Undo history ----

    private void Remember(UndoStep step)
    {
        _history.Record(step);
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
    }

    /// <summary>Changes the metadata only, keeping a step to undo it if anything changed.</summary>
    private void ChangeMetadata(Func<ArchiveMetadata, bool> change, UndoStep step)
    {
        var changed = false;
        if (UpdateMetadata(metadata => changed = change(metadata)) && changed)
            Remember(step);
        Reload();
    }

    /// <summary>The metadata of these files and folders, those in the archive, as it is now: what an undo puts back.</summary>
    private MetadataSnapshot SnapshotOf(IEnumerable<string> paths)
    {
        var keys = ArchiveKeys(paths).ToList();
        if (keys.Count == 0)
            return MetadataSnapshot.Empty;
        try
        {
            return MetadataStore.Load(_root).Snapshot(keys);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read the archive metadata: {e.Message}");
            return MetadataSnapshot.Empty;
        }
    }

    private void RestoreMetadata(MetadataSnapshot snapshot)
    {
        if (snapshot.Roots.Count > 0)
            UpdateMetadata(metadata => metadata.Restore(snapshot));
    }

    private IEnumerable<string> ArchiveKeys(IEnumerable<string> paths) => paths.Where(IsInsideArchive).Select(KeyOf);

    /// <summary>The names in a folder, to tell afterwards which ones a copy added.</summary>
    private static HashSet<string> EntryNames(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).Select(path => Path.GetFileName(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> ToPathSet(IEnumerable<string> paths) =>
        paths.Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    private string KeyOf(string path) => ArchiveLayout.ToRelativeKey(_root, path);

    private bool IsInsideArchive(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return !string.Equals(fullPath, _root, StringComparison.OrdinalIgnoreCase) &&
               TransferRules.IsSameOrInside(fullPath, _root);
    }

    private ArchiveMetadata LoadMetadata()
    {
        try
        {
            return MetadataStore.Load(_root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not read the archive metadata: {e.Message}");
            return new ArchiveMetadata();
        }
    }

    /// <summary>Applies a change to the metadata file, starting from what is on disk.</summary>
    /// <param name="change">Returns whether it changed anything, so unchanged metadata is not rewritten.</param>
    private bool UpdateMetadata(Func<ArchiveMetadata, bool> change)
    {
        try
        {
            var metadata = MetadataStore.Load(_root);
            if (change(metadata))
                MetadataStore.Save(_root, metadata);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save the archive metadata: {e}");
            Status = $"Impossibile salvare le impostazioni dell'archivio: {e.Message}";
            return false;
        }
    }

    private bool RunFileOperation(Func<bool> operation)
    {
        IsBusy = true;
        try
        {
            return operation();
        }
        finally
        {
            IsBusy = false;
            FileOperationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowPrompt(PromptViewModel prompt)
    {
        prompt.Closed += (_, _) =>
        {
            if (ReferenceEquals(Prompt, prompt))
                Prompt = null;
            // Changes noticed while the question was open.
            if (_reloadPending && _isPanelVisible)
                Reload();
        };
        Prompt = prompt;
    }

    private void ShowPicker(DestinationPickerViewModel picker)
    {
        Prompt = null;
        picker.Closed += (_, _) =>
        {
            if (ReferenceEquals(Picker, picker))
                Picker = null;
            if (_reloadPending && _isPanelVisible)
                Reload();
        };
        Picker = picker;
    }

    /// <summary>A message about an operation just done, with what the buttons next to it can do.</summary>
    private void Report(string message, StatusNotice notice)
    {
        _noticeFollows = true;
        try
        {
            Status = message;
        }
        finally
        {
            _noticeFollows = false;
        }
        Notice = notice;
    }

    private void RunSearch()
    {
        Results.Clear();
        if (!IsSearching)
            return;
        if (_index is null)
        {
            ResultsHeader = "Indicizzazione in corso…";
            return;
        }
        var hits = _index.Search(Query, MaxResults);
        foreach (var hit in hits)
            Results.Add(new SearchResultViewModel(hit) { IsCut = _cutPaths.Contains(hit.Item.FullPath) });
        ResultsHeader = hits.Count switch
        {
            0 => "Nessun risultato. Prova con una parte del nome, o con il nome della cartella.",
            1 => "1 risultato",
            MaxResults => $"Primi {MaxResults} risultati",
            _ => $"{hits.Count} risultati",
        };
    }

    private async void RebuildIndex()
    {
        _indexing?.Cancel();
        var indexing = _indexing = new CancellationTokenSource();
        var root = _root;
        var sources = _reader.InboxSources;
        try
        {
            var index = await Task.Run(() => SearchIndex.Build(root, sources, indexing.Token), indexing.Token);
            if (indexing.IsCancellationRequested)
                return;
            _index = index;
            UpdateStatus();
            if (IsSearching)
                RunSearch();
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer rebuild.
        }
        catch (Exception e)
        {
            Log.Write($"Indexing failed: {e}");
            Status = $"Ricerca non disponibile: {e.Message}";
        }
    }

    private void StartWatching()
    {
        try
        {
            _watcher = new ArchiveWatcher(_root, TimeSpan.FromMilliseconds(400),
                _reader.InboxSources.SelectMany(source => source.Folders));
            _watcher.Changed += OnArchiveChanged;
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            Log.Write($"Cannot watch {_root} for changes, the panel will reload every time it opens: {e.Message}");
        }
    }

    private void OnArchiveChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(() =>
    {
        // Reloading while a name is being typed, or during an operation, would throw that work away.
        var editing = EditTargets().Any(target => target.IsEditing);
        if (_isPanelVisible && Prompt is null && Picker is null && !IsBusy && !editing)
            Reload();
        else
            _reloadPending = true;
        RebuildIndex();
    });

    private void UpdateStatus()
    {
        var parts = new List<string> { _root };
        if (_index is not null)
            parts.Add(Count(_index.Count, "elemento", "elementi"));
        var summary = string.Join("   ·   ", parts);
        // A message about the last operation stays until the panel opens again: the summary takes the place of an older one.
        if (Status.Length == 0 || Status == _summary)
            Status = summary;
        _summary = summary;
    }

    private bool TryShell(Action action, string name)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write($"Could not open {name}: {e}");
            Status = $"Impossibile aprire «{name}»: {e.Message}";
            return false;
        }
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count:N0} {many}";
}
