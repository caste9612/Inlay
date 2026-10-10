using System.Windows;
using System.Windows.Threading;
using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App;

/// <summary>
/// Inlay lives in the notification area: the panel is created at startup and shown with the global shortcut,
/// the tray icon, or a command from a second Inlay.exe (Inlay.exe --toggle, or --sort from «Invia a Inlay»).
/// </summary>
public partial class App : Application, ISettingsHost
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly WindowsIntegration _windows = new();
    private readonly IUpdater _updater = new GitHubUpdater();
    private DispatcherTimer? _updateTimer;
    private bool _checkingUpdates;
    // The version the tray has told about: a new one is told once, not at every check.
    private string? _toldVersion;
    private SingleInstance? _singleInstance;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;
    private PanelWindow? _panel;
    private MainViewModel? _viewModel;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();
    private bool _quitting;

    /// <summary>The archive chosen the first time, before the panel exists.</summary>
    private string? _chosenRoot;

    string? IArchiveHost.CurrentRoot => _viewModel?.Root ?? _chosenRoot;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = CommandLineOptions.Parse(e.Args);

        if (options.PreviewOutput is not null)
        {
            Shutdown(RenderPreview(options));
            return;
        }
        // Next to an Inlay already running: it neither asks it for anything nor registers anything of its own.
        if (options.BenchmarkOutput is not null)
        {
            CanvasBenchmark.Start(options, Shutdown);
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // Files from «Invia a Inlay» go to the running Inlay, which asks where to put them.
            var command = options.Command ?? (options.SortPaths.Count > 0 ? Commands.Sort : Commands.Toggle);
            SingleInstance.Send(command, options.SortPaths);
            Shutdown();
            return;
        }
        if (options.Command == Commands.Exit)
        {
            Shutdown();
            return;
        }

        // Up to 0.3.2 Inlay was called Quiver: the first time, its settings and recent places come over.
        FromQuiver.TakeOverSettings();
        _settings = AppSettings.Load();
        // --archive is a one-off override: only an archive chosen in Inlay becomes the one in use.
        var root = options.ArchiveRoot ?? _settings.ArchiveRoot;
        if (root is null || !Directory.Exists(root))
        {
            // The first time, or the archive is gone: the window greets and helps to choose an archive.
            new SettingsWindow(NewSettingsViewModel(SettingsSection.Archives)).ShowDialog();
            root = _chosenRoot;
            if (root is null)
            {
                Shutdown();
                return;
            }
        }

        _viewModel = new MainViewModel(root) { Hotkey = _settings.Hotkey };
        var panel = _panel = new PanelWindow(_viewModel, _settings);
        panel.ArchivesRequested += (_, _) => ShowSettings(SettingsSection.Archives);
        panel.Prepare();

        _tray = new TrayIcon(_settings.Hotkey, ShowPanelFromTray, OpenArchiveFolder,
            () => ShowSettings(SettingsSection.Archives), () => ShowSettings(SettingsSection.Hotkey),
            () => panel.Topmost, value => panel.Topmost = value,
            _updater.IsSupported ? () => CheckForUpdates(asked: true) : null, RestartToUpdate, Quit);
        _viewModel.RestartToUpdateRequested += (_, _) => RestartToUpdate();
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => _panel.Toggle();
        if (!_hotkey.TryRegister(_settings.Hotkey))
        {
            Log.Write($"Could not register the shortcut {_settings.Hotkey}");
            _tray.ShowWarning(
                $"La scorciatoia {_settings.Hotkey} non è disponibile: forse la usa un altro programma. " +
                "Puoi cambiarla in Impostazioni, dal menu dell'icona.");
        }

        _singleInstance.CommandReceived += (command, arguments) => Dispatcher.InvokeAsync(() => Execute(command, arguments));
        _singleInstance.StartListening(_stopping.Token);
        Log.Write($"Started. Archive: {root}. Shortcut: {_settings.Hotkey}. Version: {_updater.CurrentVersion ?? "not installed"}.");
        if (Program.UpdatedTo is { } updated)
            _tray.ShowInfo($"Inlay è aggiornato alla versione {updated}. Le novità sono nelle release su GitHub.");
        StartUpdates();

        if (options.SortPaths.Count > 0)
            SortFiles(options.SortPaths);
        else if (options.Command is Commands.Show or Commands.Toggle)
            _panel.ShowPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ReleaseResources();
        base.OnExit(e);
    }

    /// <summary>What Inlay holds of Windows: the shortcut, the icon in the tray, the watcher, the pipe of the other instances.</summary>
    private void ReleaseResources()
    {
        _stopping.Cancel();
        _updateTimer?.Stop();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
    }

    /// <summary>Windows is shutting down or signing out: the window closes for good, remembering where it was.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _panel?.AllowClose();
        base.OnSessionEnding(e);
    }

    /// <summary>«Esci». A new version downloaded and not applied yet is applied once Inlay has exited.</summary>
    private async void Quit()
    {
        if (!await BeginLeaving("Inlay si chiude"))
            return;
        if (_viewModel?.UpdateReady is not null)
            _updater.ApplyOnExit();
        Shutdown();
    }

    /// <summary>
    /// «Riavvia e aggiorna», from the menu of the tray or from the status bar: Inlay closes, the new version is applied and
    /// Inlay starts again.
    /// </summary>
    private async void RestartToUpdate()
    {
        if (!await BeginLeaving("Inlay si aggiorna"))
            return;
        // Velopack ends the process at once: what Shutdown would do is done first, the place of the window saved with it.
        _panel?.Close();
        ReleaseResources();
        try
        {
            _updater.ApplyAndRestart();
        }
        catch (Exception e)
        {
            Log.Write($"Could not apply the update: {e}");
        }
        // Still here: the update could not start. Inlay closes as with «Esci», and the version downloaded applies at the next start.
        Shutdown();
    }

    /// <summary>
    /// The way out, for «Esci» and for an update: the only ways the window really closes; otherwise it goes back to the
    /// notification area. A move or a copy under way finishes first: cut halfway, it would leave a file half copied. So
    /// does the copy of an archive to another drive, which would leave half an archive there.
    /// </summary>
    /// <param name="leaving">"Inlay si chiude" or "Inlay si aggiorna": what the notices say waits for the work under way.</param>
    /// <returns>False when the way out had begun already.</returns>
    private async Task<bool> BeginLeaving(string leaving)
    {
        if (_quitting)
            return false;
        _quitting = true;
        if (_viewModel is { IsBusy: true } viewModel)
        {
            var waiting = $"{leaving} appena finisce l'operazione sui file in corso.";
            viewModel.Tell(ToastKind.Info, waiting);
            _tray?.ShowInfo(waiting);
            await viewModel.WhenIdle();
        }
        if (_settingsWindow?.ViewModel.Archives is { IsCopying: true } archives)
        {
            var copying = $"{leaving} appena finisce la copia dell'archivio.";
            archives.Status = copying;
            _tray?.ShowInfo(copying);
            await archives.WhenCopied();
        }
        _panel?.AllowClose();
        return true;
    }

    // ---- Updates ----

    /// <summary>A minute after the start, not to slow down the sign-in, and then every six hours, Inlay looks for a new version.</summary>
    private void StartUpdates()
    {
        if (!_updater.IsSupported)
            return;
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            CheckForUpdates(asked: false);
        };
        _updateTimer.Start();
    }

    /// <summary>
    /// Looks for a new version and downloads it in the background; once ready, the status bar and the menu of the tray
    /// offer to restart into it, and the tray tells about it once.
    /// </summary>
    /// <param name="asked">From «Cerca aggiornamenti»: the answer is told also when there is nothing new, or a problem.</param>
    private async void CheckForUpdates(bool asked)
    {
        if (_checkingUpdates || _quitting)
            return;
        _checkingUpdates = true;
        try
        {
            var version = await _updater.PrepareAsync(_stopping.Token);
            if (version is null)
            {
                if (asked)
                    _tray?.ShowInfo($"Inlay è aggiornato: questa è la versione {_updater.CurrentVersion}.");
                return;
            }
            Log.Write($"Update ready: {version}");
            if (_viewModel is not null)
                _viewModel.UpdateReady = version;
            _tray?.ShowUpdateReady(version);
            if (asked || _toldVersion != version)
            {
                _toldVersion = version;
                _tray?.ShowInfo($"Inlay {version} è pronto: si aggiorna quando esci, o subito con «Riavvia e aggiorna» dal menu dell'icona.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        // Whatever goes wrong, offline, GitHub not answering or too many questions in an hour, an update must never stop
        // Inlay: the next check tries again.
        catch (Exception e)
        {
            Log.Write($"Could not look for updates: {e}");
            if (asked)
                _tray?.ShowWarning("Non riesco a controllare gli aggiornamenti: forse manca la connessione. Riprova più tardi.");
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    private void Execute(string command, IReadOnlyList<string> arguments)
    {
        switch (command)
        {
            case Commands.Toggle: _panel?.Toggle(); break;
            case Commands.Show: _panel?.ShowPanel(); break;
            case Commands.Hide: _panel?.HidePanel("--hide command", returnFocus: true); break;
            case Commands.Sort: SortFiles(arguments); break;
            case Commands.Exit: Quit(); break;
            default: Log.Write($"Unknown command: {command}"); break;
        }
    }

    /// <summary>«Invia a Inlay» from Explorer: the panel opens, asking where the files go.</summary>
    private void SortFiles(IReadOnlyList<string> paths)
    {
        if (_panel is null || _viewModel is null)
            return;
        var existing = paths.Where(Path.Exists).Select(Path.GetFullPath).ToList();
        Log.Write($"Sort requested for {paths.Count} paths, {existing.Count} found");
        _panel.ShowPanel();
        if (existing.Count == 0)
        {
            _viewModel.Tell(ToastKind.Info, "I file da mandare nell'archivio non ci sono più.");
            return;
        }
        _viewModel.AskDestination(existing);
    }

    /// <summary>Waits for the tray menu to close first, otherwise it would take the focus back from the panel.</summary>
    private void ShowPanelFromTray() =>
        Dispatcher.BeginInvoke(() => _panel?.ShowPanel(), DispatcherPriority.ApplicationIdle);

    private void OpenArchiveFolder()
    {
        if (_viewModel is not null)
            _viewModel.OpenFolder(_viewModel.Root);
    }

    /// <summary>
    /// The window of the settings, from the tray or from Inlay: one at a time, on the page asked for. Opened from a visible
    /// Inlay, it belongs to it and stays above it, pinned or not.
    /// </summary>
    private void ShowSettings(SettingsSection section)
    {
        if (_settingsWindow is { } open)
        {
            open.Show(section);
            return;
        }
        _settingsWindow = new SettingsWindow(NewSettingsViewModel(section));
        if (_panel is { IsVisible: true } panel)
            _settingsWindow.Owner = panel;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private SettingsViewModel NewSettingsViewModel(SettingsSection section) =>
        new(_settings, this, WindowsFolders.ReservedForArchives(), _windows, section);

    void IArchiveHost.Use(string root)
    {
        if (_viewModel is null)
        {
            _chosenRoot = root;
            return;
        }
        _viewModel.LoadArchive(root);
        Log.Write($"Archive changed to {root}");
    }

    void IArchiveHost.Release() => _viewModel?.ReleaseArchive();

    bool ISettingsHost.TryUseHotkey(string shortcut)
    {
        // Before the panel exists, the first time, the shortcut is registered at startup from the settings.
        if (_hotkey is null)
            return true;
        if (_hotkey.TryRegister(shortcut))
        {
            _tray?.UpdateShortcut(shortcut);
            if (_viewModel is not null)
                _viewModel.Hotkey = shortcut;
            Log.Write($"Shortcut changed to {shortcut}");
            return true;
        }
        // The one in use comes back: the attempt let it go.
        _hotkey.TryRegister(_settings.Hotkey);
        Log.Write($"Could not register the shortcut {shortcut}, keeping {_settings.Hotkey}");
        return false;
    }

    private static int RenderPreview(CommandLineOptions options)
    {
        try
        {
            PreviewRenderer.Render(options);
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(options.PreviewOutput + ".error.txt", e.ToString());
            return 1;
        }
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Write($"Unexpected error: {e.Exception}");
        MessageBox.Show(
            $"Si è verificato un errore imprevisto:\n\n{e.Exception.Message}\n\nI dettagli sono nel log: {Log.FilePath}",
            "Inlay", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
