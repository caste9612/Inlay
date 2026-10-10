namespace Inlay.App.Services;

/// <summary>Commands a second Inlay.exe passes to the running one, for example from a GlazeWM keybinding.</summary>
internal static class Commands
{
    public const string Toggle = "toggle";
    public const string Show = "show";
    public const string Hide = "hide";
    public const string Exit = "exit";

    /// <summary>Files to send into the archive, from «Invia a Inlay» in Explorer: the panel opens asking where.</summary>
    public const string Sort = "sort";
}

/// <param name="PreviewOutput">Diagnostics: draw the panel into this PNG file and exit.</param>
/// <param name="PreviewPrompt">Diagnostics: draw the panel with a confirmation open (moving the first tile to the Recycle Bin).</param>
/// <param name="PreviewLinkPrompt">Diagnostics: draw the panel asking for a new shortcut, with a name it cannot use.</param>
/// <param name="PreviewSettings">Diagnostics: draw the window of the settings; with <paramref name="PreviewWelcome"/>, as the first time.</param>
/// <param name="PreviewPicker">Diagnostics: draw the panel asking where the first item of the board goes.</param>
/// <param name="PreviewNotice">Diagnostics: draw the toasts as after a move, with «Annulla» and «Mostra», and after an error.</param>
/// <param name="PreviewSection">Diagnostics: the page of the settings to draw: archives, hotkey or windows.</param>
internal sealed record CommandLineOptions(
    string? Command, string? ArchiveRoot, string? PreviewOutput, string? PreviewTopic, string? PreviewQuery, bool PreviewPrompt,
    bool PreviewLinkPrompt, bool PreviewSettings = false, bool PreviewWelcome = false,
    bool PreviewPicker = false, bool PreviewNotice = false, string? PreviewSection = null)
{
    /// <summary>The files handed over by «Invia a Inlay» in Explorer: everything after --sort.</summary>
    public IReadOnlyList<string> SortPaths { get; init; } = [];

    /// <summary>Diagnostics: draw the canvas instead of the board, shown whole unless <see cref="PreviewZoom"/> says how close.</summary>
    public bool PreviewCanvas { get; init; }

    /// <summary>Diagnostics: the zoom of the canvas, around the middle of the view (0.5 for 50 %); with --topic, around its zone.</summary>
    public double? PreviewZoom { get; init; }

    /// <summary>Diagnostics: move the canvas of the archive on screen for a few seconds and write here how smooth it was.</summary>
    public string? BenchmarkOutput { get; init; }

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string? command = null, archive = null, preview = null, topic = null, query = null, section = null, benchmark = null;
        bool prompt = false, linkPrompt = false, settings = false, welcome = false, picker = false, notice = false, canvas = false;
        double? zoom = null;
        var sort = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            var hasValue = i + 1 < args.Count;
            switch (args[i].ToLowerInvariant())
            {
                case "--toggle": command = Commands.Toggle; break;
                case "--show": command = Commands.Show; break;
                case "--hide": command = Commands.Hide; break;
                case "--exit": command = Commands.Exit; break;
                case "--archive" when hasValue: archive = args[++i]; break;
                case "--render-preview" when hasValue: preview = args[++i]; break;
                case "--topic" when hasValue: topic = args[++i]; break;
                case "--query" when hasValue: query = args[++i]; break;
                case "--prompt": prompt = true; break;
                case "--prompt-link": linkPrompt = true; break;
                case "--prompt-move": picker = true; break;
                case "--notice": notice = true; break;
                case "--archives":
                case "--settings": settings = true; break;
                case "--section" when hasValue: section = args[++i]; settings = true; break;
                case "--welcome": welcome = true; break;
                case "--canvas": canvas = true; break;
                case "--benchmark-canvas" when hasValue: benchmark = args[++i]; break;
                case "--zoom" when hasValue:
                    zoom = double.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                        ? value
                        : null;
                    canvas = true;
                    break;
                case "--sort":
                    // Explorer puts the chosen files after the arguments of the shortcut: all that follows is a path.
                    for (i++; i < args.Count; i++)
                        sort.Add(args[i]);
                    break;
            }
        }
        return new CommandLineOptions(command, archive, preview, topic, query, prompt, linkPrompt, settings, welcome, picker, notice, section)
        {
            SortPaths = sort,
            PreviewCanvas = canvas,
            PreviewZoom = zoom,
            BenchmarkOutput = benchmark,
        };
    }
}
