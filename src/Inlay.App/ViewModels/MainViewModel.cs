using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inlay.App.Interop;
using Inlay.App.Services;
using Inlay.App.Views;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>State of the panel: topic tree, current board, search, and what can be done with files, as on the desktop.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxResults = 60;
    private const int MaxToasts = 3;
    private const string Everywhere = "Everywhere";
    // Numbers as the words around them: "18,481 items", whatever the language of Windows.
    private static readonly System.Globalization.CultureInfo English = System.Globalization.CultureInfo.GetCultureInfo("en-US");

    private readonly bool _live;
    private readonly bool _readOnly;
    private readonly RecycleBin _recycleBin;
    private readonly RecentDestinations _recent;
    private readonly UndoHistory _history = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly HashSet<string> _cutPaths = new(StringComparer.OrdinalIgnoreCase);
    // The file operations running or waiting for their turn, in the order they asked for it.
    private readonly List<FileWork> _fileWork = [];
    // How many operations and steps were asked for so far: the order of each step in the history.
    private long _asked;
    // That of the step being undone, to put it back in its place if the undo fails.
    private long _undoneOrder;
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

    /// <summary>The topics on the left; closed, the board or the canvas take the whole width (Ctrl+B).</summary>
    [ObservableProperty]
    private bool _isSidebarOpen = true;

    /// <summary>The shortcut that brings Inlay forward and puts it away, as the settings have it now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shortcuts))]
    private string _hotkey = AppSettings.DefaultHotkey;

    /// <summary>The canvas instead of the board: the whole archive at once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBoardVisible), nameof(IsCanvasVisible), nameof(ViewIndex))]
    private bool _isCanvasShown;

    /// <summary>Where the board's topic sits, like "Work › Clients"; empty for topics in the root.</summary>
    [ObservableProperty]
    private string _boardPath = "";

    [ObservableProperty]
    private string _boardTitle = "";

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _resultsHeader = "";

    /// <summary>Where the search looks, as the filter above the results says it: "Everywhere", or "In: Work › Clients".</summary>
    [ObservableProperty]
    private string _placeFilterText = Everywhere;

    /// <summary>The search looks only in some places, written "in:…".</summary>
    [ObservableProperty]
    private bool _hasPlaceFilter;

    /// <summary>
    /// The last thing Inlay said: in a toast, or next to the LEDs of the status bar while an operation runs. Kept for the
    /// tests, and for what reads back the problem just told.
    /// </summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>
    /// The operations under way, next to the LEDs of the status bar: the last one started, and how many more run or wait,
    /// as in "Copying “Photos” to “Work”… · 1 more running · 1 waiting".
    /// </summary>
    [ObservableProperty]
    private string _activity = "";

    /// <summary>Each operation under way or waiting, one a line: the tooltip of <see cref="Activity"/>.</summary>
    [ObservableProperty]
    private string _activityDetails = "";

    /// <summary>"55,781 items · 2.3 GB", in the status bar once the archive has been read whole; empty before.</summary>
    [ObservableProperty]
    private string _archiveSummary = "";

    /// <summary>"3 items selected · 12.4 MB", as in Explorer; null without a selection.</summary>
    [ObservableProperty]
    private string? _selectionSummary;

    /// <summary>"120 GB free" on the disk of the archive, or "Low on space: 3.1 GB free"; null when Windows cannot say.</summary>
    [ObservableProperty]
    private string? _diskText;

    [ObservableProperty]
    private string? _diskToolTip;

    [ObservableProperty]
    private bool _isDiskLow;

    /// <summary>"3 loose files in the vault": files no topic shows, in the status bar; null when there are none.</summary>
    [ObservableProperty]
    private string? _rootFilesText;

    /// <summary>A new version of Inlay downloaded and ready, as "0.4.0": the status bar offers to restart into it. Null when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private string? _updateReady;

    [ObservableProperty]
    private PromptViewModel? _prompt;

    /// <summary>«Move to…», open over the board.</summary>
    [ObservableProperty]
    private DestinationPickerViewModel? _picker;

    /// <param name="live">
    /// Indexes the archive in the background, follows its changes, and moves and copies files on threads of their own.
    /// Without it (previews, tests) the board is read only when asked, the search needs <see cref="BuildIndexNow"/>, and
    /// file operations are done before their method returns.
    /// </param>
    /// <param name="recycleBin">Where Ctrl+Z finds what went to the Recycle Bin; by default, the one of Windows.</param>
    /// <param name="recent">Where files were sent last, offered first by «Move to…»; by default, those of this PC.</param>
    /// <param name="readOnly">
    /// For previews and measures, which look at the archive and write nothing into it: the colors new topics get are only
    /// shown, not kept.
    /// </param>
    internal MainViewModel(string root, bool live = true, RecycleBin? recycleBin = null, RecentDestinations? recent = null,
        bool readOnly = false)
    {
        _live = live;
        _readOnly = readOnly;
        ShellRunner = live ? ShellThread.Run : operation => Task.FromResult(operation());
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

    /// <summary>«Show» on the canvas: the view flies to the tile of this folder, where the items are selected.</summary>
    public event EventHandler<string>? ShowOnCanvasRequested;

    /// <summary>The button of the status bar: the app restarts into the new version, once the operations on the files end.</summary>
    public event EventHandler? RestartToUpdateRequested;

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

    /// <summary>The keys worth knowing, under the «?» of the status bar: the shortcut among them is the one in use.</summary>
    public IReadOnlyList<ShortcutGroup> Shortcuts => ShortcutGroup.For(Hotkey);

    /// <summary>The kinds the search can be narrowed to, as filters above the results; checked, those written in the search box.</summary>
    public IReadOnlyList<SearchKindFilter> KindFilters { get; } = SearchKind.All.Select(kind => new SearchKindFilter(kind)).ToList();

    /// <summary>For the tests: how much room the disk of a folder has; that of Windows in the app.</summary>
    internal Func<string, DiskSpace?> DiskOf { get; set; } = DiskSpace.Of;

    /// <summary>
    /// Where an operation of the shell runs: on a thread of its own in the app, here and now in previews and tests. The
    /// tests can hold the operations there, to see which ones run together.
    /// </summary>
    internal Func<Func<bool>, Task<bool>> ShellRunner { get; set; }

    public string Root => _root;

    /// <summary>The name of the archive in use: that of its folder.</summary>
    public string ArchiveName => Path.GetFileName(_root);

    /// <summary>The archive in the status bar, which does not say its name: where it is, and what a click on it does.</summary>
    public string ArchiveToolTip => $"{_root}: click to open it in Explorer";

    /// <summary>"Inlay 0.4.0 is ready", in the status bar; null when there is no new version.</summary>
    public string? UpdateText => UpdateReady is { } version ? $"Inlay {version} is ready" : null;

    /// <summary>«Inlay X is ready» in the status bar, clicked.</summary>
    public void RestartToUpdate() => RestartToUpdateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The folder the board shows: files pasted or dropped on its background go there.</summary>
    public string? BoardFolder => _selectedTopic?.Topic.FullPath;

    public TopicViewModel? SelectedTopic => _selectedTopic;

    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    public bool IsBoardVisible => !IsSearching && !IsCanvasShown;

    public bool IsCanvasVisible => !IsSearching && IsCanvasShown;

    /// <summary>For the switch «Board | Mosaic»: 0 for the board, 1 for the canvas.</summary>
    public int ViewIndex
    {
        get => IsCanvasShown ? 1 : 0;
        set => IsCanvasShown = value == 1;
    }

    public bool CanUndo => _history.Last is not null;

    /// <summary>For the tests: what Ctrl+Z would undo.</summary>
    internal UndoStep? LastUndoStep => _history.Last;

    /// <summary>What Ctrl+Z would undo, for the menu.</summary>
    public string UndoLabel => _history.Last switch
    {
        MoveStep => "Undo move",
        CopyStep => "Undo copy",
        LinkStep => "Undo shortcut",
        RenameStep => "Undo rename",
        CreateStep => "Undo creation",
        RecycleStep => "Undo move to Recycle Bin",
        TileOrderStep => "Undo tile move",
        TopicOrderStep or TopicMoveStep => "Undo space move",
        TopicStep { WasTopic: false } => "Undo “Make it a space”",
        TopicStep => "Undo “Back to tile”",
        ItemStep { WasItem: true } => "Undo “Unfold as tile”",
        ItemStep => "Undo “Fold into General”",
        ColorStep => "Undo color",
        ArrangeStep => "Undo sort",
        SizeStep => "Undo resize",
        _ => "Undo",
    };

    /// <summary>The panel window, owner of the progress and conflict dialogs of file operations.</summary>
    public nint OwnerWindow { get; set; }

    /// <summary>
    /// True while a move, copy, deletion or undo runs or waits for its turn: the status bar shows it, and the board is
    /// read again when it is over.
    /// </summary>
    public bool IsBusy => _fileWork.Count > 0;

    /// <summary>Done when no file operation runs or waits: «Quit» waits for it, so that a copy is not cut halfway.</summary>
    public Task WhenIdle() => IsBusy ? (_idle ??= new TaskCompletionSource()).Task : Task.CompletedTask;

    [MemberNotNull(nameof(_root), nameof(_reader))]
    public void LoadArchive(string root)
    {
        _watcher?.Dispose();
        _watcher = null;
        _root = Path.GetFullPath(root);
        // An archive of the versions called Quiver: its .quiver becomes .inlay. A preview writes nothing, and reads it there.
        if (!_readOnly)
            ArchiveFolders.TakeOverOldMetadata(_root);
        _reader = new ArchiveReader(_root, new ArchiveMetadata());
        _index = null;
        _selectedTopic = null;
        _selectedTopicPath = null;
        // What was done in another archive cannot be undone from this one, nor told about here.
        _history.Clear();
        Toasts.Clear();
        NotifyHistoryChanged();
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
        _index = SearchIndex.Build(_root);
        UpdateArchiveSummary();
    }

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
            Tell(ToastKind.Success, "Path copied", item.Item.FullPath);
        }
        catch (ExternalException)
        {
            Tell(ToastKind.Danger, "Another program is using the clipboard. Try again in a moment.");
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
            Tell(ToastKind.Danger, "Another program is using the clipboard. Try again in a moment.");
            return;
        }
        _cutPaths.Clear();
        if (cut)
            _cutPaths.UnionWith(paths);
        // On both views: switching from one to the other, the items cut still look so.
        foreach (var item in Tiles.Concat(Canvas.AllTiles()).SelectMany(tile => tile.Items).Concat(Results))
            item.IsCut = _cutPaths.Contains(item.Item.FullPath);
        if (cut)
            Tell(ToastKind.Info, $"{Count(items.Count, "item", "items")} ready to move: paste with Ctrl+V wherever you want.");
        else
            Tell(ToastKind.Success, $"{Count(items.Count, "item", "items")} copied.");
    }

    public async Task PasteInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Tell(ToastKind.Info, "There are no files on the clipboard to paste.");
            return;
        }
        var destination = Path.GetFullPath(folder);
        if (clipboard.Paths.Any(path => TransferRules.IsSameOrInside(destination, Path.GetFullPath(path))))
        {
            Tell(ToastKind.Warning, "A folder can't be pasted inside itself.");
            return;
        }
        if (clipboard.Cut)
        {
            if (clipboard.Paths.All(path => SamePath(Path.GetDirectoryName(path) ?? "", destination)))
            {
                Tell(ToastKind.Info, "The files are already in this folder.");
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

    /// <summary>Like "New › Shortcut" in Explorer: a website or a path, typed or pasted, in <paramref name="folder"/>.</summary>
    public void AskNewLink(string folder) =>
        ShowPrompt(new PromptViewModel("New shortcut", "Create shortcut", text => CreateTypedLink(folder, text),
            initialText: "",
            message: "Type the address of a website, or the path of a program, a file or a folder."));

    /// <returns>Why no shortcut was made, shown under the field; null when it is done.</returns>
    private string? CreateTypedLink(string folder, string text)
    {
        var typed = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"').Trim());
        if (typed.Length == 0)
            return "Type an address or a path.";
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
                return "This path can't be found, and it doesn't look like the address of a website.";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            Log.Write($"Could not make a shortcut to {typed}: {e}");
            return $"Couldn't create the shortcut: {e.Message}";
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

    /// <summary>"Paste shortcut", as in Explorer: shortcuts to the files on the clipboard, which stay where they are.</summary>
    public Task PasteLinksInto(string folder)
    {
        if (FileClipboard.Read() is not { } clipboard || clipboard.Paths.Count == 0)
        {
            Tell(ToastKind.Info, "There are no files on the clipboard to make shortcuts to.");
            return Task.CompletedTask;
        }
        return Transfer(clipboard.Paths, folder, TransferKind.Link);
    }

    // ---- Creating ----

    /// <summary>Like "New folder" on the desktop: a tile named "New tile", with its name ready to type over.</summary>
    /// <param name="topic">Null for the topic on the board.</param>
    public void NewTile(TopicViewModel? topic = null)
    {
        topic ??= _selectedTopic;
        if (topic is null)
        {
            Tell(ToastKind.Info, "To create a tile, choose a space first.");
            return;
        }
        var folder = topic.Topic.FullPath;
        if (CreateFolder(folder, ArchiveNames.Unique(folder, "New tile")) is not { } path)
            return;
        Remember(new CreateStep(path));
        _selectedTopicPath = topic.Topic.RelativePath;
        Reload();
        if (ShownTiles.FirstOrDefault(tile => SamePath(tile.FolderPath, path)) is { } tile)
            BeginRename(tile);
    }

    /// <param name="parent">Null for a topic in the root of the archive.</param>
    public void NewTopic(TopicViewModel? parent)
    {
        var folder = parent?.Topic.FullPath ?? _root;
        if (CreateFolder(folder, ArchiveNames.Unique(folder, "New space")) is not { } path)
            return;
        Remember(new CreateStep(path));
        // Folders in the root are topics anyway; deeper ones have to be marked.
        if (parent is not null)
            UpdateMetadata(metadata => metadata.SetTopic(KeyOf(path), true));
        _selectedTopicPath = KeyOf(path);
        Reload();
        if (FindTopic(_selectedTopicPath) is { } topic)
            BeginRename(topic);
    }

    public void NewFolder(TileViewModel tile)
    {
        if (CreateFolder(tile.FolderPath, ArchiveNames.Unique(tile.FolderPath, "New folder")) is not { } path)
            return;
        Remember(new CreateStep(path));
        // The folder of «General» is that of its topic: a folder made there is one of its rows, not a tile of its own.
        if (tile.IsGeneral)
            UpdateMetadata(metadata => metadata.SetItem(KeyOf(path), true));
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
            Tell(ToastKind.Info, "Search results can't be renamed: press Esc and open the space that holds it.");
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
            return $"This folder already has a “{target.EditName.Trim()}”. Choose another name.";
        var isFolder = Directory.Exists(path);
        if (!RenameNow(path, newPath))
            return $"Couldn't rename “{oldName}”: it may be open in another program.";
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
            ? $"Move “{Path.GetFileName(paths[0])}” to the Recycle Bin?"
            : $"Move {paths.Count} items to the Recycle Bin?";
        var label = paths.Count == 1 ? "Move to Recycle Bin" : $"Move {paths.Count} items to Recycle Bin";
        ShowPrompt(new PromptViewModel(title, label,
            _ =>
            {
                // The question closes; how it went is told in the status bar.
                Start(Recycle(paths));
                return null;
            },
            message: "Folders go with everything in them. They can be restored from the Recycle Bin.",
            isDanger: true));
    }

    public async Task<bool> Recycle(IReadOnlyList<string> paths)
    {
        using var turn = await BeginFileWork(
            paths.Count == 1 ? $"Moving “{DisplayNameOf(paths[0])}” to the Recycle Bin…" : $"Moving {paths.Count} items to the Recycle Bin…",
            new FileTouches(paths));
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
            Remember(step, turn);
            var where = inBin.Count == gone.Count ? "{0} in the Recycle Bin" : "{1} deleted for good, without going through the Recycle Bin";
            Report(gone.Count == 1
                    ? $"“{DisplayNameOf(gone[0])}” {string.Format(where, "is", "was")}."
                    : $"{gone.Count} items {string.Format(where, "are", "were")}.",
                new StatusNotice(null, gone), step, inBin.Count == gone.Count ? ToastKind.Success : ToastKind.Warning);
        }
        else if (done)
        {
            Tell(ToastKind.Info, "There was nothing to move to the Recycle Bin.");
        }
        else
        {
            Tell(ToastKind.Danger, "Moving to the Recycle Bin didn't work.");
        }
        ReloadUnlessEditing();
        return done;
    }

    // ---- «Move to…» ----

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
        var link = sources.All(path => TransferRules.HasItsOwnPlace(path, _root));
        var destinations = _reader.ReadDestinations()
            .Where(destination => KindFor(sources, destination.FullPath, link) != TransferKind.None)
            .ToList();
        var name = DisplayNameOf(sources[0]);
        var title = (sources.Count, link) switch
        {
            (1, false) => $"Move “{name}” to…",
            (_, false) => $"Move {sources.Count} items to…",
            (1, true) => $"Create a shortcut to “{name}” in…",
            _ => $"Create {sources.Count} shortcuts in…",
        };
        ShowPicker(new DestinationPickerViewModel(title, link ? "Create shortcut" : "Move", destinations, _recent.For(_root),
            destination => SendTo(sources, destination, link)));
    }

    private static TransferKind KindFor(IReadOnlyList<string> sources, string destination, bool link) =>
        TransferRules.Choose(sources, destination, forceCopy: false, forceMove: !link, forceLink: link);

    /// <returns>Why nothing was done, for the picker; null when the files went.</returns>
    private string? SendTo(IReadOnlyList<string> sources, Destination destination, bool link)
    {
        var kind = KindFor(sources, destination.FullPath, link);
        if (kind == TransferKind.None)
            return "The files are already there.";
        _recent.Remember(_root, destination.RelativeKey);
        // The picker closes at once; the files go meanwhile, with the window still in hand.
        Start(Transfer(sources, destination.FullPath, kind));
        return null;
    }

    /// <summary>«Show» on the toast of an operation: the board goes where the items went, with them selected.</summary>
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
        // A tile shows on the board of its topic, the nearest topic up from its folder.
        if (IsInsideArchive(full))
            _selectedTopicPath = KeyOf(full);
        SelectAfterReload(notice.Paths);
        Reload();
    }

    // ---- Moving ----

    /// <summary>
    /// Moves or copies files dropped or pasted onto a folder of the archive, or makes shortcuts to them there. Waits for
    /// the operations before it on the same files, then works out where things will land from the files as they are by
    /// then; meanwhile the operations on other files go on.
    /// </summary>
    /// <param name="foldersAsRows">
    /// Folders that land in a topic become rows of its «General» (see <see cref="MarkArrivals"/>); false for a tile
    /// dragged by its title, which stays a tile.
    /// </param>
    public async Task Transfer(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind, bool foldersAsRows = true)
    {
        if (kind == TransferKind.None || paths.Count == 0)
            return;
        using var turn = await BeginFileWork(Doing(paths, kind, destinationFolder), FileTouches.Transfer(paths, destinationFolder));
        await TransferNow(turn, paths, destinationFolder, kind, foldersAsRows: foldersAsRows);
    }

    /// <summary>The transfer itself, by whoever holds its turn among the file operations.</summary>
    /// <param name="reload">False when the caller changes something more first, and reads the board again itself.</param>
    /// <param name="foldersAsRows">As in <see cref="Transfer"/>.</param>
    /// <returns>Whether the shell did all it was asked, and the step that undoes it, if any.</returns>
    private async Task<(bool Done, UndoStep? Step)> TransferNow(FileWork turn, IReadOnlyList<string> paths, string destinationFolder,
        TransferKind kind, bool reload = true, bool foldersAsRows = true)
    {
        if (!EnsureFolder(destinationFolder))
            return (false, null);
        if (kind == TransferKind.Link)
        {
            CreateLinks(turn, paths, destinationFolder);
            return (true, null);
        }
        // Files copied, or moved from another disk, need room where they go: better said now than a copy stopped halfway.
        if (await NotEnoughRoom(turn, paths, destinationFolder, kind) is { } shortage)
        {
            Tell(ToastKind.Warning, shortage.Title, shortage.Body);
            UpdateDisk();
            return (false, null);
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
        // What «Undo» on the toast takes back: this operation, if it can be undone at all.
        UndoStep? step = null;
        if (kind == TransferKind.Move)
        {
            var moved = landings.Where(item => !Path.Exists(item.From) && Path.Exists(item.To)).ToList();
            if (moved.Count > 0)
            {
                // A topic of the root is one by its place alone: in another topic it stays one, with the mark.
                var rootTopics = moved.Where(item => Directory.Exists(item.To) && IsInRoot(item.From)).Select(item => item.To).ToList();
                var rows = foldersAsRows ? MarkArrivals(moved.Select(item => item.To), destinationFolder, rootTopics) : [];
                Remember(step = new MoveStep(moved.Select(item => item with { BecameRow = rows.Contains(item.To) }).ToList(), forgotten), turn);
            }
        }
        // The copies are the new names, each starting like an original: "a.txt", or "a - Copia.txt" next to it, as Windows names it.
        // Without the names from before, the copies cannot be told from what was already there: no undo then.
        else if (namesBefore is not null && EntryNames(destinationFolder) is { } namesAfter)
        {
            arrived = namesAfter
                .Where(name => !namesBefore.Contains(name) &&
                               paths.Any(path => name.StartsWith(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)))
                .Select(name => Path.Combine(destinationFolder, name))
                .ToList();
            if (arrived.Count > 0)
            {
                // Ctrl+Z takes the copies to the Recycle Bin, and what they became with them.
                if (foldersAsRows)
                    MarkArrivals(arrived, destinationFolder, rootTopics: []);
                Remember(step = new CopyStep(arrived), turn);
            }
        }

        var present = arrived.Where(Path.Exists).ToList();
        var verb = kind == TransferKind.Copy ? "copied to" : "moved to";
        // Stopped in the dialog of the shell, or by a problem the shell has just told about.
        if (!done)
            Tell(ToastKind.Info, kind == TransferKind.Copy ? "The copy wasn't completed." : "The move wasn't completed.");
        else if (present.Count > 0)
            Report(Done(present, verb, destinationFolder), new StatusNotice(destinationFolder, present), step);
        // Each one in the place of another with its name, as chosen in the dialog: done, with nothing new to point at.
        else
            Report(Done(paths, verb, destinationFolder), new StatusNotice(destinationFolder, []), step);
        // What arrived is selected, as in Explorer.
        SelectAfterReload(arrived);
        if (reload)
            ReloadUnlessEditing();
        return (done, step);
    }

    /// <summary>
    /// Folders that arrive in a topic are rows of its «General», like files, not tiles of their own: a folder of photos
    /// dropped on a topic shows as one row, not as all its photos. One that comes as a topic, or as a row, stays so.
    /// </summary>
    /// <param name="rootTopics">
    /// Those that were topics of the root, where every folder is one without a mark: here they get it, and stay topics.
    /// </param>
    /// <returns>The folders that became rows, which Ctrl+Z turns back.</returns>
    private HashSet<string> MarkArrivals(IEnumerable<string> arrived, string destinationFolder, IReadOnlyCollection<string> rootTopics)
    {
        var rows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!IsInsideArchive(destinationFolder) || FindTopic(KeyOf(destinationFolder)) is null)
            return rows;
        var folders = arrived.Where(Directory.Exists).ToList();
        if (folders.Count == 0)
            return rows;
        UpdateMetadata(metadata =>
        {
            var changed = false;
            foreach (var folder in folders)
            {
                var key = KeyOf(folder);
                if (rootTopics.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    changed |= metadata.SetTopic(key, true);
                }
                else if (!metadata.IsTopic(key) && metadata.SetItem(key, true))
                {
                    rows.Add(folder);
                    changed = true;
                }
            }
            return changed;
        });
        return rows;
    }

    /// <summary>Whether a file or folder sits right in the root of the archive, as its topics do.</summary>
    private bool IsInRoot(string path) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))) is { } parent && SamePath(parent, _root);

    /// <summary>"Moving “a.txt” to “Home”…", while the shell works.</summary>
    private static string Doing(IReadOnlyList<string> paths, TransferKind kind, string folder)
    {
        var what = paths.Count == 1 ? $"“{DisplayNameOf(paths[0])}”" : $"{paths.Count} items";
        return kind switch
        {
            TransferKind.Copy => $"Copying {what} to “{Path.GetFileName(folder)}”…",
            TransferKind.Link when paths.Count == 1 => $"Creating a shortcut to {what} in “{Path.GetFileName(folder)}”…",
            TransferKind.Link => $"Creating shortcuts to {what} in “{Path.GetFileName(folder)}”…",
            _ => $"Moving {what} to “{Path.GetFileName(folder)}”…",
        };
    }

    /// <summary>
    /// Why the files would not fit on the disk they go to: those copied, and those moved there from another disk, which
    /// Windows copies before deleting them. What the copies under way take there counts as used, all of it: two copies
    /// that fit one at a time do not start together when together they would not. Null when they fit, and then
    /// <paramref name="turn"/> keeps that room until it ends; null also when Windows cannot say how much room there is.
    /// </summary>
    private async Task<(string Title, string Body)?> NotEnoughRoom(FileWork turn, IReadOnlyList<string> paths, string destinationFolder,
        TransferKind kind)
    {
        var copied = kind == TransferKind.Copy ? paths : paths.Where(path => !ArchiveFolders.IsSameDrive(path, destinationFolder)).ToList();
        if (copied.Count == 0 || DiskOf(destinationFolder) is not { } disk)
            return null;
        // Counted away from the window: a big folder takes a while.
        var needed = _live ? await Task.Run(() => FolderSize.Of(copied)) : FolderSize.Of(copied);
        // Once the size is known: the copies that started meanwhile count too.
        var taken = _fileWork.Where(other => other != turn && other.Room?.Disk == disk.Name).Sum(other => other.Room!.Value.Bytes);
        var left = Math.Max(0, disk.Free - taken);
        if (needed <= left)
        {
            turn.Room = (disk.Name, needed);
            return null;
        }
        Log.Write($"Not enough room on {disk.Name} for {paths.Count} items: {needed} bytes needed, {disk.Free} free, {taken} for the copies under way");
        return ($"Not enough space on {disk.Name}", taken == 0
            ? $"The files need {ByteSize.Format(needed)}, and {ByteSize.Format(disk.Free)} is left. Free up some space, then try again."
            : $"The files need {ByteSize.Format(needed)}, and once the copies under way are counted {ByteSize.Format(left)} is left. " +
              "Try again when they are done, or free up some space.");
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
            Tell(ToastKind.Danger, "Couldn't save what you dropped", e.Message);
        }
        if (created.Count > 0)
        {
            var step = new CopyStep(created);
            Remember(step);
            SelectAfterReload(created);
            Report(Done(created, "saved to", destinationFolder), new StatusNotice(destinationFolder, created), step);
        }
        Reload();
    }

    /// <summary>The folder where something is about to land, made again if it went away meanwhile, as from Explorer.</summary>
    private bool EnsureFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Tell(ToastKind.Danger, $"Couldn't use the folder {folder}", e.Message);
            return false;
        }
    }

    private void CreateLinks(FileWork turn, IReadOnlyList<string> targets, string destinationFolder)
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
                Tell(ToastKind.Danger, $"Couldn't create a shortcut to “{Path.GetFileName(target)}”", e.Message);
            }
        }
        if (links.Count > 0)
        {
            var step = new LinkStep(links);
            Remember(step, turn);
            SelectAfterReload(links);
            Report(links.Count == 1
                    ? $"Shortcut “{DisplayNameOf(links[0])}” created in “{Path.GetFileName(destinationFolder)}”."
                    : $"{links.Count} shortcuts created in “{Path.GetFileName(destinationFolder)}”.",
                new StatusNotice(destinationFolder, links), step);
        }
        Reload();
    }

    /// <summary>"“Offer.pdf” moved to “Documents”.", or "3 items moved to “Documents”.".</summary>
    /// <param name="verb">With its preposition: "moved to".</param>
    private static string Done(IReadOnlyList<string> paths, string verb, string folder) =>
        paths.Count == 1
            ? $"“{DisplayNameOf(paths[0])}” {verb} “{Path.GetFileName(folder)}”."
            : $"{paths.Count} items {verb} “{Path.GetFileName(folder)}”.";

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

        var destination = parent?.Topic.FullPath ?? _root;
        using var turn = await BeginFileWork($"Moving “{topic.Name}” to “{parent?.Name ?? ArchiveName}”…",
            FileTouches.Transfer([topic.Topic.FullPath], destination));
        var previousOrder = LoadMetadata().OrderOf(topic.Topic.RelativePath);
        var newPath = Path.Combine(destination, topic.Name);
        // Two topics with the same name would be merged into one: not something a drag should do.
        if (Path.Exists(newPath))
        {
            Tell(ToastKind.Warning, $"“{parent?.Name ?? ArchiveName}” already has a “{topic.Name}”: rename one of them, then try again.");
            return;
        }
        var (done, step) = await TransferNow(turn, [topic.Topic.FullPath], destination, TransferKind.Move, reload: false,
            foldersAsRows: false);
        if (!done || !Directory.Exists(newPath))
        {
            Reload();
            return;
        }
        // Ctrl+Z puts it back where it was among the topics it left, not in the place it takes here. Its own step: one of
        // another operation may have ended meanwhile.
        if (step is MoveStep move)
        {
            var topicMove = new TopicMoveStep(move, previousOrder);
            _history.Replace(move, topicMove);
            // The toast of the move takes back all of it.
            foreach (var toast in Toasts.Where(toast => ReferenceEquals(toast.UndoStep, move)))
                toast.UndoStep = topicMove;
            NotifyHistoryChanged();
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
        var key = tile.MetadataKey;
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
    /// «Sort by»: the items of the tile by name, date, type or size. A new key starts in its usual direction; the order
    /// given by hand goes.
    /// </summary>
    public void SortTile(TileViewModel tile, SortKey key)
    {
        var metadataKey = tile.MetadataKey;
        var sort = tile.SortedBy == key ? new ItemSort(key, tile.IsDescending) : ItemSort.For(key);
        Arrange(tile, metadataKey, m => m.SetSort(metadataKey, sort));
    }

    /// <summary>«Ascending» or «Descending», for the sort in use, by name when the items were in order by hand.</summary>
    public void SetSortDirection(TileViewModel tile, bool descending)
    {
        var metadataKey = tile.MetadataKey;
        var sort = new ItemSort(tile.SortedBy ?? SortKey.Name, descending);
        Arrange(tile, metadataKey, m => m.SetSort(metadataKey, sort));
    }

    /// <summary>
    /// Items dragged to another place in their tile, before <paramref name="before"/> or at the end: the tile keeps that
    /// order from now on, and what arrives later goes after them.
    /// </summary>
    public void MoveItems(TileViewModel tile, IReadOnlyList<ItemViewModel> items, ItemViewModel? before)
    {
        if (items.Count == 0 || (before is not null && items.Contains(before)))
            return;
        var key = tile.MetadataKey;
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

    // ---- Rows of «General» ----

    /// <summary>«Unfold as tile»: a folder shown as a row of «General» gets a tile of its own, its folder where it is.</summary>
    public void PromoteItem(ItemViewModel item)
    {
        if (!item.CanBecomeTile)
            return;
        var key = KeyOf(item.Item.FullPath);
        ChangeMetadata(metadata => metadata.SetItem(key, false), new ItemStep(key, item.Name, WasItem: true));
    }

    /// <summary>«Fold into General»: a tile becomes a row of «General», its folder where it is; the row is selected, to be seen.</summary>
    public void DemoteTile(TileViewModel tile)
    {
        if (tile.RelativePath is not { } key)
            return;
        SelectAfterReload([tile.FolderPath]);
        ChangeMetadata(metadata => metadata.SetItem(key, true), new ItemStep(key, tile.Title, WasItem: false));
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
    /// «Color» of a topic: one of the palette, none on purpose (<see cref="TopicColors.None"/>), or with null the automatic
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
    /// Takes back the last thing done in Inlay, as Ctrl+Z does in Explorer. During moves or copies it waits for all of
    /// them to end, and then takes back the one asked for last, even if another ended after it; the operations asked
    /// meanwhile wait for it.
    /// </summary>
    public Task Undo() => Undo(only: null);

    /// <param name="only">The step a toast offers to undo: that one, even if others were recorded since.</param>
    private async Task Undo(UndoStep? only)
    {
        CommitAllEdits();
        using var turn = await BeginFileWork("Undoing the last operation…", touches: null);
        var step = only ?? _history.Last;
        if (step is null || _history.OrderOf(step) is not { } stepOrder)
        {
            Tell(ToastKind.Info, only is null ? "There is nothing to undo." : "This operation can no longer be undone.");
            return;
        }
        _history.Remove(step);
        // Put back in its place if the undo fails, as if never taken.
        _undoneOrder = stepOrder;
        // Its toast offered «Undo»: now it is done, and what the undo did takes its place.
        foreach (var toast in Toasts.Where(toast => ReferenceEquals(toast.UndoStep, step)).ToList())
            Toasts.Remove(toast);
        NotifyHistoryChanged();
        var outcome = step switch
        {
            MoveStep move => await UndoMove(turn, move),
            TopicMoveStep topicMove => await UndoTopicMove(turn, topicMove),
            CopyStep copy => await RecycleCreated(copy, copy.Copies, "Copy undone", "the copies", "copies"),
            LinkStep link => await RecycleCreated(link, link.Links, "Shortcut undone", "the shortcuts", "shortcuts"),
            RenameStep rename => UndoRename(rename),
            CreateStep create => UndoCreate(create),
            RecycleStep recycle => UndoRecycle(recycle),
            TileOrderStep order => UndoOrder(order.PreviousOrder, "The tiles are back in the order they had."),
            TopicOrderStep order => UndoOrder(order.PreviousOrder, "The spaces are back in the order they had."),
            TopicStep topic => UndoTopic(topic),
            ItemStep item => UndoItem(item),
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
            return Outcome.Refused($"“{step.Name}” is gone.");
        if (!UpdateMetadata(metadata => metadata.SetColor(step.Key, step.Previous, step.WasAutomatic)))
            return null;
        return Outcome.Done(step.Previous is null ? $"“{step.Name}” has no color again." : $"“{step.Name}” has its old color again.");
    }

    /// <summary>The undo did not happen, most likely because a file is open in another program: Ctrl+Z can try again.</summary>
    private void KeepForRetry(UndoStep step) => Remember(step, _undoneOrder);

    private async Task<Outcome?> UndoMove(FileWork turn, MoveStep step)
    {
        // Only what is still where it went goes back, and only to a place that is still free.
        var items = step.Items.Where(item => Path.Exists(item.To) && !Path.Exists(item.From)).ToList();
        if (items.Count == 0)
            return Outcome.Refused("The move can't be undone: the items have been moved or renamed since.");
        turn.Say(items.Count == 1
            ? $"Putting “{DisplayNameOf(items[0].To)}” back…"
            : $"Putting {items.Count} items back…");
        var owner = OwnerWindow;
        await RunShell(() => ShellFileOperations.MoveEach(
            items.Select(item => item.To).ToList(), items.Select(item => item.From).ToList(), owner));
        var back = items.Where(item => Path.Exists(item.From) && !Path.Exists(item.To)).ToList();
        if (back.Count == 0)
        {
            KeepForRetry(step);
            return Outcome.Failed("The move wasn't undone: an item may be open in another program. Try again with Ctrl+Z.");
        }
        FollowBack(back, step.Forgotten);
        SelectAfterReload(back.Select(item => item.From));
        var done = back.Count == 1
            ? $"Move undone: “{DisplayNameOf(back[0].From)}” is back in “{FolderName(back[0].From)}”."
            : $"Move undone: {back.Count} items are back where they were.";
        return Outcome.Done(done + LeftAlone(step.Items.Count - back.Count));
    }

    private async Task<Outcome?> UndoTopicMove(FileWork turn, TopicMoveStep step)
    {
        var outcome = await UndoMove(turn, step.Move);
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
                {
                    changed |= metadata.MoveFolder(KeyOf(item.To), KeyOf(item.From));
                    // A row of «General» only since it arrived: back where it was, it is what it was there.
                    if (item.BecameRow)
                        changed |= metadata.SetItem(KeyOf(item.From), false);
                }
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

    /// <summary>Undoes copies and shortcuts made by Inlay: they go to the Recycle Bin, never deleted for good.</summary>
    /// <param name="them">With the article, as in "the copies".</param>
    /// <param name="many">As in "3 copies are in the Recycle Bin".</param>
    private async Task<Outcome?> RecycleCreated(UndoStep step, IReadOnlyList<string> created, string undone, string them, string many)
    {
        var existing = created.Where(Path.Exists).ToList();
        if (existing.Count == 0)
            return Outcome.Refused($"There is nothing to undo: {them} are gone.");
        // Without a Recycle Bin, deleting is for good, changes made since included: not something an undo may do.
        if (!existing.All(RecycleBin.IsAvailableFor))
            return Outcome.Refused($"Not undone: this drive has no Recycle Bin, so nothing could be restored afterwards. If you don't need {them}, press Del.");
        var folders = existing.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        var owner = OwnerWindow;
        await RunShell(() => ShellFileOperations.Recycle(existing, owner));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        var removed = existing.Where(path => !Path.Exists(path)).ToList();
        if (removed.Count == 0)
        {
            KeepForRetry(step);
            return Outcome.Failed($"The undo didn't work: {them} are still there. Try again with Ctrl+Z.");
        }
        var done = removed.Count == 1
            ? $"{undone}: “{DisplayNameOf(removed[0])}” is in the Recycle Bin."
            : $"{undone}: {removed.Count} {many} are in the Recycle Bin.";
        return Outcome.Done(done + LeftAlone(created.Count - removed.Count));
    }

    private Outcome? UndoRename(RenameStep step)
    {
        var newName = DisplayNameOf(step.To);
        if (!Path.Exists(step.To))
            return Outcome.Refused($"The rename can't be undone: “{newName}” is gone.");
        var onlyTheCaseChanged = string.Equals(step.From, step.To, StringComparison.OrdinalIgnoreCase);
        if (!onlyTheCaseChanged && Path.Exists(step.From))
            return Outcome.Refused($"The rename can't be undone: the folder already has a “{DisplayNameOf(step.From)}”.");
        if (!RenameNow(step.To, step.From))
        {
            KeepForRetry(step);
            return Outcome.Failed($"Couldn't rename “{newName}”: it may be open in another program. Try again with Ctrl+Z.");
        }
        // The metadata follows the folder back, and so does the selection of a topic.
        KeepPlace(step.To, step.From);
        if (IsInsideArchive(step.From))
            FollowFolder(KeyOf(step.To), KeyOf(step.From));
        SelectAfterReload([step.From]);
        return Outcome.Done($"Rename undone: it is called “{DisplayNameOf(step.From)}” again.");
    }

    private Outcome? UndoCreate(CreateStep step)
    {
        var name = Path.GetFileName(step.Folder);
        try
        {
            if (!Directory.Exists(step.Folder))
                return Outcome.Refused($"“{name}” is gone.");
            // Only an empty folder goes away for good: anything put in it since stays.
            if (Directory.EnumerateFileSystemEntries(step.Folder).Any())
                return Outcome.Refused($"“{name}” is no longer empty, so it stays. If you don't need it, move it to the Recycle Bin with Del.");
            Directory.Delete(step.Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            KeepForRetry(step);
            return Outcome.Failed($"Couldn't remove “{name}”: {e.Message}");
        }
        // What it got meanwhile, like the mark of a topic, goes with it.
        if (IsInsideArchive(step.Folder))
            ForgetFolder(KeyOf(step.Folder));
        return Outcome.Done($"Creation of “{name}” undone.");
    }

    private Outcome? UndoRecycle(RecycleStep step)
    {
        if (step.Items.Count == 0)
            return Outcome.Refused("It can't be restored: Windows deleted it for good, without going through the Recycle Bin.");
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
                ? Outcome.Failed("The restore didn't work: a folder may be open in another program. Try again with Ctrl+Z.")
                : Outcome.Refused("It is no longer in the Recycle Bin, or another item already took its place.");
        }
        ShellNotify.RecycleBinChanged();
        RestoreMetadata(step.Forgotten.Restrict(ArchiveKeys(restored.Select(item => item.OriginalPath))));
        SelectAfterReload(restored.Select(item => item.OriginalPath));
        var done = restored.Count == 1
            ? $"“{DisplayNameOf(restored[0].OriginalPath)}” is back from the Recycle Bin in “{FolderName(restored[0].OriginalPath)}”."
            : $"{restored.Count} items are back from the Recycle Bin.";
        var lost = step.NotInBin switch
        {
            0 => "",
            1 => " One item had been deleted for good.",
            var count => $" {count} items had been deleted for good.",
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
            return Outcome.Refused($"“{step.Name}” is gone.");
        if (!UpdateMetadata(metadata => metadata.SetArrangement(step.Key, step.Sort, step.Items)))
            return null;
        return Outcome.Done($"“{step.Name}” is back in the order it had.");
    }

    private Outcome? UndoSize(SizeStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"“{step.Name}” is gone.");
        if (!UpdateMetadata(metadata => metadata.SetSize(step.Key, step.Previous)))
            return null;
        return Outcome.Done($"“{step.Name}” is back to the size it had.");
    }

    private Outcome? UndoTopic(TopicStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"“{step.Name}” is gone.");
        if (!UpdateMetadata(metadata => metadata.SetTopic(step.Key, step.WasTopic)))
            return null;
        return Outcome.Done(step.WasTopic ? $"“{step.Name}” is a space again." : $"“{step.Name}” is a tile again.");
    }

    private Outcome? UndoItem(ItemStep step)
    {
        if (!Directory.Exists(Path.Combine(_root, step.Key)))
            return Outcome.Refused($"“{step.Name}” is gone.");
        if (!UpdateMetadata(metadata => metadata.SetItem(step.Key, step.WasItem)))
            return null;
        return Outcome.Done(step.WasItem ? $"“{step.Name}” is back in “General”." : $"“{step.Name}” is a tile again.");
    }

    /// <summary>For the items an undo could not put back: something else took their place, or they are gone.</summary>
    private static string LeftAlone(int count) => count switch
    {
        0 => "",
        1 => " One item changed meanwhile and was left as it is.",
        _ => $" {count} items changed meanwhile and were left as they are.",
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
            ? "This space is empty. Drop files here, or create a tile with Ctrl+Shift+N."
            : "";
        OnPropertyChanged(nameof(BoardFolder));
        OnPropertyChanged(nameof(SelectedTopic));
    }

    partial void OnQueryChanged(string value) => RunSearch();

    /// <summary>Reads topics and the current board again, keeping the selection if that topic still exists.</summary>
    private void Reload()
    {
        _reloadPending = false;
        var metadata = LoadMetadata();
        _reader = new ArchiveReader(_root, metadata);
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
        RootFilesText = _reader.ReadRootFiles().Count switch
        {
            0 => null,
            1 => "1 loose file in the vault",
            var count => string.Create(English, $"{count:N0} loose files in the vault"),
        };

        if ((FindTopicOrParent(_selectedTopicPath) ?? Topics.FirstOrDefault()) is { } shown)
            SelectTopic(shown);
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
    /// Reads the board of every topic for the canvas: the zones, tiles and items still there keep their view models, with
    /// their selection and the names being typed.
    /// </summary>
    private void ReadCanvas() => ReconcileZones(Canvas.Zones, Topics.Select(ZoneModelOf).ToList());

    private ZoneModel ZoneModelOf(TopicViewModel topic) =>
        new(topic.Topic.RelativePath, topic.Name, topic, _reader.ReadBoard(topic.Topic).Tiles, topic.Children.Select(ZoneModelOf).ToList());

    private void ReconcileZones(ObservableCollection<ZoneViewModel> zones, IReadOnlyList<ZoneModel> models, bool nested = false) =>
        Reconciler.Reconcile(zones, models, model => model.Key, zone => zone.Key,
            model => FillZone(new ZoneViewModel(model.Key, model.Title, model.Topic) { IsNested = nested }, model),
            (zone, model) => FillZone(zone, model));

    private ZoneViewModel FillZone(ZoneViewModel zone, ZoneModel model)
    {
        Reconciler.Reconcile(zone.Tiles, model.Tiles, tile => TileViewModel.KeyOf(tile.FolderPath, tile.IsGeneral), tile => tile.Key,
            tile => new TileViewModel(tile, _root, _cutPaths), (viewModel, tile) => viewModel.Update(tile));
        ReconcileZones(zone.Children, model.Children, nested: true);
        zone.Update(model.Title, model.Topic);
        return zone;
    }

    /// <summary>A zone as read from the archive: its topic, its tiles and the zones inside it.</summary>
    private sealed record ZoneModel(string Key, string Title, TopicViewModel Topic, IReadOnlyList<Tile> Tiles, IReadOnlyList<ZoneModel> Children);

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
        BoardTitle = "Empty vault";
        BoardPath = "";
        SubTopics.Clear();
        Tiles.Clear();
        EmptyMessage = "Create your first space with the + button next to “Spaces”.";
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
            Tell(ToastKind.Danger, $"Couldn't create the folder “{name}”", e.Message);
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

    /// <summary>A step done here and now: the newest of all.</summary>
    private void Remember(UndoStep step) => Remember(step, ++_asked);

    /// <summary>
    /// A step of a file operation, in the place of when the operation was asked for: a long copy that ends after a move
    /// asked later stays before it, and Ctrl+Z takes back the move first.
    /// </summary>
    private void Remember(UndoStep step, FileWork work) => Remember(step, work.Order);

    private void Remember(UndoStep step, long order)
    {
        _history.Record(step, order);
        NotifyHistoryChanged();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(UndoLabel));
        // «Undo» on a toast stays only while its operation is the one Ctrl+Z would undo.
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

    /// <summary>The names in a folder, to tell afterwards which ones a copy added; null when they cannot be read.</summary>
    private static HashSet<string>? EntryNames(string folder)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(folder).Select(path => Path.GetFileName(path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An empty list would make everything in the folder look new, and Ctrl+Z would take it all to the Recycle Bin.
            Log.Write($"Could not read the names in {folder}: {e.Message}");
            return null;
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
            Tell(ToastKind.Danger, "Couldn't save the vault settings", e.Message);
            return false;
        }
    }

    /// <summary>
    /// Waits for the file operations before it that touch the same files, running or waiting, then holds its turn until
    /// disposed: it starts from the files as they left them, while the operations on other files go on alongside.
    /// </summary>
    /// <param name="activity">What the status bar says once the turn comes, next to its LEDs.</param>
    /// <param name="touches">
    /// What it changes (see <see cref="FileTouches"/>); null for everything, as Ctrl+Z: it waits for every operation
    /// before it, and those after it wait for it, so that it takes back the last thing done.
    /// </param>
    private async Task<FileWork> BeginFileWork(string activity, FileTouches? touches)
    {
        var work = new FileWork(this, activity, touches, ++_asked);
        _fileWork.Add(work);
        if (_fileWork.Count == 1)
            OnPropertyChanged(nameof(IsBusy));
        StartWhatCan();
        await work.Turn;
        return work;
    }

    /// <summary>Starts each operation that no operation before it, running or waiting, gets in the way of.</summary>
    private void StartWhatCan()
    {
        for (var i = 0; i < _fileWork.Count; i++)
        {
            var work = _fileWork[i];
            if (work.IsRunning || _fileWork.Take(i).Any(before => before.Overlaps(work)))
                continue;
            work.Start();
            Status = work.Activity;
        }
        ShowFileWork();
    }

    private void EndFileWork(FileWork work)
    {
        if (!_fileWork.Remove(work))
            return;
        FileOperationCompleted?.Invoke(this, EventArgs.Empty);
        if (_fileWork.Count > 0)
        {
            StartWhatCan();
            return;
        }
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

    /// <summary>
    /// The operations under way next to the LEDs of the status bar: the last one started, and how many more run or wait.
    /// The tooltip lists them all.
    /// </summary>
    private void ShowFileWork()
    {
        var running = _fileWork.Where(work => work.IsRunning).ToList();
        if (running.Count == 0)
            return;
        var waiting = _fileWork.Count - running.Count;
        var activity = running[^1].Activity + (running.Count - 1) switch
        {
            0 => "",
            var others => string.Create(English, $" · {others:N0} more running"),
        };
        Activity = waiting > 0 ? string.Create(English, $"{activity} · {waiting:N0} waiting") : activity;
        ActivityDetails = string.Join(Environment.NewLine,
            _fileWork.Select(work => work.IsRunning ? work.Activity : $"Waiting: {work.Activity}"));
    }

    /// <summary>A file operation that asked for its turn: what it touches, what the status bar says of it, the room it takes.</summary>
    /// <param name="order">When it was asked for, among the operations and the other steps: that of its undo step.</param>
    private sealed class FileWork(MainViewModel owner, string activity, FileTouches? touches, long order) : IDisposable
    {
        private readonly TaskCompletionSource _turn = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _ended;

        public string Activity { get; private set; } = activity;

        public long Order { get; } = order;

        /// <summary>Null for everything, as Ctrl+Z.</summary>
        public FileTouches? Touches { get; } = touches;

        /// <summary>The room its copy takes on a disk while it runs: a copy that starts meanwhile counts it as used.</summary>
        public (string Disk, long Bytes)? Room { get; set; }

        public Task Turn => _turn.Task;

        public bool IsRunning => _turn.Task.IsCompleted;

        public void Start() => _turn.SetResult();

        /// <summary>Two operations get in each other's way when one of them touches everything, or when what they touch overlaps.</summary>
        public bool Overlaps(FileWork other) => Touches is null || other.Touches is null || Touches.Overlaps(other.Touches);

        /// <summary>What it is doing now, as "Putting 3 items back…".</summary>
        public void Say(string activity)
        {
            Activity = activity;
            owner.Status = activity;
            owner.ShowFileWork();
        }

        public void Dispose()
        {
            if (_ended)
                return;
            _ended = true;
            owner.EndFileWork(this);
        }
    }

    /// <summary>An operation of the shell: on its own thread in the app, here and now in previews and tests.</summary>
    private Task<bool> RunShell(Func<bool> operation) => ShellRunner(operation);

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

    /// <summary>What an operation just did: «Undo» takes back <paramref name="step"/>, «Show» goes where the items went.</summary>
    private void Report(string message, StatusNotice notice, UndoStep? step, ToastKind kind = ToastKind.Success) =>
        Tell(new ToastViewModel(kind, message, notice: notice.ShowFolder is null ? null : notice, undoStep: step)
        {
            CanUndo = step is not null && ReferenceEquals(step, _history.Last),
        });

    /// <summary>«Show» on a toast.</summary>
    public void ShowFrom(ToastViewModel toast)
    {
        if (toast.Notice is { } notice)
            ShowNotice(notice);
    }

    /// <summary>«Undo» on a toast: its own operation, even if another one ended after it meanwhile.</summary>
    public Task UndoFrom(ToastViewModel toast) => toast is { CanUndo: true, UndoStep: { } step } ? Undo(step) : Task.CompletedTask;

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
        var count = Count(selected.Count, "item", "items") + " selected";
        SelectionSummary = bytes > 0 ? $"{count} · {ByteSize.Format(bytes)}" : count;
    }

    private void RunSearch()
    {
        Results.Clear();
        var query = SearchQuery.Parse(Query);
        // The filters above the results say what the search box has, written there or chosen with them.
        foreach (var filter in KindFilters)
            filter.IsChecked = query.Kinds.Contains(filter.Kind);
        HasPlaceFilter = query.Places.Count > 0;
        PlaceFilterText = HasPlaceFilter ? "In: " + string.Join(", ", query.Places.Select(SearchQuery.Display)) : Everywhere;
        if (!IsSearching)
            return;
        if (_index is null)
        {
            ResultsHeader = "Indexing…";
            return;
        }
        var hits = _index.Search(query, MaxResults);
        foreach (var hit in hits)
            Results.Add(new SearchResultViewModel(hit) { IsCut = _cutPaths.Contains(hit.Item.FullPath) });
        ResultsHeader = (query.Words.Count, hits.Count) switch
        {
            // Only filters: what passes them, the newest first.
            (0, 0) => "Nothing matches these filters.",
            (0, 1) => "1 item",
            (0, MaxResults) => $"The {MaxResults} most recent",
            (0, var count) => $"{count} items, most recent first",
            (_, 0) => query.HasFilters
                ? "No results. Try part of the name, or remove a filter."
                : "No results. Try part of the name, or the name of the folder.",
            (_, 1) => "1 result",
            (_, MaxResults) => $"First {MaxResults} results",
            (_, var count) => $"{count} results",
        };
    }

    /// <summary>A filter above the results, or in the menu of the search box: adds or takes away its "type:…".</summary>
    public void ToggleKindFilter(SearchKind kind) => Query = SearchQuery.ToggleKind(Query, kind);

    /// <summary>
    /// Only in this topic, or everywhere with null: "in:…" in the search box, in the place of those there. From the root
    /// ("in:/Work"): a folder with the same name deeper down is another place.
    /// </summary>
    public void SetSearchPlace(TopicViewModel? topic) => Query = SearchQuery.WithPlace(Query, topic is null ? null : PlaceOf(topic));

    /// <summary>Whether the search looks only in this topic, or everywhere for null: the menu of the places checks it.</summary>
    public bool IsSearchPlace(TopicViewModel? topic)
    {
        var places = SearchQuery.Parse(Query).Places;
        return topic is null ? places.Count == 0 : places.Any(place => SearchQuery.SamePlace(place, PlaceOf(topic)));
    }

    private static string PlaceOf(TopicViewModel topic) => "/" + topic.Topic.RelativePath;

    private async void RebuildIndex()
    {
        _indexing?.Cancel();
        var indexing = _indexing = new CancellationTokenSource();
        var root = _root;
        try
        {
            var index = await Task.Run(() => SearchIndex.Build(root, indexing.Token), indexing.Token);
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
            Tell(ToastKind.Danger, "Search is not available", e.Message);
        }
    }

    private void StartWatching()
    {
        try
        {
            _watcher = new ArchiveWatcher(_root, TimeSpan.FromMilliseconds(400));
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
            : $"{Count(_index.ArchiveCount, "item", "items")} · {ByteSize.Format(_index.ArchiveBytes)}";
        UpdateDisk();
    }

    /// <summary>The free space of the disk of the archive: at every reload, after each operation and each time the panel opens.</summary>
    private void UpdateDisk()
    {
        var disk = DiskOf(_root);
        IsDiskLow = disk?.IsLow ?? false;
        DiskText = disk is null ? null : disk.IsLow ? $"Low on space: {disk.FreeText}" : disk.FreeText;
        DiskToolTip = disk is null
            ? null
            : string.Join(Environment.NewLine, new[]
            {
                disk.Description,
                _index is null ? null : $"The vault takes {ByteSize.Format(_index.ArchiveBytes)}.",
                disk.IsLow ? "Space is running low: the biggest copies won't fit." : null,
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
            Tell(ToastKind.Danger, $"Couldn't open “{name}”", e.Message);
            return false;
        }
    }

    private static string Count(int count, string one, string many) => count == 1 ? $"1 {one}" : string.Create(English, $"{count:N0} {many}");
}
