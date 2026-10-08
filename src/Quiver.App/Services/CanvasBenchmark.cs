using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Quiver.App.ViewModels;
using Quiver.App.Views;
using Quiver.Core;

namespace Quiver.App.Services;

/// <summary>
/// Diagnostics: how smoothly the canvas moves on this PC. Opens the canvas of an archive in a window as big as the screen,
/// moves the camera along the same path every time (closer, across, farther, back and forth) and writes how long each
/// frame took. Only reads the archive. The window does not take the focus, and as a tool window GlazeWM leaves it alone.
/// Example: Quiver.exe --archive E:\Archivio --benchmark-canvas prova.txt
/// </summary>
internal static class CanvasBenchmark
{
    // Each part of the path lasts this long.
    private const double PartSeconds = 2;
    private static readonly string[] Parts = ["avvicinamento", "spostamento da vicino", "allontanamento", "zoom avanti e indietro"];

    public static void Start(CommandLineOptions options, Action<int> done)
    {
        var output = options.BenchmarkOutput ?? throw new ArgumentException("Manca il file dei risultati.");
        var root = options.ArchiveRoot ?? throw new ArgumentException("--benchmark-canvas richiede anche --archive <cartella>.");
        IReadOnlyList<InboxSource> sources = options.DesktopStandIn is { } desktop ? [new InboxSource("Desktop", [desktop])] : [];
        var recent = RecentDestinations.Load(Path.Combine(Path.GetTempPath(), "quiver-benchmark-recenti.json"));
        var opening = Stopwatch.StartNew();
        var viewModel = new MainViewModel(root, live: false, inboxSources: sources, recent: recent) { IsCanvasShown = true };
        var view = new PanelView { DataContext = viewModel };
        var area = SystemParameters.WorkArea;
        var window = new Window
        {
            Title = "Quiver: prova della tela",
            Content = view,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = area.Left,
            Top = area.Top,
            Width = area.Width,
            Height = area.Height,
        };
        var run = new Run(viewModel, view, window, output, root, done, opening);
        window.ContentRendered += (_, _) => run.Begin();
        window.Show();
    }

    /// <param name="opening">Running since the canvas was asked for: until the first frame, how long it takes to open.</param>
    private sealed class Run(MainViewModel viewModel, PanelView view, Window window, string output, string root, Action<int> done, Stopwatch opening)
    {
        // A frame this long is told one by one, with what happened before it.
        private const double SlowFrame = 100;

        private readonly Stopwatch _clock = new();
        private readonly List<(int Part, double Milliseconds)> _frames = [];
        private readonly List<string> _slow = [];
        private CanvasLevel _lastLevel;
        private int _lastSleeping;
        private int[] _lastCollections = new int[3];
        private CanvasCamera _all;
        private CanvasCamera _near;
        private CanvasCamera _far;
        private Size _view;
        private TimeSpan? _lastFrame;
        private long _memoryBefore;

        private TimeSpan _opened;

        public void Begin()
        {
            _opened = opening.Elapsed;
            var surface = view.CanvasSurface;
            _view = surface.RenderSize;
            var world = surface.WorldBounds;
            _all = CanvasCamera.Fit(world, _view);
            // Close up in the middle of the canvas, and far enough to see more than all of it.
            _near = new CanvasCamera(1, new Point(world.X + world.Width / 2 - _view.Width / 2, world.Y + world.Height / 2 - _view.Height / 2));
            _far = _all.ZoomAt(new Point(_view.Width / 2, _view.Height / 2), 0.5);
            viewModel.Canvas.Camera = _all;
            _lastLevel = viewModel.Canvas.Level;
            _lastSleeping = surface.SleepingCount;
            _lastCollections = Collections();
            _memoryBefore = Process.GetCurrentProcess().WorkingSet64;
            _clock.Start();
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            var time = (e as RenderingEventArgs)?.RenderingTime ?? _clock.Elapsed;
            // The same frame can be reported twice: only a new one counts.
            if (_lastFrame is { } last && time == last)
                return;
            var seconds = _clock.Elapsed.TotalSeconds;
            var part = (int)(seconds / PartSeconds);
            if (_lastFrame is { } previous && part < Parts.Length)
            {
                var milliseconds = (time - previous).TotalMilliseconds;
                _frames.Add((part, milliseconds));
                if (milliseconds >= SlowFrame)
                    _slow.Add(SlowLine(part, milliseconds));
            }
            NoteState();
            _lastFrame = time;
            if (part >= Parts.Length)
            {
                CompositionTarget.Rendering -= OnFrame;
                Finish();
                return;
            }
            var t = seconds / PartSeconds - part;
            viewModel.Canvas.Camera = part switch
            {
                0 => _all.Toward(_near, t, _view),
                1 => _near.Pan(new Vector(-t * _view.Width, -t * _view.Height / 3)),
                2 => _near.Toward(_far, t, _view),
                _ => _far.ZoomAt(new Point(_view.Width / 2, _view.Height / 2), Math.Pow(1 / _far.Zoom, 0.5 - 0.5 * Math.Cos(t * 2 * Math.PI))),
            };
        }

