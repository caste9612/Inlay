using System.Diagnostics;
using System.Text;

namespace Inlay.App.Services;

/// <summary>
/// Starts Claude Code in a folder of the vault: in Windows Terminal when it is installed, otherwise in a PowerShell window
/// of its own. The shell stays open when Claude ends. With a prompt, the chat starts with it, as `claude "…"` does.
/// </summary>
internal static class ClaudeLauncher
{
    /// <summary>The program of Claude Code, as its installer leaves it on the PATH (`claude.exe`) or npm (`claude.cmd`).</summary>
    private static readonly string[] ClaudeNames = ["claude.exe", "claude.cmd"];

    /// <summary>Whether the `claude` command can be found: on the PATH of Inlay, or where its installer puts it.</summary>
    public static bool IsInstalled() =>
        ClaudeNames.Any(name => FindOnPath(name) is not null) ||
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"));

    /// <summary>Opens the window with Claude Code started in <paramref name="folder"/>.</summary>
    /// <param name="prompt">The first message of the chat; null or blank for an empty chat.</param>
    public static void Start(string folder, string? prompt)
    {
        // By name, not by path: the PowerShell of the Store sits in WindowsApps, where its own file can't be started directly.
        var shell = FindOnPath("pwsh.exe") is not null ? "pwsh.exe" : "powershell.exe";
        var info = StartInfo(folder, prompt, shell, FindOnPath("wt.exe"));
        Process.Start(info)?.Dispose();
    }

    /// <summary>How the window is started: Windows Terminal with a tab in the folder, or the shell alone.</summary>
    internal static ProcessStartInfo StartInfo(string folder, string? prompt, string shell, string? terminal)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Command(prompt)));
        string[] shellArguments = ["-NoLogo", "-NoExit", "-EncodedCommand", encoded];
        if (terminal is null)
        {
            var alone = new ProcessStartInfo(shell) { WorkingDirectory = folder, UseShellExecute = false };
            foreach (var argument in shellArguments)
                alone.ArgumentList.Add(argument);
            return alone;
        }

        // Windows Terminal reads `;` as the start of another of its commands: in the folder and the title it is escaped.
        var info = new ProcessStartInfo(terminal) { UseShellExecute = false };
        info.ArgumentList.Add("--title");
        info.ArgumentList.Add(EscapeForTerminal($"Claude · {Path.GetFileName(folder.TrimEnd('\\', '/'))}"));
        info.ArgumentList.Add("-d");
        info.ArgumentList.Add(EscapeForTerminal(folder));
        info.ArgumentList.Add(shell);
        foreach (var argument in shellArguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    /// <summary>The PowerShell command: `claude`, or `claude '…'` with the prompt quoted for PowerShell.</summary>
    internal static string Command(string? prompt) =>
        string.IsNullOrWhiteSpace(prompt) ? "claude" : $"claude '{prompt.Trim().Replace("'", "''")}'";

    internal static string EscapeForTerminal(string text) => text.Replace(";", "\\;");

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(folder => Path.Combine(folder, name))
            .FirstOrDefault(File.Exists);
}
