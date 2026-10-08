using System.Runtime.CompilerServices;
using Quiver.App.Services;

namespace Quiver.App.Tests;

/// <summary>What the app logs during the tests goes to a file of the tests: the log of the user tells only about Quiver.</summary>
internal static class TestLog
{
    // Before any test runs, in every test class: what a module initializer is for.
    [ModuleInitializer]
    internal static void KeepOffTheUsersLog() =>
        Log.FilePath = Path.Combine(Path.GetTempPath(), "quiver-app-tests", "quiver.log");
}
