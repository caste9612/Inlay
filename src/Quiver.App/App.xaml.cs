using System.Windows;
using System.Windows.Threading;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App;

/// <summary>
/// Quiver lives in the notification area: the panel is created at startup and shown with the global shortcut,
/// the tray icon, or a command from a second Quiver.exe (Quiver.exe --toggle).
/// </summary>
public partial class App : Application, IArchiveHost
{
    private readonly CancellationTokenSource _stopping = new();
    private SingleInstance? _singleInstance;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;
    private PanelWindow? _panel;
    private MainViewModel? _viewModel;
    private ArchivesWindow? _archivesWindow;
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
            SingleInstance.Send(options.Command ?? Commands.Toggle);
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
            // The first time, or the archive is gone: the window of the archives greets and helps to choose one.
            new ArchivesWindow(new ArchivesViewModel(_settings, this, WindowsFolders.ReservedForArchives())).ShowDialog();
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
        _panel = new PanelWindow(_viewModel);
        _panel.ArchivesRequested += (_, _) => ShowArchives();
        _panel.Prepare();

        _tray = new TrayIcon(_settings.Hotkey, ShowPanelFromTray, OpenArchiveFolder, ShowArchives, Shutdown);
        _hotkey = new GlobalHotkey();
        _hotkey.Pressed += (_, _) => _panel.Toggle();
        if (!_hotkey.TryRegister(_settings.Hotkey))
        {
            Log.Write($"Could not register the shortcut {_settings.Hotkey}");
            _tray.ShowWarning(
                $"La scorciatoia {_settings.Hotkey} non è disponibile: forse la usa un altro programma. " +
                $"Puoi cambiarla in {AppSettings.FilePath}.");
        }

        _singleInstance.CommandReceived += command => Dispatcher.InvokeAsync(() => Execute(command));
        _singleInstance.StartListening(_stopping.Token);
        Log.Write($"Started. Archive: {root}. Shortcut: {_settings.Hotkey}.");

        if (options.Command is Commands.Show or Commands.Toggle)
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

    private void Execute(string command)
    {
        switch (command)
        {
            case Commands.Toggle: _panel?.Toggle(); break;
            case Commands.Show: _panel?.ShowPanel(); break;
            case Commands.Hide: _panel?.HidePanel("--hide command", returnFocus: true); break;
            case Commands.Exit: Shutdown(); break;
            default: Log.Write($"Unknown command: {command}"); break;
        }
    }

    /// <summary>Waits for the tray menu to close first, otherwise it would take the focus back from the panel.</summary>
    private void ShowPanelFromTray() =>
        Dispatcher.BeginInvoke(() => _panel?.ShowPanel(), DispatcherPriority.ApplicationIdle);

    private void OpenArchiveFolder()
    {
        if (_viewModel is not null)
            _viewModel.OpenFolder(_viewModel.Root);
    }

    /// <summary>The window of the archives, from the tray or the panel: one at a time.</summary>
    private void ShowArchives()
    {
        _panel?.HidePanel("archives");
        if (_archivesWindow is { } open)
        {
            open.Activate();
            return;
        }
        _archivesWindow = new ArchivesWindow(new ArchivesViewModel(_settings, this, WindowsFolders.ReservedForArchives()));
        _archivesWindow.Closed += (_, _) => _archivesWindow = null;
        _archivesWindow.Show();
        _archivesWindow.Activate();
    }

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