        private void Finish()
        {
            var text = new StringBuilder();
            var dpi = VisualTreeHelper.GetDpi(window);
            var pixels = new Size(_view.Width * dpi.DpiScaleX, _view.Height * dpi.DpiScaleY);
            text.AppendLine(CultureInfo.InvariantCulture, $"Quiver: prova della tela, {DateTime.Now:dd/MM/yyyy HH:mm}");
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Tela {pixels.Width:N0} x {pixels.Height:N0} pixel, scala {dpi.DpiScaleX * 100:N0} %, livello della scheda grafica per WPF {RenderCapability.Tier >> 16}");
            var zones = viewModel.Canvas.AllZones().Count();
            var tiles = viewModel.Canvas.AllTiles().ToList();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Archivio {root}: {zones} zone, {tiles.Count} riquadri, {tiles.Sum(tile => tile.Count)} elementi");
            text.AppendLine(CultureInfo.InvariantCulture, $"Apertura della tela, letta e disegnata: {_opened.TotalSeconds:N2} s");
            for (var part = 0; part < Parts.Length; part++)
                text.AppendLine(Line(Parts[part], _frames.Where(frame => frame.Part == part).Select(frame => frame.Milliseconds).ToList()));
            text.AppendLine(Line("in tutto", _frames.Select(frame => frame.Milliseconds).ToList()));
            text.AppendLine(CultureInfo.InvariantCulture, $"Righe ancora da creare alla fine: {view.CanvasSurface.SleepingCount} liste su {tiles.Count}");
            foreach (var slow in _slow)
                text.AppendLine(slow);
            text.AppendLine(CultureInfo.InvariantCulture,
                $"Memoria: {_memoryBefore / 1048576.0:N0} MB all'inizio, {Process.GetCurrentProcess().WorkingSet64 / 1048576.0:N0} MB alla fine");
            File.WriteAllText(output, text.ToString());
            window.Close();
            viewModel.Dispose();
            done(0);
        }

        /// <summary>"Fotogramma lento: 120 ms, allontanamento, zoom 64 %, da vicino a medio, 3 liste svegliate, raccolte 1/0/0".</summary>
        private string SlowLine(int part, double milliseconds)
        {
            var level = viewModel.Canvas.Level;
            var collections = Collections();
            var changes = level == _lastLevel ? $"livello {Name(level)}" : $"da {Name(_lastLevel)} a {Name(level)}";
            return string.Create(CultureInfo.InvariantCulture,
                $"Fotogramma lento: {milliseconds:N0} ms, {Parts[part]}, zoom {viewModel.Canvas.Camera.Zoom * 100:N0} %, {changes}, " +
                $"{_lastSleeping - view.CanvasSurface.SleepingCount} liste svegliate, raccolte della memoria {collections[0] - _lastCollections[0]}/{collections[1] - _lastCollections[1]}/{collections[2] - _lastCollections[2]}");

            static string Name(CanvasLevel level) => level switch
            {
                CanvasLevel.Far => "lontano",
                CanvasLevel.Middle => "medio",
                _ => "vicino",
            };
        }

        private void NoteState()
        {
            _lastLevel = viewModel.Canvas.Level;
            _lastSleeping = view.CanvasSurface.SleepingCount;
            _lastCollections = Collections();
        }

        private static int[] Collections() => [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

        /// <summary>"avvicinamento: 58 fotogrammi al secondo, metà entro 16,7 ms, il 95 % entro 18,2 ms, 0 oltre 33 ms".</summary>
        private static string Line(string name, List<double> milliseconds)
        {
            if (milliseconds.Count == 0)
                return $"{name}: nessun fotogramma";
            milliseconds.Sort();
            double At(double share) => milliseconds[Math.Min(milliseconds.Count - 1, (int)(share * milliseconds.Count))];
            var perSecond = 1000 / milliseconds.Average();
            return string.Create(CultureInfo.InvariantCulture,
                $"{name}: {perSecond:N0} fotogrammi al secondo, metà entro {At(0.5):N1} ms, il 95 % entro {At(0.95):N1} ms, il peggiore {milliseconds[^1]:N1} ms, {milliseconds.Count(ms => ms > 33.4)} oltre 33 ms");
        }
    }
}
