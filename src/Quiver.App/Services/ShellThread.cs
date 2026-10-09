namespace Quiver.App.Services;

/// <summary>
/// Runs a file operation of the Windows shell on a thread of its own: during a long copy the window stays usable, the
/// progress and conflict dialogs of Windows show as usual, and operations on other files run alongside, each with its
/// own progress window as in Explorer. The shell wants an STA thread, like the UI thread.
/// </summary>
internal static class ShellThread
{
    /// <returns>What the operation returned, once it is done; its exception if it threw.</returns>
    public static Task<bool> Run(Func<bool> operation)
    {
        // The continuation goes back to whoever waits, never runs on this thread.
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(operation());
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        })
        {
            // In the background: on «Esci» Quiver waits for the operations itself, while Windows signing out cannot wait.
            IsBackground = true,
            Name = "Quiver file operation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
