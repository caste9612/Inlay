using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Quiver.App.ViewModels;
using Quiver.App.Views;
using Quiver.Core;

namespace Quiver.App.Services;

/// <summary>
/// Diagnostics: draws the panel for an archive into a PNG without showing any window, to check the layout
/// from a script. Example: Quiver.exe --archive D:\Archivio --render-preview panel.png --topic Lavoro/Clienti
/// </summary>
internal static class PreviewRenderer
{
    private static readonly Size PanelSize = new(1240, 780);
    // The window less the frame of Windows, which is invisible: its title bar is in the view.
    private static readonly Size SettingsSize = new(960, 650);

    public static void Render(CommandLineOptions options)
    {
        var output = options.PreviewOutput ?? throw new ArgumentException("Manca il file di destinazione.");
        var root = options.ArchiveRoot ?? throw new ArgumentException("--render-preview richiede anche --archive <cartella>.");
        // No window drives the dispatcher here: the icons of Windows are read at once, on this thread.
        ShellIcons.Synchronous = true;
        if (options.PreviewSettings)
        {
            RenderSettings(root, options.PreviewWelcome, options.PreviewSection, output);
            return;
        }

        // The real Desktop stays out of previews: a folder can stand in for it.
        IReadOnlyList<InboxSource> sources = options.DesktopStandIn is { } desktop ? [new InboxSource("Desktop", [desktop])] : [];
        // Recent places of the preview only, in a file of its own: the first tile of the board, as the place used last.
        var recent = RecentDestinations.Load(Path.Combine(Path.GetTempPath(), "quiver-preview-recenti.json"));
        using var viewModel = new MainViewModel(root, live: false, inboxSources: sources, recent: recent);
        viewModel.BuildIndexNow();
        if (options.PreviewTopic is { } topic)
            viewModel.SelectTopicByPath(topic);
        if (options.PreviewInbox)
            viewModel.ShowInboxCommand.Execute(null);
        if (options.PreviewQuery is { } query)
            viewModel.Query = query;
        // A confirmation over the board: moving its first tile to the Recycle Bin.
        if (options.PreviewPrompt && viewModel.Tiles.FirstOrDefault(tile => tile.IsOwnFolder) is { } tile)
            viewModel.AskRecycle([tile.FolderPath]);
        // A question with a field: a new shortcut, with something typed that is neither a path nor a site.
        if (options.PreviewLinkPrompt && viewModel.BoardFolder is { } folder)
        {
            viewModel.AskNewLink(folder);
            viewModel.Prompt!.Text = "rossi";
            viewModel.Prompt.ConfirmCommand.Execute(null);
        }
        // «Sposta in…» for the first item of the board.
        if (options.PreviewPicker && viewModel.Tiles.SelectMany(candidate => candidate.Items).FirstOrDefault() is { } first)
        {
            if (viewModel.Tiles.FirstOrDefault(candidate => candidate.IsOwnFolder) is { RelativePath: { } key })
                recent.Remember(root, key);
            viewModel.AskDestination([first.Item.FullPath]);
        }
        // The status bar as after a move, with its buttons; nothing is moved.
        if (options.PreviewNotice && viewModel.Tiles.FirstOrDefault(candidate => candidate.IsOwnFolder) is { } destination)
        {
            viewModel.Status = $"3 elementi spostati in «{destination.Title}».";
            viewModel.Notice = new StatusNotice(destination.FolderPath, []);
        }

        Save(new PanelView { DataContext = viewModel }, PanelSize, output);
    }

    /// <summary>
    /// The window of the settings on one of its pages, with the archive as the one in use and a second one that is not
    /// there; the first time, with no archive yet. The settings are a copy in a file of their own: nothing of the PC
    /// is touched, and what Windows says is made up.
    /// </summary>
    private static void RenderSettings(string root, bool welcome, string? section, string output)
    {
        var settings = new AppSettings { SavePath = Path.Combine(Path.GetTempPath(), "quiver-preview-settings.json") };
        if (!welcome)
        {
            settings.Archives.Add(Path.Combine(Path.GetPathRoot(root)!, "Archivio vecchio"));
            settings.UseArchive(root);
        }
        var page = section?.ToLowerInvariant() switch
        {
            "hotkey" => SettingsSection.Hotkey,
            "inbox" => SettingsSection.Inbox,
            "windows" => SettingsSection.Windows,
            _ => SettingsSection.Archives,
        };
        var viewModel = new SettingsViewModel(settings, new PreviewHost(welcome ? null : root), WindowsFolders.ReservedForArchives(),
            new PreviewWindows(), page);
        Save(new SettingsView { DataContext = viewModel }, SettingsSize, output);
    }

    private static void Save(FrameworkElement view, Size size, string output)
    {
        Settle(view);
        view.Measure(size);
        view.Arrange(new Rect(size));
        Settle(view);

        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }

    /// <summary>Lets bindings, layout and queued work run, since no window is driving them.</summary>
    private static void Settle(UIElement view)
    {
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        view.UpdateLayout();
    }

    private sealed class PreviewHost(string? root) : ISettingsHost
    {
        public string? CurrentRoot => root;

        public void Use(string archive)
        {
        }

        public void Release()
        {
        }

        public bool TryUseHotkey(string shortcut) => true;

        public void InboxChanged()
        {
        }
    }

    /// <summary>What Windows would say, made up: both switches on.</summary>
    private sealed class PreviewWindows : IWindowsIntegration
    {
        public bool AutostartEnabled => true;

        public bool TrySetAutostart(bool enabled) => true;

        public bool SendToInstalled => true;

        public bool TrySetSendTo(bool installed) => true;
    }
}
