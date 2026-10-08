using System.Windows;

namespace Quiver.App.Tests;

/// <summary>
/// The resources of App.xaml, Brace's included, for the tests that build the views of the app. In a plain application of
/// WPF, never Quiver's own: its start would find the Quiver of the user running and send it a command. One for all the
/// tests, as Windows wants.
/// </summary>
internal static class AppResources
{
    private static readonly Lock Gate = new();

    public static void Load()
    {
        lock (Gate)
        {
            if (Application.Current is not null)
                return;
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var dictionary in new[] { "Brace/Brace.xaml", "Themes/Quiver.xaml" })
            {
                application.Resources.MergedDictionaries.Add(
                    new ResourceDictionary { Source = new Uri($"pack://application:,,,/Quiver;component/{dictionary}") });
            }
        }
    }
}
