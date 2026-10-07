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
/// <param name="PreviewPrompt">Diagnostics: draw the panel with the "new tile" question open.</param>
internal sealed record CommandLineOptions(
    string? Command, string? ArchiveRoot, string? PreviewOutput, string? PreviewTopic, string? PreviewQuery, bool PreviewPrompt)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        string? command = null, archive = null, preview = null, topic = null, query = null;
        var prompt = false;
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
            }
        }
        return new CommandLineOptions(command, archive, preview, topic, query, prompt);
    }
}
