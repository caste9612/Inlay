using System.Collections.Concurrent;

namespace Quiver.App.Services;

/// <summary>
/// Runs the file operations of the Windows shell one after the other, on a thread of their own: during a long copy the
/// window stays usable, while the progress and conflict dialogs of Windows show as usual. The shell wants an STA thread,
/// like the UI thread.
/// </summary>
internal sealed class FileOperationQueue : IDisposable
{
    private readonly BlockingCollection<Job> _jobs = new();

    public FileOperationQueue()
    {
        // In the background: on «Esci» Quiver waits for the operations itself, while Windows signing out cannot wait.
        var thread = new Thread(Work) { IsBackground = true, Name = "Quiver file operations" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <returns>What the operation returned, once it is done; its exception if it threw.</returns>
    public Task<bool> Run(Func<bool> operation)
    {
        // The continuation goes back to whoever waits, never runs on this thread.
        var job = new Job(operation, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        _jobs.Add(job);
        return job.Done.Task;
    }

    /// <summary>The operation under way finishes; nothing new starts.</summary>
    public void Dispose() => _jobs.CompleteAdding();

    private void Work()
    {
        foreach (var job in _jobs.GetConsumingEnumerable())
        {
            try
            {
                job.Done.SetResult(job.Operation());
            }
            catch (Exception e)
            {
                job.Done.SetException(e);
            }
        }
    }

    private sealed record Job(Func<bool> Operation, TaskCompletionSource<bool> Done);
}
