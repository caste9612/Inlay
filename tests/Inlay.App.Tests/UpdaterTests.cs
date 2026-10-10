using Inlay.App.Services;

namespace Inlay.App.Tests;

/// <summary>The updates from GitHub, as far as they can be tried without installing Inlay.</summary>
public sealed class UpdaterTests
{
    [Fact]
    public async Task AnInlayNotInstalled_DoesNotLookForUpdates()
    {
        // As here, or run from the code: nothing to update, and no question to GitHub.
        var updater = new GitHubUpdater();

        Assert.False(updater.IsSupported);
        Assert.Null(updater.CurrentVersion);
        Assert.Null(await updater.PrepareAsync(CancellationToken.None));
    }
}
