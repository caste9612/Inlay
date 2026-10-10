using System.Globalization;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inlay.App.Services;

namespace Inlay.App.ViewModels;

/// <summary>
/// The version of Inlay and its updates, as the page «About» of the settings shows them. The app keeps it up to date as it
/// looks for new versions, a minute after the start and then every six hours; the button of the page looks now.
/// </summary>
public sealed partial class UpdatesViewModel : ObservableObject
{
    /// <summary>The releases on GitHub, with what each version brings.</summary>
    public const string ReleasesPage = GitHubUpdater.Repository + "/releases";

    private readonly Action _check;
    private readonly Action _restart;

    /// <summary>A check under way: the button waits for it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    private bool _isChecking;

    /// <summary>A new version downloaded and ready, as "0.4.0"; null when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsReady))]
    private string? _readyVersion;

    /// <summary>When the last check that got an answer ended; null before the first one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private DateTime? _lastCheck;

    /// <summary>The last check got no answer from GitHub, as without a connection.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _lastCheckFailed;

    /// <param name="version">The version running, as "0.3.4".</param>
    /// <param name="canUpdate">Installed by its installer: an Inlay run from the code does not update itself.</param>
    /// <param name="check">Looks for a new version now; how it went comes back through the properties.</param>
    /// <param name="restart">«Restart to update»: Inlay closes, the new version is applied and Inlay starts again.</param>
    internal UpdatesViewModel(string version, bool canUpdate, Action check, Action restart)
    {
        Version = version;
        CanUpdate = canUpdate;
        _check = check;
        _restart = restart;
    }

    /// <summary>As "0.3.4".</summary>
    public string Version { get; }

    public string VersionText => $"Version {Version}";

    public bool CanUpdate { get; }

    public bool IsReady => ReadyVersion is not null;

    /// <summary>Where the updates are, in a sentence: the page says it under «Updates».</summary>
    public string StatusText =>
        !CanUpdate ? "This Inlay runs from the code, not from its installer: it doesn't update itself."
        : ReadyVersion is { } ready ? $"Inlay {ready} is ready: it updates when you quit, or right away with Restart to update."
        : IsChecking ? "Checking for updates…"
        : LastCheckFailed ? "Couldn't check for updates: the connection may be down. Inlay tries again by itself later."
        : LastCheck is { } when ? $"Inlay is up to date: checked {DateLabel.For(when)} at {when.ToString("HH:mm", CultureInfo.InvariantCulture)}."
        : "Not checked yet: Inlay looks for a new version a minute after it starts.";

    /// <summary>The version of an Inlay run from the code, which Velopack does not know: as the project says it, "0.3.4".</summary>
    internal static string VersionOfTheCode() =>
        typeof(UpdatesViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    private bool CanCheck() => CanUpdate && !IsChecking;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private void Check() => _check();

    [RelayCommand]
    private void Restart() => _restart();
}
