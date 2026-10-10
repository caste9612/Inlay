using Velopack;
using Velopack.Sources;

namespace Inlay.App.Services;

/// <summary>Where Inlay finds its new versions. The app uses <see cref="GitHubUpdater"/>; tests can use one of their own.</summary>
internal interface IUpdater
{
    /// <summary>Installed by its installer, so able to update itself: not when run from the code, as with dotnet run.</summary>
    bool IsSupported { get; }

    /// <summary>As "0.3.3"; null when not installed.</summary>
    string? CurrentVersion { get; }

    /// <summary>Looks for a newer version and downloads it, in the background.</summary>
    /// <returns>The version ready to be applied, as "0.4.0"; null when Inlay is up to date.</returns>
    Task<string?> PrepareAsync(CancellationToken cancellationToken);

    /// <summary>Closes Inlay at once, applies the version ready and starts Inlay again.</summary>
    void ApplyAndRestart();

    /// <summary>The version ready is applied as soon as Inlay has exited, without starting it again.</summary>
    void ApplyOnExit();
}

/// <summary>
/// The new versions from the releases of the public repository on GitHub, through Velopack, without a token: each release
/// has the files Velopack reads (releases.win.json and the package). Pre-releases are not offered. A version downloaded
/// and never applied, as when Windows shuts down first, is found again by the check after the next start and offered
/// again: Velopack does not apply it at the start (see Program.Main).
/// </summary>
internal sealed class GitHubUpdater : IUpdater
{
    public const string Repository = "https://github.com/caste9612/Inlay";

    // Null when Velopack was not set up by Program.Main, as in the tests: then there is nothing to update.
    private readonly UpdateManager? _manager = Create();

    public bool IsSupported => _manager?.IsInstalled == true;

    public string? CurrentVersion => _manager?.CurrentVersion?.ToString();

    public async Task<string?> PrepareAsync(CancellationToken cancellationToken)
    {
        if (_manager is not { IsInstalled: true } manager)
            return null;
        // Downloaded before and not applied yet: ready as it is.
        if (manager.UpdatePendingRestart is { } pending)
            return pending.Version.ToString();
        var update = await manager.CheckForUpdatesAsync();
        if (update is null)
            return null;
        await manager.DownloadUpdatesAsync(update, null, cancellationToken);
        return update.TargetFullRelease.Version.ToString();
    }

    public void ApplyAndRestart() => _manager?.ApplyUpdatesAndRestart(_manager.UpdatePendingRestart);

    public void ApplyOnExit()
    {
        if (_manager?.UpdatePendingRestart is { } pending)
            _manager.WaitExitThenApplyUpdates(pending, silent: true, restart: false);
    }

    /// <summary>
    /// The releases on GitHub; or, with the environment variable INLAY_UPDATES, a folder or an address with the files of
    /// vpk pack, to try an update before publishing it (docs/rilascio.md).
    /// </summary>
    private static UpdateManager? Create()
    {
        try
        {
            var other = Environment.GetEnvironmentVariable("INLAY_UPDATES");
            IUpdateSource source = string.IsNullOrWhiteSpace(other)
                ? new GithubSource(Repository, accessToken: null, prerelease: false)
                : Directory.Exists(other)
                    ? new SimpleFileSource(new DirectoryInfo(other))
                    : new SimpleWebSource(other);
            return new UpdateManager(source);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
