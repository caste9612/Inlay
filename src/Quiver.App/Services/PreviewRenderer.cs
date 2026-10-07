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

    public static void Render(CommandLineOptions options)
    {
        var output = options.PreviewOutput ?? throw new ArgumentException("Manca il file di destinazione.");
        var root = options.ArchiveRoot ?? throw new ArgumentException("--render-preview richiede anche --archive <cartella>.");

        // The real Desktop stays out of previews: a folder can stand in for it.
        IReadOnlyList<InboxSource> sources = options.DesktopStandIn is { } desktop ? [new InboxSource("Desktop", [desktop])] : [];
        using var viewModel = new MainViewModel(root, live: false, inboxSources: sources);
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

        var view = new PanelView { DataContext = viewModel };
        Settle(view);
        view.Measure(PanelSize);
        view.Arrange(new Rect(PanelSize));
        Settle(view);

        var bitmap = new RenderTargetBitmap((int)PanelSize.Width, (int)PanelSize.Height, 96, 96, PixelFormats.Pbgra32);
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
}
