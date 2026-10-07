using System.Runtime.ExceptionServices;

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
