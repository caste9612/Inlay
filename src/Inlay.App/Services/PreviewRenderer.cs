using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Inlay.App.ViewModels;
using Inlay.App.Views;
using Inlay.Core;

namespace Inlay.App.Services;

/// <summary>
/// Diagnostics: draws the panel for an archive into a PNG without showing any window, to check the layout
/// from a script. Example: Inlay.exe --archive D:\Archivio --render-preview panel.png --topic Lavoro/Clienti
/// </summary>
internal static class PreviewRenderer
{
    private static readonly Size PanelSize = new(1240, 780);
    // The window less the frame of Windows, which is invisible: its title bar is in the view.
    private static readonly Size SettingsSize = new(960, 650);

    public static void Render(CommandLineOptions options)
    {
        var output = options.PreviewOutput ?? throw new ArgumentException("The file to draw into is missing.");
        var root = options.ArchiveRoot ?? throw new ArgumentException("--render-preview also needs --archive <folder>.");
        // No window drives the dispatcher here: the icons of Windows are read at once, on this thread.
        ShellIcons.Synchronous = true;
        if (options.PreviewSettings)
        {
            RenderSettings(root, options.PreviewWelcome, options.PreviewSection, output);
            return;
        }

        // Recent places of the preview only, in a file of its own: the first tile of the board, as the place used last.
        var recent = RecentDestinations.Load(Path.Combine(Path.GetTempPath(), "inlay-preview-recenti.json"));
        using var viewModel = new MainViewModel(root, live: false, recent: recent, readOnly: true);
        viewModel.BuildIndexNow();
        if (options.PreviewTopic is { } topic)
            viewModel.SelectTopicByPath(topic);
        viewModel.IsCanvasShown = options.PreviewCanvas;
        if (options.PreviewQuery is { } query)
            viewModel.Query = query;
        // A confirmation over the board: moving its first tile to the Recycle Bin.
        if (options.PreviewPrompt && viewModel.Tiles.FirstOrDefault(tile => tile.IsOwnFolder) is { } tile)
            viewModel.AskRecycle([tile.FolderPath]);
        // A question with a field: a new shortcut, with something typed that is neither a path nor a site.
        if (options.PreviewLinkPrompt && viewModel.BoardFolder is { } folder)
        {
            viewModel.AskNewLink(folder);
            viewModel.Prompt!.Text = "smith";
            viewModel.Prompt.ConfirmCommand.Execute(null);
        }
        // «Move to…» for the first item of the board.
        if (options.PreviewPicker && viewModel.Tiles.SelectMany(candidate => candidate.Items).FirstOrDefault() is { } first)
        {
            if (viewModel.Tiles.FirstOrDefault(candidate => candidate.IsOwnFolder) is { RelativePath: { } key })
                recent.Remember(root, key);
            viewModel.AskDestination([first.Item.FullPath]);
        }
        // The toast of a move, with its buttons; nothing is moved.
        if (options.PreviewNotice && viewModel.Tiles.FirstOrDefault(candidate => candidate.IsOwnFolder) is { } destination)
        {
            viewModel.Tell(new ToastViewModel(ToastKind.Success, $"3 items moved to “{destination.Title}”.",
                notice: new StatusNotice(destination.FolderPath, []), undoStep: new CopyStep([])));
            viewModel.Tell(ToastKind.Danger, "Couldn't open “Quote.pdf”", "The file is being used by another process.");
        }

        var view = new PanelView { DataContext = viewModel };
        Save(view, PanelSize, output, options.PreviewCanvas ? () => view.FrameCanvas(options.PreviewZoom, options.PreviewTopic) : null);
    }

    /// <summary>
    /// The window of the settings on one of its pages, with the archive as the one in use and a second one that is not
    /// there; the first time, with no archive yet. The settings are a copy in a file of their own: nothing of the PC
    /// is touched, and what Windows says is made up.
    /// </summary>
    private static void RenderSettings(string root, bool welcome, string? section, string output)
    {
        var settings = new AppSettings { SavePath = Path.Combine(Path.GetTempPath(), "inlay-preview-settings.json") };
        if (!welcome)
        {
            settings.Archives.Add(Path.Combine(Path.GetPathRoot(root)!, "Old vault"));
            settings.UseArchive(root);
        }
        var page = section?.ToLowerInvariant() switch
        {
            "hotkey" => SettingsSection.Hotkey,
            "windows" => SettingsSection.Windows,
            "about" => SettingsSection.About,
            _ => SettingsSection.Archives,
        };
        var viewModel = new SettingsViewModel(settings, new PreviewHost(welcome ? null : root), WindowsFolders.ReservedForArchives(),
            new PreviewWindows(), new UpdatesViewModel(UpdatesViewModel.VersionOfTheCode(), canUpdate: true, () => { }, () => { }) { LastCheck = DateTime.Now }, page);
        Save(new SettingsView { DataContext = viewModel }, SettingsSize, output);
    }

    /// <param name="prepare">What to do once the view is laid out, before it is drawn: placing the camera of the canvas.</param>
    private static void Save(FrameworkElement view, Size size, string output, Action? prepare = null)
    {
        Settle(view);
        view.Measure(size);
        view.Arrange(new Rect(size));
        Settle(view);
        if (prepare is not null)
        {
            prepare();
            Settle(view);
            view.Measure(size);
            view.Arrange(new Rect(size));
            Settle(view);
        }

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
