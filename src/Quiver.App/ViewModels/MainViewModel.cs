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

/// <summary>State of the panel: topic tree, current board, search, and the actions on the archive.</summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxResults = 60;

    private readonly bool _watchForChanges;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private string _root;
    private ArchiveReader _reader;
    private ArchiveWatcher? _watcher;
    private SearchIndex? _index;
    private CancellationTokenSource? _indexing;
    private TopicViewModel? _selectedTopic;
    private string? _selectedTopicPath;
    private bool _isPanelVisible;
    private bool _reloadPending;
    private TimeSpan? _lastOpenTime;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(IsBoardVisible))]
    private string _query = "";

    [ObservableProperty]
    private string _breadcrumb = "";

    [ObservableProperty]
    private string _emptyMessage = "";

    [ObservableProperty]
    private string _resultsHeader = "";

    [ObservableProperty]
    private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInboxItems))]
    private int _inboxCount;

    [ObservableProperty]
    private bool _isInboxShown;

    [ObservableProperty]
    private PromptViewModel? _prompt;

    public MainViewModel(string root, bool watchForChanges = true)
    {
        _watchForChanges = watchForChanges;
        LoadArchive(root);
    }

    /// <summary>Raised after something was opened, so the panel can get out of the way.</summary>
    public event EventHandler? ItemOpened;

    /// <summary>
    /// Raised when a file operation ends. The shell's own windows take the focus during the operation
    /// and do not give it back, so the panel has to take it again.
    /// </summary>
    public event EventHandler? FileOperationCompleted;

    public ObservableCollection<TopicViewModel> Topics { get; } = [];

    public ObservableCollection<TopicViewModel> SubTopics { get; } = [];

    public ObservableCollection<TileViewModel> Tiles { get; } = [];

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    public string Root => _root;

    public string InboxPath => _reader.InboxPath;

    public TopicViewModel? SelectedTopic => _selectedTopic;

    public bool IsSearching => !string.IsNullOrWhiteSpace(Query);

    public bool IsBoardVisible => !IsSearching;

    public bool HasInboxItems => InboxCount > 0;

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
        _reader = new ArchiveReader(_root, new ArchiveMetadata());
        _index = null;
        _selectedTopic = null;
        _selectedTopicPath = null;
        IsInboxShown = false;
        Reload();
        RebuildIndex();
        if (_watchForChanges)
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
        // Without a watcher (unsupported drive) there is no other way to notice changes.
        if (_reloadPending || _watcher is null)
            Reload();
    }

    public void OnPanelHidden()
    {
        _isPanelVisible = false;
        Prompt = null;
    }

    public void ReportOpenTime(TimeSpan elapsed)
    {
        _lastOpenTime = elapsed;
        Log.Write($"Panel shown in {elapsed.TotalMilliseconds:N0} ms");
        UpdateStatus();
    }

    /// <summary>Builds the search index synchronously, for preview rendering.</summary>
    public void BuildIndexNow()
    {
        _index = SearchIndex.Build(_root);
        UpdateStatus();
    }

    public void Open(ArchiveItem item) => RunShell(() => ShellLauncher.Open(item.FullPath), item.DisplayName);

    public void Reveal(ArchiveItem item) => RunShell(() => ShellLauncher.Reveal(item.FullPath), item.DisplayName);

    public void OpenFolder(string path) => RunShell(() => ShellLauncher.Open(path), Path.GetFileName(path));

    public void CopyPath(ArchiveItem item)
    {
        try
        {
            Clipboard.SetText(item.FullPath);
            Status = $"Percorso copiato: {item.FullPath}";
        }
        catch (ExternalException e)
        {
            Status = $"Impossibile copiare il percorso: {e.Message}";
        }
    }

    public void PromoteTile(TileViewModel tile)
    {
        if (tile.RelativePath is { } path && UpdateMetadata(metadata => metadata.SetTopic(path, true)))
            Reload();
    }

    public void DemoteTopic(TopicViewModel topic)
    {
        if (!topic.CanDemote)
            return;
        // A selection inside the demoted topic falls back to its parent.
        if (_selectedTopicPath is { } selected && IsKeyAtOrBelow(selected, topic.Topic.RelativePath))
            _selectedTopicPath = topic.Parent.Topic.RelativePath;
        if (UpdateMetadata(metadata => metadata.SetTopic(topic.Topic.RelativePath, false)))
            Reload();
    }

    /// <param name="parent">Null for a topic in the root of the archive.</param>
    public void AskNewTopic(TopicViewModel? parent)
    {
        var folder = parent?.Topic.FullPath ?? _root;
        var title = parent is null ? "Nuovo argomento" : $"Nuovo argomento dentro «{parent.Name}»";
        ShowPrompt(new PromptViewModel(title, "Crea", name => CreateFolder(folder, name, asTopic: true), initialText: ""));
    }

    /// <param name="topic">Null for the topic on the board.</param>
    public void AskNewTile(TopicViewModel? topic)
    {
        topic ??= _selectedTopic;
        if (topic is null)
        {
            Status = "Per creare un riquadro, scegli prima un argomento.";
            return;
        }
        var folder = topic.Topic.FullPath;
        ShowPrompt(new PromptViewModel($"Nuovo riquadro in «{topic.Name}»", "Crea",
            name => CreateFolder(folder, name, asTopic: false), initialText: ""));
    }

    public void AskRename(string path)
    {
        var isFolder = Directory.Exists(path);
        var name = Path.GetFileName(path);
        // Like Explorer, the extension starts out of the selection.
        var selected = isFolder || !Path.HasExtension(name) ? name.Length : Path.GetFileNameWithoutExtension(name).Length;
        ShowPrompt(new PromptViewModel("Rinomina", "Rinomina", newName => Rename(path, newName, isFolder),
            initialText: name, selectionLength: selected));
    }

    /// <summary>Files go straight to the Recycle Bin; folders, with everything in them, after a confirmation.</summary>
    public void AskRecycle(string path)
    {
        if (!Directory.Exists(path))
        {
            Recycle([path]);
            return;
        }
        ShowPrompt(new PromptViewModel("Sposta nel Cestino", "Sposta nel Cestino",
            _ => Recycle([path]) ? null : "Lo spostamento nel Cestino non è riuscito.",
            message: $"La cartella «{Path.GetFileName(path)}» andrà nel Cestino con tutto il suo contenuto."));
    }

    public bool Recycle(IReadOnlyList<string> paths)
    {
        var folders = paths.Where(Directory.Exists).Where(IsInsideArchive).ToList();
        var done = RunFileOperation(() => ShellFileOperations.Recycle(paths, OwnerWindow));
        foreach (var folder in folders.Where(folder => !Directory.Exists(folder)))
            ForgetFolder(KeyOf(folder));
        Reload();
        return done;
    }

    /// <summary>Moves or copies files dropped onto a folder of the archive.</summary>
    public void Transfer(IReadOnlyList<string> paths, string destinationFolder, TransferKind kind)
    {
        if (kind == TransferKind.None)
            return;
        try
        {
            // The inbox folder only exists once something lands in it.
            Directory.CreateDirectory(destinationFolder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Status = $"Impossibile usare la cartella {destinationFolder}: {e.Message}";
            return;
        }

        List<string> movedFolders = kind == TransferKind.Move
            ? paths.Where(Directory.Exists).Where(IsInsideArchive).ToList()
            : [];
        var done = RunFileOperation(() => kind == TransferKind.Copy
            ? ShellFileOperations.Copy(paths, destinationFolder, OwnerWindow)
            : ShellFileOperations.Move(paths, destinationFolder, OwnerWindow));
        // Folders keep their metadata (topic, colors…) at the new place.
        foreach (var folder in movedFolders.Where(folder => !Directory.Exists(folder)))
        {
            var newPath = Path.Combine(destinationFolder, Path.GetFileName(folder));
            if (IsInsideArchive(newPath))
                FollowFolder(KeyOf(folder), KeyOf(newPath));
            else
                ForgetFolder(KeyOf(folder));
        }
        if (!done)
            Status = kind == TransferKind.Copy ? "La copia non è stata completata." : "Lo spostamento non è stato completato.";
        Reload();
    }

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
        Breadcrumb = string.Join(" › ", topic.PathNames());
        Replace(SubTopics, topic.Children);
        Replace(Tiles, board.Tiles.Select(tile => new TileViewModel(tile, _root)));
        EmptyMessage = board.Tiles.Count == 0 && topic.Children.Count == 0
            ? "Questo argomento è vuoto: crea un riquadro (Ctrl+Maiusc+N) o trascina qui dei file."
            : "";
    }

    [RelayCommand]
    private void ShowInbox()
    {
        IsInboxShown = true;
        if (_selectedTopic is not null)
            _selectedTopic.IsSelected = false;
        _selectedTopic = null;

        var items = _reader.ReadInbox();
        InboxCount = items.Count;
        Breadcrumb = "Da smistare";
        SubTopics.Clear();
        Tiles.Clear();
        if (items.Count > 0)
            Tiles.Add(new TileViewModel("Da smistare", _reader.InboxPath, items));
        EmptyMessage = items.Count == 0
            ? $"Niente da smistare. Qui compaiono i file lasciati nella radice dell'archivio o nella cartella «{ArchiveLayout.InboxFolderName}»."
            : "";
    }

    partial void OnQueryChanged(string value) => RunSearch();

    /// <summary>Reads topics and the current board again, keeping the selection if that topic still exists.</summary>
    private void Reload()
    {
        _reloadPending = false;
        _reader = new ArchiveReader(_root, LoadMetadata());

        var collapsed = Topics
            .SelectMany(topic => topic.SelfAndDescendants())
            .Where(topic => !topic.IsExpanded)
            .Select(topic => topic.Topic.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedTopic = null;
        Replace(Topics, _reader.ReadTopics().Select(topic => new TopicViewModel(topic, parent: null, collapsed)));
        InboxCount = _reader.ReadInbox().Count;

        if (IsInboxShown)
            ShowInbox();
        else if ((FindTopic(_selectedTopicPath) ?? Topics.FirstOrDefault()) is { } topic)
            SelectTopic(topic);
        else
            ShowEmptyArchive();
    }

    private void ShowEmptyArchive()
    {
        Breadcrumb = "Archivio vuoto";
        SubTopics.Clear();
        Tiles.Clear();
        EmptyMessage = "Crea il primo argomento con «Nuovo argomento», qui a sinistra.";
    }

    private TopicViewModel? FindTopic(string? relativePath) =>
        relativePath is null
            ? null
            : Topics.SelectMany(topic => topic.SelfAndDescendants())
                .FirstOrDefault(topic => string.Equals(topic.Topic.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    private string? CreateFolder(string parentFolder, string name, bool asTopic)
    {
        name = name.Trim();
        if (ArchiveNames.Validate(name) is { } problem)
            return problem;
        var inRoot = string.Equals(Path.GetFullPath(parentFolder), _root, StringComparison.OrdinalIgnoreCase);
        if (inRoot && name.Equals(ArchiveLayout.InboxFolderName, StringComparison.OrdinalIgnoreCase))
            return "Questo nome è riservato ai file da smistare.";
        var path = Path.Combine(parentFolder, name);
        if (Path.Exists(path))
            return $"«{name}» esiste già.";
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Impossibile creare la cartella: {e.Message}";
        }

        if (asTopic)
        {
            // Folders in the root are topics anyway; deeper ones have to be marked.
            if (!inRoot)
                UpdateMetadata(metadata => metadata.SetTopic(KeyOf(path), true));
            _selectedTopicPath = KeyOf(path);
            IsInboxShown = false;
        }
        Reload();
        return null;
    }

    private string? Rename(string path, string newName, bool isFolder)
    {
        newName = newName.Trim();
        var oldName = Path.GetFileName(path);
        if (newName == oldName)
            return null;
        if (ArchiveNames.Validate(newName) is { } problem)
            return problem;
        var newPath = Path.Combine(Path.GetDirectoryName(path)!, newName);
        var onlyTheCaseChanges = newName.Equals(oldName, StringComparison.OrdinalIgnoreCase);
        if (!onlyTheCaseChanges && Path.Exists(newPath))
            return $"In questa cartella c'è già «{newName}».";
        if (!RunFileOperation(() => ShellFileOperations.Rename(path, newPath, OwnerWindow)))
            return "Impossibile rinominare: forse è aperto in un altro programma.";
        if (isFolder && IsInsideArchive(path))
            FollowFolder(KeyOf(path), KeyOf(newPath));
        Reload();
        return null;
    }

    /// <summary>A folder was renamed or moved: its metadata and the selection follow it.</summary>
    private void FollowFolder(string oldKey, string newKey)
    {
        UpdateMetadata(metadata => metadata.MoveFolder(oldKey, newKey));
        if (_selectedTopicPath is { } selected && IsKeyAtOrBelow(selected, oldKey))
            _selectedTopicPath = newKey + selected[oldKey.Length..];
    }

    /// <summary>A folder was deleted: its metadata goes, and a selection inside it moves to its parent.</summary>
    private void ForgetFolder(string key)
    {
        UpdateMetadata(metadata => metadata.RemoveFolder(key));
        if (_selectedTopicPath is { } selected && IsKeyAtOrBelow(selected, key))
        {
            var slash = key.LastIndexOf('/');
            _selectedTopicPath = slash > 0 ? key[..slash] : null;
        }
    }

    private static bool IsKeyAtOrBelow(string key, string folderKey) =>
        key.Equals(folderKey, StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith(folderKey + "/", StringComparison.OrdinalIgnoreCase);

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
            Results.Add(new SearchResultViewModel(hit));
        ResultsHeader = hits.Count switch
        {
            0 => "Nessun risultato",
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
        try
        {
            var index = await Task.Run(() => SearchIndex.Build(root, indexing.Token), indexing.Token);
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
        // Reloading while the user types a name would throw the prompt's context away.
        if (_isPanelVisible && Prompt is null && !IsBusy)
            Reload();
        else
            _reloadPending = true;
        RebuildIndex();
    });

    private void UpdateStatus()
    {
        var parts = new List<string> { _root };
        if (_index is not null)
            parts.Add($"{_index.Count:N0} elementi");
        if (_lastOpenTime is { } openTime)
            parts.Add($"aperto in {openTime.TotalMilliseconds:N0} ms");
        Status = string.Join("   ·   ", parts);
    }

    private void RunShell(Action action, string name)
    {
        try
        {
            action();
            ItemOpened?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write($"Could not open {name}: {e}");
            Status = $"Impossibile aprire «{name}»: {e.Message}";
        }
    }

    private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }
}
