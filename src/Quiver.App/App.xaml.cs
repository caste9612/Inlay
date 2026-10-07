using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Quiver.App.Services;
using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App;

/// <summary>
/// Quiver lives in the notification area: the panel is created at startup and shown with the global shortcut,
/// the tray icon, or a command from a second Quiver.exe (Quiver.exe --toggle).
/// </summary>
public partial class App : Application
{
    private readonly CancellationTokenSource _stopping = new();
    private SingleInstance? _singleInstance;
    private GlobalHotkey? _hotkey;
    private TrayIcon? _tray;
    private PanelWindow? _panel;
    private MainViewModel? _viewModel;
    private AppSettings _settings = new();

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
        // --archive is a one-off override: only a folder chosen in the dialog becomes the default.
        var root = options.ArchiveRoot ?? _settings.ArchiveRoot;
        if (root is null || !Directory.Exists(root))
        {
            root = AskForArchiveRoot(firstRun: true, root);
            if (root is null)
            {
                Shutdown();
                return;
            }
            SaveArchiveRoot(root);
        }

        // A trial with a demo archive can use a stand-in Desktop, so that dragging never moves the user's own files there.
        IReadOnlyList<InboxSource> inboxSources = options.DesktopStandIn is { } desktop
            ? [new InboxSource("Desktop", [desktop])]
            : WindowsFolders.InboxSources(_settings);
        _viewModel = new MainViewModel(root, inboxSources: inboxSources);
        _panel = new PanelWindow(_viewModel);
        _panel.Prepare();

        _tray = new TrayIcon(_settings.Hotkey, ShowPanelFromTray, OpenArchiveFolder, ChangeArchive, Shutdown);
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

    private void ChangeArchive()
    {
        _panel?.HidePanel("changing archive");
        if (AskForArchiveRoot(firstRun: false, _viewModel?.Root) is not { } root)
            return;
        SaveArchiveRoot(root);
        _viewModel?.LoadArchive(root);
        Log.Write($"Archive changed to {root}");
    }

    private void SaveArchiveRoot(string root)
    {
        _settings.ArchiveRoot = root;
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save the settings: {e.Message}");
        }
    }

    private static string? AskForArchiveRoot(bool firstRun, string? current)
    {
        if (firstRun)
        {
            MessageBox.Show(
                "Scegli la cartella che fa da archivio: ogni sua sottocartella diventa un argomento.\n\n" +
                "Puoi crearne una nuova, ad esempio D:\\Archivio, oppure usare una cartella che hai già. " +
                "Quiver lavora sui file veri: quello che sposti o rinomini qui cambia anche in Explorer. " +
                "Ciò che elimini va nel Cestino, e Ctrl+Z annulla l'ultima operazione.",
                "Benvenuto in Quiver", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        var dialog = new OpenFolderDialog { Title = "Scegli la cartella dell'archivio" };
        if (current is not null && Directory.Exists(current))
            dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
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
            "Quiver", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
