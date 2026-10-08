using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
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
    private const int MaxToasts = 3;

    private readonly bool _live;
    private readonly bool _readOnly;
    private IReadOnlyList<InboxSource> _inboxSources;
    private readonly RecycleBin _recycleBin;
    private readonly RecentDestinations _recent;
    private readonly UndoHistory _history = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    // The shell's operations: on a thread of their own in the app, one at a time; null in previews and tests, which run them at once.
    private readonly FileOperationQueue? _shell;
    private readonly SemaphoreSlim _fileWork = new(1, 1);
    private int _fileWorkCount;
    private TaskCompletionSource? _idle;
    private bool _renaming;
    private bool _indexPending;
    private string _root;
    private ArchiveReader _reader;
    private ArchiveWatcher? _watcher;
    private SearchIndex? _index;
    private CancellationTokenSource? _indexing;
    private TopicViewModel? _selectedTopic;
    private string? _selectedTopicPath;
    private HashSet<string>? _selectAfterReload;
    private bool _isPanelVisible;
    private bool _reloadPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(IsBoardVisible), nameof(IsCanvasVisible))]
    private string _query = "";

    /// <summary>The canvas instead of the board: the whole archive at once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardVisible), nameof(IsCanvasVisible), nameof(ViewIndex))]
    private bool _isCanvasShown;

    /// <summary>Where the board's topic sits, like "Lavoro › Clienti"; empty for topics in the root.</summary>
    [ObservableProperty]
    private string _boardPath = "";

    [ObservableProperty]
    private string _boardTitle = "";

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _resultsHeader = "";

    /// <summary>
    /// The last thing Quiver said: in a toast, or next to the LEDs of the status bar while an operation runs. Kept for the
    /// tests, and for what reads back the problem just told.
    /// </summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>The operation under way, next to the LEDs of the status bar.</summary>
    [ObservableProperty]
    private string _activity = "";

    /// <summary>"55.781 elementi · 2,3 GB", in the status bar once the archive has been read whole; empty before.</summary>
    [ObservableProperty]
    private string _archiveSummary = "";

    /// <summary>"3 elementi selezionati · 12,4 MB", as in Explorer; null without a selection.</summary>
    [ObservableProperty]
    private string? _selectionSummary;

    /// <summary>"120 GB liberi" on the disk of the archive, or "Poco spazio: 3,1 GB liberi"; null when Windows cannot say.</summary>
    [ObservableProperty]
    private string? _diskText;

    [ObservableProperty]
    private string? _diskToolTip;

    [ObservableProperty]
    private bool _isDiskLow;

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
    /// Indexes the archive in the background, follows its changes, and moves and copies files on a thread of their own.
    /// Without it (previews, tests) the board is read only when asked, the search needs <see cref="BuildIndexNow"/>, and
    /// file operations are done before their method returns.
    /// </param>
    /// <param name="inboxSources">Places outside the archive shown in "Da smistare", like the Desktop.</param>
    /// <param name="recycleBin">Where Ctrl+Z finds what went to the Recycle Bin; by default, the one of Windows.</param>
    /// <param name="recent">Where files were sent last, offered first by «Sposta in…»; by default, those of this PC.</param>
    /// <param name="readOnly">
    /// For previews and measures, which look at the archive and write nothing into it: the colors new topics get are only
    /// shown, not kept.
    /// </param>
    internal MainViewModel(string root, bool live = true, IReadOnlyList<InboxSource>? inboxSources = null, RecycleBin? recycleBin = null,
        RecentDestinations? recent = null, bool readOnly = false)
    {
        _live = live;
        _readOnly = readOnly;
        _shell = live ? new FileOperationQueue() : null;
        _inboxSources = inboxSources ?? [];
        _recycleBin = recycleBin ?? new RecycleBin(WindowsFolders.RecycleBinOf);
        _recent = recent ?? RecentDestinations.Load();
        LoadArchive(root);
    }

    /// <summary>
    /// Raised when a file operation ends. The shell's own windows take the focus during the operation
    /// and do not give it back, so the window may have to take it again.
    /// </summary>
    public event EventHandler? FileOperationCompleted;

    /// <summary>Raised when items were selected after a reload (renamed, moved, pasted): the view focuses them.</summary>
    public event EventHandler? SelectionRestored;

    /// <summary>«Mostra» on the canvas: the view flies to the tile of this folder, where the items are selected.</summary>
    public event EventHandler<string>? ShowOnCanvasRequested;

    /// <summary>
    /// Raised after the topics and the board were read again. Their elements are new, so the view puts back the
    /// keyboard focus if it was on one of the old ones.
    /// </summary>
    public event EventHandler? Reloaded;

    public ObservableCollection<TopicViewModel> Topics { get; } = [];

    public ObservableCollection<TopicViewModel> SubTopics { get; } = [];

    public ObservableCollection<TileViewModel> Tiles { get; } = [];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    /// <summary>The zones of the canvas, read when it is shown.</summary>
    public CanvasViewModel Canvas { get; } = new();

    /// <summary>The toasts in the bottom right corner, the newest last: what operations did, and what went wrong.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <summary>For the tests: how much room the disk of a folder has; that of Windows in the app.</summary>
    internal Func<string, DiskSpace?> DiskOf { get; set; } = DiskSpace.Of;

    public string Root => _root;

    /// <summary>The name of the archive in use: that of its folder.</summary>
    public string ArchiveName => Path.GetFileName(_root);

    /// <summary>The archive in the status bar: where it is, and what a click on it does.</summary>
    public string ArchiveToolTip => $"{_root}: apri in Explorer";

    public string InboxPath => _reader.InboxPath;

    /// <summary>The folder the board shows: files pasted or dropped on its background go there.</summary>
    public string? BoardFolder => IsInboxShown ? _reader.InboxPath : _selectedTopic?.Topic.FullPath;

    public TopicViewModel? SelectedTopic => _selectedTopic;

    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    public bool IsBoardVisible => !IsSearching && !IsCanvasShown;

    public bool IsCanvasVisible => !IsSearching && IsCanvasShown;

    /// <summary>For the switch «Bacheca | Tela»: 0 for the board, 1 for the canvas.</summary>
    public int ViewIndex
    {
        get => IsCanvasShown ? 1 : 0;
        set => IsCanvasShown = value == 1;
    }

    public bool HasInboxItems => InboxCount > 0;

    public bool CanUndo => _history.Last is not null;

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
        TopicOrderStep or TopicMoveStep => "Annulla lo spostamento dell'argomento",
        TopicStep { WasTopic: false } => "Annulla «Promuovi ad argomento»",
        TopicStep => "Annulla «Riporta a riquadro»",
        ColorStep => "Annulla il colore",
        ArrangeStep => "Annulla l'ordinamento",
        SizeStep => "Annulla il ridimensionamento",
        _ => "Annulla",
    };

    /// <summary>The panel window, owner of the progress and conflict dialogs of file operations.</summary>
    public nint OwnerWindow { get; set; }

    /// <summary>
    /// True while a move, copy, deletion or undo runs or waits for its turn: the status bar shows it, and the board is
    /// read again when it is over.
    /// </summary>
    public bool IsBusy => _fileWorkCount > 0;

    /// <summary>Done when no file operation runs or waits: «Esci» waits for it, so that a copy is not cut halfway.</summary>
    public Task WhenIdle() => IsBusy ? (_idle ??= new TaskCompletionSource()).Task : Task.CompletedTask;

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
        // What was done in another archive cannot be undone from this one, nor told about here.
        _history.Clear();
        Toasts.Clear();
        NotifyHistoryChanged();
        IsInboxShown = false;
        OnPropertyChanged(nameof(ArchiveName));
        OnPropertyChanged(nameof(ArchiveToolTip));
        UpdateArchiveSummary();
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

    /// <summary>The places of «Da smistare» changed in the settings: the archive is read again with them, the history kept.</summary>
    public void UseInboxSources(IReadOnlyList<InboxSource> sources)
    {
        _inboxSources = sources;
        ReleaseArchive();
        Reload();
        if (!_live)
            return;
        RebuildIndex();
        StartWatching();
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
        // The toasts were for the previous time.
        Toasts.Clear();
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

    /// <summary>
    /// The window came forward again, from the taskbar, Alt+Tab or a click: what changed meanwhile shows, dates included,
    /// unless something is under way that a reload would throw away.
    /// </summary>
    public void OnWindowActivated()
    {
        if (!_isPanelVisible || Prompt is not null || Picker is not null || IsBusy || EditTargets().Any(target => target.IsEditing))
            return;
        Reload();
    }

    public void ReportOpenTime(TimeSpan elapsed) => Log.Write($"Panel shown in {elapsed.TotalMilliseconds:N0} ms");

    /// <summary>Builds the search index synchronously, for preview rendering.</summary>
    public void BuildIndexNow()
    {
        _index = SearchIndex.Build(_root, _reader.InboxSources);
        UpdateArchiveSummary();
    }

    /// <summary>Whether a file or folder waits to be sorted: dragged away from there, it is moved even to another drive.</summary>
    public bool IsInInbox(string path) => _reader.IsInInbox(path);

    /// <summary>The selected items of the board or of the canvas, or of the search results while searching.</summary>
    public IReadOnlyList<ItemViewModel> SelectedItems() =>
        (IsSearching ? Results.Cast<ItemViewModel>() : ShownTiles.SelectMany(tile => tile.Items)).Where(item => item.IsSelected).ToList();

    /// <summary>The tiles on screen: those of the board, or those of every zone of the canvas.</summary>
    public IEnumerable<TileViewModel> ShownTiles => IsCanvasShown ? Canvas.AllTiles() : Tiles;

    /// <summary>The tiles next to this one: on its board, or in its zone of the canvas.</summary>
    public IReadOnlyList<TileViewModel> SiblingsOf(TileViewModel tile) =>
        Tiles.Contains(tile) ? Tiles : Canvas.ZoneWith(tile)?.Tiles ?? (IReadOnlyList<TileViewModel>)Tiles;

    // ---- Opening ----

    public void Open(IReadOnlyList<ItemViewModel> items)
    {
        foreach (var item in items)
            TryShell(() => ShellLauncher.Open(item.Item.FullPath), item.Name);
    }

    public void Reveal(ItemViewModel item) => TryShell(() => ShellLauncher.Reveal(item.Item.FullPath), item.Name);

    public void OpenFolder(string path) => TryShell(() => ShellLauncher.Open(path), Path.GetFileName(path));

    public void OpenWith(ItemViewModel item) => TryShell(() => ShellLauncher.OpenWith(item.Item.FullPath), item.Name);

    public void ShowProperties(string path) => TryShell(() => ShellLauncher.ShowProperties(path), Path.GetFileName(path));

    public void CopyPath(ItemViewModel item)
    {
        try
        {
            Clipboard.SetText(item.Item.FullPath);
            Tell(ToastKind.Success, "Percorso copiato", item.Item.FullPath);
        }
        catch (ExternalException)
        {
            Tell(ToastKind.Danger, "Gli appunti sono occupati da un altro programma. Riprova tra un attimo.");
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
            Tell(ToastKind.Danger, "Gli appunti sono occupati da un altro programma. Riprova tra un attimo.");
            return;
        }
        _cutPaths.Clear();
        if (cut)
            _cutPaths.UnionWith(paths);
        // On both views: switching from one to the other, the items cut still look so.
        foreach (var item in Tiles.Concat(Canvas.AllTiles()).SelectMany(tile => tile.Items).Concat(Results))
            item.IsCut = _cutPaths.Contains(item.Item.FullPath);
        if (cut)
            Tell(ToastKind.Info, $"{Count(items.Count, "elemento pronto", "elementi pronti")} da spostare: incolla con Ctrl+V dove vuoi.");
        else
            Tell(ToastKind.Success, $"{Count(items.Count, "elemento copiato", "elementi copiati")}.");
    }

    public async Task PasteInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Tell(ToastKind.Info, "Negli appunti non ci sono file da incollare.");
            return;
        }
        var destination = Path.GetFullPath(folder);
        if (clipboard.Paths.Any(path => TransferRules.IsSameOrInside(destination, Path.GetFullPath(path))))
        {
            Tell(ToastKind.Warning, "Una cartella non si può incollare dentro se stessa.");
            return;
        }
        if (clipboard.Cut)
        {
            if (clipboard.Paths.All(path => SamePath(Path.GetDirectoryName(path) ?? "", destination)))
            {
                Tell(ToastKind.Info, "I file sono già in questa cartella.");
                return;
            }
            _cutPaths.Clear();
            // Off the clipboard at once: a second Ctrl+V while the move runs would try to move the same files again.
            FileClipboard.Clear();
            await Transfer(clipboard.Paths, destination, TransferKind.Move);
        }
        else
        {
            await Transfer(clipboard.Paths, destination, TransferKind.Copy);
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
    public Task PasteLinksInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Tell(ToastKind.Info, "Negli appunti non ci sono file da collegare.");
            return Task.CompletedTask;
        }
        return Transfer(clipboard.Paths, folder, TransferKind.Link);
    }

    // ---- Creating ----

    /// <summary>Like "New folder" on the desktop: a tile named "Nuovo riquadro", with its name ready to type over.</summary>
    /// <param name="topic">Null for the topic on the board.</param>
    public void NewTile(TopicViewModel? topic = null)
    {
        topic ??= _selectedTopic;
        if (topic is null)
        {
            Tell(ToastKind.Info, "Per creare un riquadro, scegli prima un argomento.");
            return;
        }
        var folder = topic.Topic.FullPath;
        if (CreateFolder(folder, ArchiveNames.Unique(folder, "Nuovo riquadro")) is not { } path)
            return;
        Remember(new CreateStep(path));
        _selectedTopicPath = topic.Topic.RelativePath;
        IsInboxShown = false;
        Reload();
        if (ShownTiles.FirstOrDefault(tile => SamePath(tile.FolderPath, path)) is { } tile)
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
            Tell(ToastKind.Info, "Dai risultati della ricerca non si rinomina: premi Esc e apri l'argomento che lo contiene.");
            return;
        }
        CancelAllEdits();
        target.BeginEdit();
    }

    public void CancelRename(RenameableViewModel target)
    {
        target.IsEditing = false;
        // What changed while the name was being typed, like files arrived from a copy, shows now.
        if (_reloadPending && !EditTargets().Any(other => other.IsEditing))
            Reload();
    }

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
        if (!RenameNow(path, newPath))
            return $"Impossibile rinominare «{oldName}»: forse è aperto in un altro programma.";
        target.IsEditing = false;
        Remember(new RenameStep(path, newPath));
        KeepPlace(path, newPath);
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
            Start(Recycle(paths));
            return;
        }
        var title = paths.Count == 1
            ? $"Spostare «{Path.GetFileName(paths[0])}» nel Cestino?"
            : $"Spostare {paths.Count} elementi nel Cestino?";
        var label = paths.Count == 1 ? "Sposta nel Cestino" : $"Sposta {paths.Count} elementi nel Cestino";
        ShowPrompt(new PromptViewModel(title, label,
            _ =>
            {
                // The question closes; how it went is told in the status bar.
                Start(Recycle(paths));
                return null;
            },
            message: "Le cartelle ci vanno con tutto il loro contenuto. Si possono recuperare dal Cestino.",
            isDanger: true));
    }

    public async Task<bool> Recycle(IReadOnlyList<string> paths)
    {
        using var turn = await BeginFileWork(
            paths.Count == 1 ? $"Sposto «{DisplayNameOf(paths[0])}» nel Cestino…" : $"Sposto {paths.Count} elementi nel Cestino…");
        var folders = paths.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        var forgotten = SnapshotOf(paths);
        // The Recycle Bin dates what it receives: right after the deletion, it tells where each item went.
        var since = DateTime.UtcNow.AddSeconds(-2);
        var owner = OwnerWindow;
        var done = await RunShell(() => ShellFileOperations.Recycle(paths, owner));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        var gone = paths.Where(path => !Path.Exists(path)).ToList();
        if (gone.Count > 0)
        {
            var inBin = _recycleBin.FindDeleted(gone, since);
            var step = new RecycleStep(inBin, gone.Count - inBin.Count, forgotten.Restrict(ArchiveKeys(gone)));
            Remember(step);
            var where = inBin.Count == gone.Count ? "nel Cestino" : "stat{0} eliminat{0} definitivamente, senza passare dal Cestino";
            Report(gone.Count == 1
                    ? $"«{DisplayNameOf(gone[0])}» è {string.Format(where, "o")}."
                    : $"{gone.Count} elementi sono {string.Format(where, "i")}.",
                new StatusNotice(null, gone), step, inBin.Count == gone.Count ? ToastKind.Success : ToastKind.Warning);
        }
        else if (done)
        {
            Tell(ToastKind.Info, "Non c'era niente da spostare nel Cestino.");
        }
        else
        {
            Tell(ToastKind.Danger, "Lo spostamento nel Cestino non è riuscito.");
        }
        ReloadUnlessEditing();
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
        // The picker closes at once; the files go meanwhile, with the window still in hand.
        Start(Transfer(sources, destination.FullPath, kind));
        return null;
    }

    /// <summary>«Mostra» on the toast of an operation: the board goes where the items went, with them selected.</summary>
    public void ShowNotice(StatusNotice notice)
    {
        if (notice.ShowFolder is not { } folder)
            return;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        // On the canvas every tile is there already: the view flies to the right one.
        if (IsCanvasShown)
        {
            SelectAfterReload(notice.Paths);
            Reload();
            ShowOnCanvasRequested?.Invoke(this, full);
            return;
        }
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

    /// <summary>
    /// Moves or copies files dropped or pasted onto a folder of the archive, or makes shortcuts to them there. Waits for
    /// the operations already under way, then works out where things will land from the files as they are by then.
    /// </summary>
    public async Task Transfer(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind)
    {
        if (kind == TransferKind.None || paths.Count == 0)
            return;
        using var turn = await BeginFileWork(Doing(paths, kind, destinationFolder));
        await TransferNow(paths, destinationFolder, kind);
    }

    /// <summary>The transfer itself, by whoever holds the turn of the file operations.</summary>
    /// <param name="reload">False when the caller changes something more first, and reads the board again itself.</param>
    /// <returns>Whether the shell did all it was asked.</returns>
    private async Task<bool> TransferNow(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind, bool reload = true)
    {
        if (!EnsureFolder(destinationFolder))
            return false;
        if (kind == TransferKind.Link)
        {
            CreateLinks(paths, destinationFolder);
            return true;
        }
        // Files copied, or moved from another disk, need room where they go: better said now than a copy stopped halfway.
        if (await NotEnoughRoom(paths, destinationFolder, kind) is { } shortage)
        {
            Tell(ToastKind.Warning, shortage.Title, shortage.Body);
            UpdateDisk();
            return false;
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
        ShowActivity(Doing(paths, kind, destinationFolder));
        var owner = OwnerWindow;
        var done = await RunShell(() => kind == TransferKind.Copy
            ? ShellFileOperations.Copy(paths, destinationFolder, owner, renameOnCollision: nextToOriginal)
            : ShellFileOperations.Move(paths, destinationFolder, owner));
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
        // What «Annulla» on the toast takes back: this operation, if it can be undone at all.
        UndoStep? step = null;
        if (kind == TransferKind.Move)
        {
            var moved = landings.Where(item => !Path.Exists(item.From) && Path.Exists(item.To)).ToList();
            if (moved.Count > 0)
                Remember(step = new MoveStep(moved, forgotten));
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
                Remember(step = new CopyStep(arrived));
        }

        var present = arrived.Where(Path.Exists).ToList();
        var verb = kind == TransferKind.Copy ? ("copiato", "copiati") : ("spostato", "spostati");
        // Stopped in the dialog of the shell, or by a problem the shell has just told about.
        if (!done)
            Tell(ToastKind.Info, kind == TransferKind.Copy ? "La copia non è stata completata." : "Lo spostamento non è stato completato.");
        else if (present.Count > 0)
            Report(Done(present, verb, destinationFolder), new StatusNotice(destinationFolder, present), step);
        // Each one in the place of another with its name, as chosen in the dialog: done, with nothing new to point at.
        else
            Report(Done(paths, verb, destinationFolder), new StatusNotice(destinationFolder, []), step);
        // What arrived is selected, as in Explorer.
        SelectAfterReload(arrived);
        if (reload)
            ReloadUnlessEditing();
        return done;
    }

    /// <summary>"Sposto «a.txt» in «Casa»…", while the shell works.</summary>
    private static string Doing(IReadOnlyList<string> paths, TransferKind kind, string folder)
    {
        var verb = kind switch
        {
            TransferKind.Copy => "Copio",
            TransferKind.Link => "Collego",
            _ => "Sposto",
        };
        return paths.Count == 1
            ? $"{verb} «{DisplayNameOf(paths[0])}» in «{Path.GetFileName(folder)}»…"
            : $"{verb} {paths.Count} elementi in «{Path.GetFileName(folder)}»…";
    }

    /// <summary>
    /// Why the files would not fit on the disk they go to: those copied, and those moved there from another disk, which
    /// Windows copies before deleting them. Null when they fit, or when Windows cannot say how much room there is.
    /// </summary>
    private async Task<(string Title, string Body)?> NotEnoughRoom(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind)
    {
        var copied = kind == TransferKind.Copy ? paths : paths.Where(path => !ArchiveFolders.IsSameDrive(path, destinationFolder)).ToList();
        if (copied.Count == 0 || DiskOf(destinationFolder) is not { } disk)
            return null;
        // Counted away from the window: a big folder takes a while.
        var needed = _live ? await Task.Run(() => FolderSize.Of(copied)) : FolderSize.Of(copied);
        if (needed <= disk.Free)
            return null;
        Log.Write($"Not enough room on {disk.Name} for {paths.Count} items: {needed} bytes needed, {disk.Free} free");
        return ($"Su {disk.Name} non c'è abbastanza spazio",
            $"Servono {ByteSize.Format(needed)} e ne restano {ByteSize.Format(disk.Free)}. Libera un po' di spazio, poi riprova.");
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
            Tell(ToastKind.Danger, "Non sono riuscito a salvare quello che hai trascinato", e.Message);
        }
        if (created.Count > 0)
        {
            var step = new CopyStep(created);
            Remember(step);
            SelectAfterReload(created);
            Report(Done(created, ("salvato", "salvati"), destinationFolder), new StatusNotice(destinationFolder, created), step);
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
            Tell(ToastKind.Danger, $"Impossibile usare la cartella {folder}", e.Message);
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
                Tell(ToastKind.Danger, $"Impossibile creare il collegamento a «{Path.GetFileName(target)}»", e.Message);
            }
        }
        if (links.Count > 0)
        {
            var step = new LinkStep(links);
            Remember(step);
            SelectAfterReload(links);
            Report(links.Count == 1
                    ? $"Collegamento «{DisplayNameOf(links[0])}» creato in «{Path.GetFileName(destinationFolder)}»."
                    : $"{links.Count} collegamenti creati in «{Path.GetFileName(destinationFolder)}».",
                new StatusNotice(destinationFolder, links), step);
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
        var order = SiblingsOf(tile).Where(t => t.IsOwnFolder).Select(t => t.RelativePath!).ToList();
        var metadata = LoadMetadata();
        var previousOrder = order.ToDictionary(key => key, metadata.OrderOf, StringComparer.OrdinalIgnoreCase);
        order.Remove(tile.RelativePath!);
        var index = before?.RelativePath is { } target ? order.IndexOf(target) : -1;
        order.Insert(index < 0 ? order.Count : index, tile.RelativePath!);
        ChangeMetadata(m => m.SetOrder(order), new TileOrderStep(previousOrder));
    }

    /// <summary>
    /// Puts a topic in another place in the sidebar: among the topics of <paramref name="parent"/> (null: those in the
    /// root), before <paramref name="before"/> or at the end. Next to the same topics only the order changes; under
    /// another topic its folder moves there, and it stays a topic.
    /// </summary>
    public async Task MoveTopic(TopicViewModel topic, TopicViewModel? parent, TopicViewModel? before)
    {
        if (ReferenceEquals(topic, before) || !CanGoUnder(topic, parent))
            return;
        if (ReferenceEquals(topic.Parent, parent))
        {
            var siblings = (parent?.Children ?? Topics).Select(sibling => sibling.Topic.RelativePath).ToList();
            var metadata = LoadMetadata();
            var previousOrders = siblings.ToDictionary(key => key, metadata.OrderOf, StringComparer.OrdinalIgnoreCase);
            ChangeMetadata(m => m.SetOrder(PlaceBefore(siblings, topic.Topic.RelativePath, before)), new TopicOrderStep(previousOrders));
            return;
        }

        using var turn = await BeginFileWork($"Sposto «{topic.Name}» in «{parent?.Name ?? ArchiveName}»…");
        var previousOrder = LoadMetadata().OrderOf(topic.Topic.RelativePath);
        var destination = parent?.Topic.FullPath ?? _root;
        var newPath = Path.Combine(destination, topic.Name);
        // Two topics with the same name would be merged into one: not something a drag should do.
        if (Path.Exists(newPath))
        {
            Tell(ToastKind.Warning, $"In «{parent?.Name ?? ArchiveName}» c'è già «{topic.Name}»: rinominane uno, poi riprova.");
            return;
        }
        if (!await TransferNow([topic.Topic.FullPath], destination, TransferKind.Move, reload: false) || !Directory.Exists(newPath))
        {
            Reload();
            return;
        }
        // Ctrl+Z puts it back where it was among the topics it left, not in the place it takes here.
        if (_history.Last is MoveStep move)
        {
            _history.Pop();
            var topicMove = new TopicMoveStep(move, previousOrder);
            // The toast of the move takes back all of it.
            foreach (var toast in Toasts.Where(toast => ReferenceEquals(toast.UndoStep, move)))
                toast.UndoStep = topicMove;
            Remember(topicMove);
        }
        var newKey = KeyOf(newPath);
        var order = (parent?.Children ?? Topics).Select(sibling => sibling.Topic.RelativePath)
            .Where(key => !string.Equals(key, newKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
        UpdateMetadata(m =>
        {
            // Below the root a folder is a topic only when marked; in the root every folder is one.
            var marked = parent is not null && m.SetTopic(newKey, true);
            return m.SetOrder(PlaceBefore(order, newKey, before)) | marked;
        });
        if (parent is not null)
            parent.IsExpanded = true;
        Reload();
    }

    /// <summary>Not inside itself, nor inside a topic that is inside it.</summary>
    public static bool CanGoUnder(TopicViewModel topic, TopicViewModel? parent) =>
        parent is null || (!ReferenceEquals(parent, topic) && !parent.Ancestors().Contains(topic));

    private static List<string> PlaceBefore(List<string> keys, string key, TopicViewModel? before)
    {
        keys.RemoveAll(other => string.Equals(other, key, StringComparison.OrdinalIgnoreCase));
        var index = before is null ? -1 : keys.FindIndex(other => string.Equals(other, before.Topic.RelativePath, StringComparison.OrdinalIgnoreCase));
        keys.Insert(index < 0 ? keys.Count : index, key);
        return keys;
    }

    /// <summary>Gives a tile the size its corner was dragged to, or the usual one with null, kept in the archive.</summary>
    public void ResizeTile(TileViewModel tile, TileSize? size)
    {
        if (tile.MetadataKey is not { } key)
            return;
        var previous = LoadMetadata().SizeOf(key);
        var wanted = size?.Clamped() is { IsUsual: false } clamped ? clamped : null;
        if (previous == wanted)
        {
            // Dragged and brought back: what the drag showed goes away.
            tile.Size = previous;
            return;
        }
        ChangeMetadata(m => m.SetSize(key, wanted), new SizeStep(key, tile.Title, previous));
    }

    /// <summary>
    /// «Ordina per»: the items of the tile by name, date, type or size. A new key starts in its usual direction; the order
    /// given by hand goes.
    /// </summary>
    public void SortTile(TileViewModel tile, SortKey key)
    {
        if (tile.MetadataKey is not { } metadataKey)
            return;
        var sort = tile.SortedBy == key ? new ItemSort(key, tile.IsDescending) : ItemSort.For(key);
        Arrange(tile, metadataKey, m => m.SetSort(metadataKey, sort));
    }

    /// <summary>«Crescente» or «Decrescente», for the sort in use, by name when the items were in order by hand.</summary>
    public void SetSortDirection(TileViewModel tile, bool descending)
    {
        if (tile.MetadataKey is not { } metadataKey)
            return;
        var sort = new ItemSort(tile.SortedBy ?? SortKey.Name, descending);
        Arrange(tile, metadataKey, m => m.SetSort(metadataKey, sort));
    }

    /// <summary>
    /// Items dragged to another place in their tile, before <paramref name="before"/> or at the end: the tile keeps that
    /// order from now on, and what arrives later goes after them.
    /// </summary>
    public void MoveItems(TileViewModel tile, IReadOnlyList<ItemViewModel> items, ItemViewModel? before)
    {
        if (tile.MetadataKey is not { } key || items.Count == 0 || (before is not null && items.Contains(before)))
            return;
        var current = tile.Items.Select(item => item.Item.Name).ToList();
        // In the order they have in the tile, wherever they were picked from.
        var moving = tile.Items.Where(items.Contains).Select(item => item.Item.Name).ToList();
        var names = current.Except(moving, StringComparer.OrdinalIgnoreCase).ToList();
        var index = before is null ? -1 : names.FindIndex(name => string.Equals(name, before.Item.Name, StringComparison.OrdinalIgnoreCase));
        names.InsertRange(index < 0 ? names.Count : index, moving);
        if (names.SequenceEqual(current, StringComparer.OrdinalIgnoreCase))
            return;
        SelectAfterReload(items.Select(item => item.Item.FullPath));
        Arrange(tile, key, m => m.SetItemOrder(key, names));
    }

    private void Arrange(TileViewModel tile, string key, Func<ArchiveMetadata, bool> change)
    {
        var metadata = LoadMetadata();
        ChangeMetadata(change, new ArrangeStep(key, tile.Title, metadata.SortOf(key), metadata.ItemOrderOf(key)));
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

    /// <summary>Gives a tile a color of the palette, or none: a stripe on its title, kept in the metadata of the archive.</summary>
    public void SetTileColor(TileViewModel tile, string? color)
    {
        if (tile.RelativePath is not { } key)
            return;
        var metadata = LoadMetadata();
        ChangeMetadata(m => m.SetColor(key, color), new ColorStep(key, tile.Title, metadata.ColorOf(key), metadata.IsColorAutomatic(key)));
    }

    /// <summary>
    /// «Colore» of a topic: one of the palette, none on purpose (<see cref="TopicColors.None"/>), or with null the automatic
    /// one: for a topic of the root the color least used by the others, for one inside another that of its topic. Kept in
    /// the metadata of the archive; Ctrl+Z takes it back.
    /// </summary>
    public void SetTopicColor(TopicViewModel topic, string? color)
    {
        var key = topic.Topic.RelativePath;
        var metadata = LoadMetadata();
        ChangeMetadata(Choose, new ColorStep(key, topic.Name, metadata.ColorOf(key), metadata.IsColorAutomatic(key)));

        bool Choose(ArchiveMetadata m)
        {
            if (color is not null)
                return m.SetColor(key, color);
            if (topic.Parent is not null)
                return m.SetColor(key, null);
            var others = Topics.Where(other => !ReferenceEquals(other, topic)).Select(other => m.ColorOf(other.Topic.RelativePath));
            return m.SetColor(key, TopicColors.LeastUsed(others), automatic: true);
        }
    }

    // ---- Undo ----

    /// <summary>
    /// Takes back the last thing done in Quiver, as Ctrl+Z does in Explorer. During a move or a copy it waits for it to
    /// end, and then takes that back.
    /// </summary>
    public async Task Undo()
    {
        CommitAllEdits();
        using var turn = await BeginFileWork("Annullo l'ultima operazione…");
        if (_history.Pop() is not { } step)
        {
            Tell(ToastKind.Info, "Non c'è niente da annullare.");
            return;
        }
        // Its toast offered «Annulla»: now it is done, and what the undo did takes its place.
        foreach (var toast in Toasts.Where(toast => ReferenceEquals(toast.UndoStep, step)).ToList())
            Toasts.Remove(toast);
        NotifyHistoryChanged();
        var outcome = step switch
        {
            MoveStep move => await UndoMove(move),
            TopicMoveStep topicMove => await UndoTopicMove(topicMove),
            CopyStep copy => await RecycleCreated(copy, copy.Copies, "Copia annullata", "le copie", "copie"),
            LinkStep link => await RecycleCreated(link, link.Links, "Collegamento annullato", "i collegamenti", "collegamenti"),
            RenameStep rename => UndoRename(rename),
            CreateStep create => UndoCreate(create),
            RecycleStep recycle => UndoRecycle(recycle),
            TileOrderStep order => UndoOrder(order.PreviousOrder, "I riquadri sono tornati nell'ordine di prima."),
            TopicOrderStep order => UndoOrder(order.PreviousOrder, "Gli argomenti sono tornati nell'ordine di prima."),
            TopicStep topic => UndoTopic(topic),
            ColorStep color => UndoColor(color),
            ArrangeStep arrange => UndoArrange(arrange),
            SizeStep size => UndoSize(size),
            _ => throw new InvalidOperationException($"Unknown undo step {step.GetType().Name}"),
        };
        if (outcome is { } said)
            Tell(said.Kind, said.Message);
        ReloadUnlessEditing();
    }

    /// <summary>What an undo has to say, and of what kind; null when a problem with the metadata has been told already.</summary>
    private readonly record struct Outcome(ToastKind Kind, string Message)
    {
        public static Outcome Done(string message) => new(ToastKind.Success, message);

        public static Outcome Refused(string message) => new(ToastKind.Warning, message);

        public static Outcome Failed(string message) => new(ToastKind.Danger, message);
    }

    private Outcome? UndoColor(ColorStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"«{step.Name}» non c'è più.");
        if (!UpdateMetadata(metadata => metadata.SetColor(step.Key, step.Previous, step.WasAutomatic)))
            return null;
        return Outcome.Done(step.Previous is null ? $"«{step.Name}» è di nuovo senza colore." : $"«{step.Name}» ha di nuovo il colore di prima.");
    }

    /// <summary>The undo did not happen, most likely because a file is open in another program: Ctrl+Z can try again.</summary>
    private void KeepForRetry(UndoStep step) => Remember(step);

    private async Task<Outcome?> UndoMove(MoveStep step)
    {
        // Only what is still where it went goes back, and only to a place that is still free.
        var items = step.Items.Where(item => Path.Exists(item.To) && !Path.Exists(item.From)).ToList();
        if (items.Count == 0)
            return Outcome.Refused("Lo spostamento non si può annullare: nel frattempo gli elementi sono stati spostati o rinominati.");
        ShowActivity(items.Count == 1
            ? $"Rimetto «{DisplayNameOf(items[0].To)}» dov'era…"
            : $"Rimetto {items.Count} elementi dov'erano…");
        var owner = OwnerWindow;
        await RunShell(() => ShellFileOperations.MoveEach(
            items.Select(item => item.To).ToList(), items.Select(item => item.From).ToList(), owner));
        var back = items.Where(item => Path.Exists(item.From) && !Path.Exists(item.To)).ToList();
        if (back.Count == 0)
        {
            KeepForRetry(step);
            return Outcome.Failed("Lo spostamento non è stato annullato: forse un elemento è aperto in un altro programma. Riprova con Ctrl+Z.");
        }
        FollowBack(back, step.Forgotten);
        SelectAfterReload(back.Select(item => item.From));
        var done = back.Count == 1
            ? $"Spostamento annullato: «{DisplayNameOf(back[0].From)}» è di nuovo in «{FolderName(back[0].From)}»."
            : $"Spostamento annullato: {back.Count} elementi sono tornati dov'erano.";
        return Outcome.Done(done + LeftAlone(step.Items.Count - back.Count));
    }

    private async Task<Outcome?> UndoTopicMove(TopicMoveStep step)
    {
        var outcome = await UndoMove(step.Move);
        // The folder came back with the place it had taken over there: it gets its own back.
        if (step.Move.Items is [var item] && Directory.Exists(item.From) && !Directory.Exists(item.To))
            UpdateMetadata(metadata => metadata.SetOrder(KeyOf(item.From), step.PreviousOrder));
        return outcome;
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
    private async Task<Outcome?> RecycleCreated(UndoStep step, IReadOnlyList<string> created, string undone, string them, string many)
    {
        var existing = created.Where(Path.Exists).ToList();
        if (existing.Count == 0)
            return Outcome.Refused($"Non c'è niente da annullare: {them} non ci sono più.");
        // Without a Recycle Bin, deleting is for good, changes made since included: not something an undo may do.
        if (!existing.All(RecycleBin.IsAvailableFor))
            return Outcome.Refused($"Annullamento non fatto: su questo disco non c'è il Cestino, quindi non si potrebbe più recuperare niente. Se {them} non servono, usa Canc.");
        var folders = existing.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        var owner = OwnerWindow;
        await RunShell(() => ShellFileOperations.Recycle(existing, owner));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        var removed = existing.Where(path => !Path.Exists(path)).ToList();
        if (removed.Count == 0)
        {
            KeepForRetry(step);
            return Outcome.Failed($"Annullamento non riuscito: {them} sono ancora lì. Riprova con Ctrl+Z.");
        }
        var done = removed.Count == 1
            ? $"{undone}: «{DisplayNameOf(removed[0])}» è nel Cestino."
            : $"{undone}: {removed.Count} {many} sono nel Cestino.";
        return Outcome.Done(done + LeftAlone(created.Count - removed.Count));
    }

    private Outcome? UndoRename(RenameStep step)
    {
        var newName = DisplayNameOf(step.To);
        if (!Path.Exists(step.To))
            return Outcome.Refused($"La rinomina non si può annullare: «{newName}» non c'è più.");
        var onlyTheCaseChanged = string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase);
        if (!onlyTheCaseChanged && Path.Exists(step.From))
            return Outcome.Refused($"La rinomina non si può annullare: nella cartella c'è già «{DisplayNameOf(step.From)}».");
        if (!RenameNow(step.To, step.From))
        {
            KeepForRetry(step);
            return Outcome.Failed($"Impossibile rinominare «{newName}»: forse è aperto in un altro programma. Riprova con Ctrl+Z.");
        }
        // The metadata follows the folder back, and so does the selection of a topic.
        KeepPlace(step.To, step.From);
        if (IsInsideArchive(step.From))
            FollowFolder(KeyOf(step.To), KeyOf(step.From));
        SelectAfterReload([step.From]);
        return Outcome.Done($"Rinomina annullata: si chiama di nuovo «{DisplayNameOf(step.From)}».");
    }

    private Outcome? UndoCreate(CreateStep step)
    {
        var name = Path.GetFileName(step.Folder);
        try
        {
            if (!Directory.Exists(step.Folder))
                return Outcome.Refused($"«{name}» non c'è più.");
            // Only an empty folder goes away for good: anything put in it since stays.
            if (Directory.EnumerateFileSystemEntries(step.Folder).Any())
                return Outcome.Refused($"«{name}» non è più vuota, quindi resta. Se non ti serve, spostala nel Cestino con Canc.");
            Directory.Delete(step.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            KeepForRetry(step);
            return Outcome.Failed($"Impossibile togliere «{name}»: {e.Message}");
        }
        // What it got meanwhile, like the mark of a topic, goes with it.
        if (IsInsideArchive(step.Folder))
            ForgetFolder(KeyOf(step.Folder));
        return Outcome.Done($"Annullata la creazione di «{name}».");
    }

    private Outcome? UndoRecycle(RecycleStep step)
    {
        if (step.Items.Count == 0)
            return Outcome.Refused("Non si può ripristinare: Windows l'ha eliminato definitivamente, senza passare dal Cestino.");
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
                ? Outcome.Failed("Il ripristino non è riuscito: forse una cartella è aperta in un altro programma. Riprova con Ctrl+Z.")
                : Outcome.Refused("Nel Cestino non c'è più, oppure al suo posto c'è già un altro elemento.");
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
        return Outcome.Done(done + LeftAlone(step.Items.Count - restored.Count - failed.Count) + lost);
    }

    private Outcome? UndoOrder(IReadOnlyDictionary<string, int?> previousOrder, string done)
    {
        // A tile or topic renamed or removed since has no folder to take the place.
        var existing = previousOrder.Where(entry => Directory.Exists(Path.Combine(_root, entry.Key))).ToList();
        var saved = UpdateMetadata(metadata =>
        {
            var changed = false;
            foreach (var (key, order) in existing)
                changed |= metadata.SetOrder(key, order);
            return changed;
        });
        return saved ? Outcome.Done(done) : null;
    }

    private Outcome? UndoArrange(ArrangeStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"«{step.Name}» non c'è più.");
        if (!UpdateMetadata(metadata => metadata.SetArrangement(step.Key, step.Sort, step.Items)))
            return null;
        return Outcome.Done($"«{step.Name}» è di nuovo nell'ordine di prima.");
    }

    private Outcome? UndoSize(SizeStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"«{step.Name}» non c'è più.");
        if (!UpdateMetadata(metadata => metadata.SetSize(step.Key, step.Previous)))
            return null;
        return Outcome.Done($"«{step.Name}» ha di nuovo la misura di prima.");
    }

    private Outcome? UndoTopic(TopicStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"«{step.Name}» non c'è più.");
        if (!UpdateMetadata(metadata => metadata.SetTopic(step.Key, step.WasTopic)))
            return null;
        return Outcome.Done(step.WasTopic ? $"«{step.Name}» è di nuovo un argomento." : $"«{step.Name}» è di nuovo un riquadro.");
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
        _shell?.Dispose();
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

    /// <summary>Reads topics and the current board again, keeping the selection if that topic still exists.</summary>
    private void Reload()
    {
        _reloadPending = false;
        var metadata = LoadMetadata();
        _reader = new ArchiveReader(_root, metadata, _inboxSources);
        var topics = _reader.ReadTopics();
        // A topic of the root seen for the first time gets its color, and keeps it in the archive; a preview only shows it.
        var rootKeys = topics.Select(topic => topic.RelativePath).ToList();
        if (TopicColors.AssignMissing(metadata, rootKeys))
        {
            if (!_readOnly)
                UpdateMetadata(m => TopicColors.AssignMissing(m, rootKeys));
            topics = _reader.ReadTopics();
        }
        // The topics, tiles and items still there keep their view models, with their selection, the name being typed
        // and the branches left open; the view hears only about what moved, arrived or went away.
        Reconciler.Reconcile(Topics, topics, topic => topic.RelativePath, topic => topic.Topic.RelativePath,
            topic => new TopicViewModel(topic, parent: null), (viewModel, topic) => viewModel.Update(topic));
        foreach (var topic in Topics)
            topic.RefreshColors();
        InboxCount = _reader.ReadInbox().Sum(tile => tile.Items.Count);

        if (IsInboxShown)
            ShowInbox();
        else if ((FindTopicOrParent(_selectedTopicPath) ?? Topics.FirstOrDefault()) is { } topic)
            SelectTopic(topic);
        else
            ShowEmptyArchive();
        if (IsCanvasShown)
            ReadCanvas();

        // What was just renamed, moved or pasted takes the place of the selection.
        ApplyPendingSelection();
        UpdateSelectionSummary();
        UpdateDisk();
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads the board of every topic for the canvas, «Da smistare» first: the zones, tiles and items still there keep
    /// their view models, with their selection and the names being typed.
    /// </summary>
    private void ReadCanvas()
    {
        List<ZoneModel> zones = [new(ZoneViewModel.InboxKey, "Da smistare", null, _reader.ReadInbox(), [])];
        zones.AddRange(Topics.Select(ZoneModelOf));
        ReconcileZones(Canvas.Zones, zones);
    }

    private ZoneModel ZoneModelOf(TopicViewModel topic) =>
        new(topic.Topic.RelativePath, topic.Name, topic, _reader.ReadBoard(topic.Topic).Tiles, topic.Children.Select(ZoneModelOf).ToList());

    private void ReconcileZones(ObservableCollection<ZoneViewModel> zones, IReadOnlyList<ZoneModel> models, bool nested = false) =>
        Reconciler.Reconcile(zones, models, model => model.Key, zone => zone.Key,
            model => FillZone(new ZoneViewModel(model.Key, model.Title, model.Topic) { IsNested = nested }, model),
            (zone, model) => FillZone(zone, model));

    private ZoneViewModel FillZone(ZoneViewModel zone, ZoneModel model)
    {
        // The tiles of «Da smistare» are folders outside the archive, as on its board.
        if (model.Topic is null)
            Reconciler.Reconcile(zone.Tiles, model.Tiles, tile => TileViewModel.KeyOf(tile.FolderPath, isGeneral: false), tile => tile.Key,
                tile => new TileViewModel(tile.Title, tile.FolderPath, tile.Items, _cutPaths), (viewModel, tile) => viewModel.Update(tile));
        else
            Reconciler.Reconcile(zone.Tiles, model.Tiles, tile => TileViewModel.KeyOf(tile.FolderPath, tile.IsGeneral), tile => tile.Key,
                tile => new TileViewModel(tile, _root, _cutPaths), (viewModel, tile) => viewModel.Update(tile));
        ReconcileZones(zone.Children, model.Children, nested: true);
        zone.Update(model.Title, model.Topic);
        return zone;
    }

    /// <summary>A zone as read from the archive: its topic (none for «Da smistare»), its tiles and the zones inside it.</summary>
    private sealed record ZoneModel(string Key, string Title, TopicViewModel? Topic, IReadOnlyList<Tile> Tiles, IReadOnlyList<ZoneModel> Children);

    /// <summary>A name being typed in one view is confirmed before the other takes its place.</summary>
    partial void OnIsCanvasShownChanging(bool value) => CommitAllEdits();

    partial void OnIsCanvasShownChanged(bool value)
    {
        if (value)
            ReadCanvas();
        else
            Reload();
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
        var found = ShownTiles.SelectMany(tile => tile.Items).Where(item => paths.Contains(item.Item.FullPath)).ToList();
        if (found.Count == 0)
            return;
        ClearSelection();
        foreach (var item in found)
            item.IsSelected = true;
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<ItemViewModel> SelectedBoardItems() => ShownTiles.SelectMany(tile => tile.Items).Where(item => item.IsSelected);

    private void ClearSelection()
    {
        foreach (var item in SelectedBoardItems().ToList())
            item.IsSelected = false;
    }

    /// <summary>Everything whose name can be edited in place: topics, tiles and items.</summary>
    private IEnumerable<RenameableViewModel> EditTargets() =>
        Topics.SelectMany(topic => topic.SelfAndDescendants())
            .Concat<RenameableViewModel>(ShownTiles)
            .Concat(ShownTiles.SelectMany(tile => tile.Items));

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
                Tell(ToastKind.Info, problem);
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
        ShownTiles.SelectMany(tile => tile.Items).FirstOrDefault(item => SamePath(item.Item.FullPath, path));

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
            Tell(ToastKind.Danger, $"Impossibile creare la cartella «{name}»", e.Message);
            return null;
        }
    }

    /// <summary>A renamed item keeps its place in the order its tile was given by hand.</summary>
    private void KeepPlace(string oldPath, string newPath)
    {
        if (Path.GetDirectoryName(oldPath) is { } folder && IsInsideArchive(folder))
            UpdateMetadata(metadata => metadata.RenameItem(KeyOf(folder), Path.GetFileName(oldPath), Path.GetFileName(newPath)));
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
        // «Annulla» on a toast stays only while its operation is the one Ctrl+Z would undo.
        foreach (var toast in Toasts)
            toast.CanUndo = toast.UndoStep is not null && ReferenceEquals(toast.UndoStep, _history.Last);
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
            Tell(ToastKind.Danger, "Impossibile salvare le impostazioni dell'archivio", e.Message);
            return false;
        }
    }

    /// <summary>
    /// Waits for the file operations already under way, then holds the turn until disposed: each operation starts from
    /// the files as the previous one left them, and Ctrl+Z takes back the operation it waited for.
    /// </summary>
    /// <param name="activity">What the status bar says once the turn comes, next to its LEDs.</param>
    private async Task<IDisposable> BeginFileWork(string activity)
    {
        if (++_fileWorkCount == 1)
            OnPropertyChanged(nameof(IsBusy));
        await _fileWork.WaitAsync();
        ShowActivity(activity);
        return new FileWorkTurn(this);
    }

    private void EndFileWork()
    {
        _fileWork.Release();
        FileOperationCompleted?.Invoke(this, EventArgs.Empty);
        if (--_fileWorkCount > 0)
            return;
        OnPropertyChanged(nameof(IsBusy));
        _idle?.TrySetResult();
        _idle = null;
        // While files kept arriving the index waited: it is built once, now.
        if (_indexPending)
        {
            _indexPending = false;
            RebuildIndex();
        }
    }

    private sealed class FileWorkTurn(MainViewModel owner) : IDisposable
    {
        private bool _ended;

        public void Dispose()
        {
            if (_ended)
                return;
            _ended = true;
            owner.EndFileWork();
        }
    }

    /// <summary>An operation of the shell: on its own thread in the app, here and now in previews and tests.</summary>
    private Task<bool> RunShell(Func<bool> operation) => _shell?.Run(operation) ?? Task.FromResult(operation());

    /// <summary>A rename, done at once: it is quick, and the field of the name waits for its answer.</summary>
    private bool RenameNow(string path, string newPath)
    {
        // The shell may show a dialog, and meanwhile the window gets its messages: no reload in the middle of it.
        _renaming = true;
        try
        {
            return ShellFileOperations.Rename(path, newPath, OwnerWindow);
        }
        finally
        {
            _renaming = false;
            FileOperationCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Reads the board again, unless a name is being typed: then as soon as it is done.</summary>
    private void ReloadUnlessEditing()
    {
        if (EditTargets().Any(target => target.IsEditing))
            _reloadPending = true;
        else
            Reload();
    }

    /// <summary>
    /// An operation started from a question or a picker, which close without waiting for it: an unexpected error still
    /// reaches the handler of the app, as if it had happened right there.
    /// </summary>
    private void Start(Task operation) =>
        operation.ContinueWith(
            failed => _dispatcher.InvokeAsync(() => ExceptionDispatchInfo.Throw(failed.Exception!.GetBaseException())),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

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

    /// <summary>Something to say, in a toast of its kind: problems stay until closed, and go in the log too.</summary>
    internal void Tell(ToastKind kind, string title, string? body = null) => Tell(new ToastViewModel(kind, title, body));

    internal void Tell(ToastViewModel toast)
    {
        Status = toast.Body is null ? toast.Title : $"{toast.Title}: {toast.Body}";
        if (toast.Kind is ToastKind.Warning or ToastKind.Danger)
            Log.Write($"Told: {Status}");
        // The same news again takes the place of the one already there, and no more than three at once.
        foreach (var same in Toasts.Where(other => other.Title == toast.Title && other.Body == toast.Body).ToList())
            Toasts.Remove(same);
        Toasts.Add(toast);
        while (Toasts.Count > MaxToasts)
            Toasts.RemoveAt(0);
    }

    /// <summary>What an operation just did: «Annulla» takes back <paramref name="step"/>, «Mostra» goes where the items went.</summary>
    private void Report(string message, StatusNotice notice, UndoStep? step, ToastKind kind = ToastKind.Success) =>
        Tell(new ToastViewModel(kind, message, notice: notice.ShowFolder is null ? null : notice, undoStep: step)
        {
            CanUndo = step is not null && ReferenceEquals(step, _history.Last),
        });

    /// <summary>The operation under way, next to the LEDs of the status bar.</summary>
    private void ShowActivity(string message)
    {
        Status = message;
        Activity = message;
    }

    /// <summary>«Mostra» on a toast.</summary>
    public void ShowFrom(ToastViewModel toast)
    {
        if (toast.Notice is { } notice)
            ShowNotice(notice);
    }

    /// <summary>«Annulla» on a toast: the operation it tells about, while it is still the last one done.</summary>
    public Task UndoFrom(ToastViewModel toast) => toast.CanUndo ? Undo() : Task.CompletedTask;

    public void DismissToast(ToastViewModel toast) => Toasts.Remove(toast);

    /// <summary>The selection changed, on the board, on the canvas or among the results: the status bar says how much it is.</summary>
    public void UpdateSelectionSummary()
    {
        var selected = SelectedItems();
        if (selected.Count == 0)
        {
            SelectionSummary = null;
            return;
        }
        // As in Explorer, the size of the files: that of folders would take a count of everything inside them.
        var bytes = selected.Sum(item => item.Item.Size ?? 0);
        var count = Count(selected.Count, "elemento selezionato", "elementi selezionati");
        SelectionSummary = bytes > 0 ? $"{count} · {ByteSize.Format(bytes)}" : count;
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
            UpdateArchiveSummary();
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
            Tell(ToastKind.Danger, "Ricerca non disponibile", e.Message);
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
        // Reloading while a name is being typed, or during an operation, would throw that work away. An operation reads
        // the board again when it ends.
        var editing = EditTargets().Any(target => target.IsEditing);
        if (_isPanelVisible && Prompt is null && Picker is null && !IsBusy && !_renaming && !editing)
            Reload();
        else
            _reloadPending = true;
        // A long copy keeps changing the archive: the index is built once at its end, not again and again.
        if (IsBusy)
            _indexPending = true;
        else
            RebuildIndex();
    });

    /// <summary>The archive in the status bar, once it has been read whole: how many items, and what they take.</summary>
    private void UpdateArchiveSummary()
    {
        ArchiveSummary = _index is null
            ? ""
            : $"{Count(_index.ArchiveCount, "elemento", "elementi")} · {ByteSize.Format(_index.ArchiveBytes)}";
        UpdateDisk();
    }

    /// <summary>The free space of the disk of the archive: at every reload, after each operation and each time the panel opens.</summary>
    private void UpdateDisk()
    {
        var disk = DiskOf(_root);
        IsDiskLow = disk?.IsLow ?? false;
        DiskText = disk is null ? null : disk.IsLow ? $"Poco spazio: {disk.FreeText}" : disk.FreeText;
        DiskToolTip = disk is null
            ? null
            : string.Join(Environment.NewLine, new[]
            {
                disk.Description,
                _index is null ? null : $"L'archivio occupa {ByteSize.Format(_index.ArchiveBytes)}.",
                disk.IsLow ? "Resta poco spazio: le copie più grandi non ci stanno." : null,
            }.OfType<string>());
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
            Tell(ToastKind.Danger, $"Impossibile aprire «{name}»", e.Message);
            return false;
        }
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : $"{count:N0} {many}";
}
