namespace Quiver.App.Services;

/// <summary>Commands a second Quiver.exe passes to the running one, for example from a GlazeWM keybinding.</summary>
internal static class Commands
{
    public const string Toggle = "toggle";
    public const string Show = "show";
    public const string Hide = "hide";
    public const string Exit = "exit";
}

/// <param name="PreviewOutput">Diagnostics: draw the panel into this PNG file and exit.</param>
/// <param name="PreviewPrompt">Diagnostics: draw the panel with a confirmation open (moving the first tile to the Recycle Bin).</param>
/// <param name="PreviewLinkPrompt">Diagnostics: draw the panel asking for a new shortcut, with a name it cannot use.</param>
/// <param name="PreviewInbox">Diagnostics: draw "Da smistare" instead of a topic.</param>
/// <param name="PreviewArchives">Diagnostics: draw the window of the archives; with <paramref name="PreviewWelcome"/>, as the first time.</param>
/// <param name="DesktopStandIn">
/// For trials and previews: a folder shown as the Desktop in "Da smistare" in place of the real one, so that a demo archive
/// never takes the user's own files.
/// </param>
internal sealed record CommandLineOptions(
    string? Command, string? ArchiveRoot, string? PreviewOutput, string? PreviewTopic, string? PreviewQuery, bool PreviewPrompt,
    bool PreviewLinkPrompt, bool PreviewInbox, string? DesktopStandIn, bool PreviewArchives = false, bool PreviewWelcome = false)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string? command = null, archive = null, preview = null, topic = null, query = null, desktop = null;
        bool prompt = false, linkPrompt = false, inbox = false, archives = false, welcome = false;
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
                case "--inbox": inbox = true; break;
                case "--desktop" when hasValue: desktop = args[++i]; break;
                case "--archives": archives = true; break;
                case "--welcome": welcome = true; break;
            }
        }
        return new CommandLineOptions(command, archive, preview, topic, query, prompt, linkPrompt, inbox, desktop, archives, welcome);
    }
}
