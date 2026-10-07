namespace Quiver.Core;

/// <summary>
/// Reports changes made to the archive from outside Quiver (Explorer, other apps). Bursts of events,
/// like copying a whole folder, are merged into a single notification.
/// </summary>
public sealed class ArchiveWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _delay;

    public ArchiveWatcher(string root, TimeSpan delay)
    {
        _delay = delay;
        _timer = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty));
        _watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.Renamed += OnChanged;
        // The event buffer overflowed and some changes were lost: a full reload covers them.
        _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised on a thread-pool thread.</summary>
    public event EventHandler? Changed;

    private void OnChanged(object sender, FileSystemEventArgs e) => Schedule();

    private void Schedule() => _timer.Change(_delay, Timeout.InfiniteTimeSpan);

    public void Dispose()
    {
        _watcher.Dispose();
        _timer.Dispose();
    }
}
