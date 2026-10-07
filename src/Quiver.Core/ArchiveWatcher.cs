namespace Quiver.Core;

/// <summary>
/// Reports changes made to the archive from outside Quiver (Explorer, other apps). Bursts of events,
/// like copying a whole folder, are merged into a single notification.
/// </summary>
public sealed class ArchiveWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Timer _timer;
    private readonly TimeSpan _delay;

    /// <param name="otherFolders">Folders watched too, without their subfolders: those of the inbox sources, like the Desktop.</param>
    public ArchiveWatcher(string root, TimeSpan delay, IEnumerable<string>? otherFolders = null)
    {
        _delay = delay;
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        try
        {
            _watchers.Add(Watch(root, includeSubdirectories: true));
            foreach (var folder in otherFolders ?? [])
            {
                // A source that cannot be watched is read again each time the panel opens: no reason to fail for it.
                if (Directory.Exists(folder) && TryWatch(folder) is { } watcher)
                    _watchers.Add(watcher);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Raised on a thread-pool thread.</summary>
    public event EventHandler? Changed;

    private FileSystemWatcher? TryWatch(string folder)
    {
        try
        {
            return Watch(folder, includeSubdirectories: false);
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private FileSystemWatcher Watch(string folder, bool includeSubdirectories)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = includeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Created += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnChanged;
        // The event buffer overflowed and some changes were lost: a full reload covers them.
        watcher.Error += (_, _) => Schedule();
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Schedule();

    private void Schedule()
    {
        try
        {
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // An event that arrived while the watcher was being closed.
        }
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _timer.Dispose();
    }
}
