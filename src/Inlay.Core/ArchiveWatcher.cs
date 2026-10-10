namespace Inlay.Core;

/// <summary>
/// Reports changes made to the archive from outside Inlay (Explorer, other apps). Bursts of events,
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
        _watcher = new FileSystemWatcher
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
        try
        {
            _watcher.Path = root;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Raised on a thread-pool thread.</summary>
    public event EventHandler? Changed;

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
        _watcher.Dispose();
        _timer.Dispose();
    }
}
