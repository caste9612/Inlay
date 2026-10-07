using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Quiver.App.Tests;

/// <summary>The shell and WPF want a single-threaded apartment, like the UI thread of the app.</summary>
internal static class StaThread
{
    public static T Run<T>(Func<T> action)
    {
        T result = default!;
        Run(() => { result = action(); });
        return result;
    }

    /// <summary>Asynchronous work, whose continuations come back to the STA thread as in the app, through its dispatcher.</summary>
    public static void RunAsync(Func<Task> action) => Run(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        var frame = new DispatcherFrame();
        ExceptionDispatchInfo? error = null;
        dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                error = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                frame.Continue = false;
            }
        });
        Dispatcher.PushFrame(frame);
        error?.Throw();
    });

    public static void Run(Action action)
    {
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                error = ExceptionDispatchInfo.Capture(e);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}
