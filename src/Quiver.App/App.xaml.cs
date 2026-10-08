using System.Windows;
using System.Windows.Threading;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App;

/// <summary>
/// Quiver lives in the notification area: the panel is created at startup and shown with the global shortcut,
/// the tray icon, or a command from a second Quiver.exe (Quiver.exe --toggle, or --sort from «Invia a Quiver»).
/// </summary>
public partial class App : Application, ISettingsHost
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly WindowsIntegration _windows = new();
    private SingleInstance? _singleInstance;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;
    private PanelWindow? _panel;
    private MainViewModel? _viewModel;
    private SettingsWindow? _settingsWindow;
    private AppSettings _settings = new();

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

        DispatcherUnhandledException += OnUnhandledException;
        _singleInstance = new SingleInstance();
        if (!_singleInstance.IsFirstInstance)
        {
            // Files from «Invia a Quiver» go to the running Quiver, which asks where to put them.
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

        _settings = AppSettings.Load();
        // --archive is a one-off override: only an archive chosen in Quiver becomes the one in use.
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

        // A trial with a demo archive can use a stand-in Desktop, so that dragging never moves the user's own files there.
        IReadOnlyList<InboxSource> inboxSources = options.DesktopStandIn is { } desktop
            ? [new InboxSource("Desktop", [desktop])]
            : WindowsFolders.InboxSources(_settings);
        _viewModel = new MainViewModel(root, inboxSources: inboxSources);
        var panel = _panel = new PanelWindow(_viewModel, _settings);
        panel.ArchivesRequested += (_, _) => ShowSettings(SettingsSection.Archives);
        panel.Prepare();

        _tray = new TrayIcon(_settings.Hotkey, ShowPanelFromTray, OpenArchiveFolder,
            () => ShowSettings(SettingsSection.Archives), () => ShowSettings(SettingsSection.Hotkey),
            () => panel.Topmost, value => panel.Topmost = value, Quit);
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
        Log.Write($"Started. Archive: {root}. Shortcut: {_settings.Hotkey}.");

        if (options.SortPaths.Count > 0)
            SortFiles(options.SortPaths);
        else if (options.Command is Commands.Show or Commands.Toggle)
            _panel.ShowPanel();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopping.Cancel();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Windows is shutting down or signing out: the window closes for good, remembering where it was.</summary>
    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _panel?.AllowClose();
        base.OnSessionEnding(e);
    }

    /// <summary>«Esci»: the only way the window really closes; otherwise it goes back to the notification area.</summary>
    private void Quit()
    {
        _panel?.AllowClose();
        Shutdown();
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

    /// <summary>«Invia a Quiver» from Explorer: the panel opens, asking where the files go.</summary>
    private void SortFiles(IReadOnlyList<string> paths)
    {
        if (_panel is null || _viewModel is null)
            return;
        var existing = paths.Where(Path.Exists).Select(Path.GetFullPath).ToList();
        Log.Write($"Sort requested for {paths.Count} paths, {existing.Count} found");
        _panel.ShowPanel();
        if (existing.Count == 0)
        {
            _viewModel.Status = "I file da mandare nell'archivio non ci sono più.";
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
    /// The window of the settings, from the tray or from Quiver: one at a time, on the page asked for. Opened from a visible
    /// Quiver, it belongs to it and stays above it, pinned or not.
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
            Log.Write($"Shortcut changed to {shortcut}");
            return true;
        }
        // The one in use comes back: the attempt let it go.
        _hotkey.TryRegister(_settings.Hotkey);
        Log.Write($"Could not register the shortcut {shortcut}, keeping {_settings.Hotkey}");
        return false;
    }

    void ISettingsHost.InboxChanged() => _viewModel?.UseInboxSources(WindowsFolders.InboxSources(_settings));

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
            "Quiver", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
