using System.Runtime.CompilerServices;
using Inlay.App.Services;

namespace Inlay.App.Tests;

/// <summary>What the app logs during the tests goes to a file of the tests: the log of the user tells only about Inlay.</summary>
internal static class TestLog
{
    // Before any test runs, in every test class: what a module initializer is for.
    [ModuleInitializer]
    internal static void KeepOffTheUsersLog() =>
        Log.FilePath = Path.Combine(Path.GetTempPath(), "inlay-app-tests", "inlay.log");
}
