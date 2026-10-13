using System.Text;
using Inlay.App.Services;

namespace Inlay.App.Tests;

public sealed class ClaudeLauncherTests
{
    [Fact]
    public void Command_WithoutPrompt_IsClaudeAlone()
    {
        Assert.Equal("claude", ClaudeLauncher.Command(null));
        Assert.Equal("claude", ClaudeLauncher.Command("   "));
    }

    [Fact]
    public void Command_QuotesThePrompt_ForPowerShell()
    {
        Assert.Equal("claude 'Read the files'", ClaudeLauncher.Command("  Read the files "));
        // Inside single quotes PowerShell takes everything literally, except a quote, which is doubled.
        Assert.Equal("claude 'What''s in it? $HOME `n'", ClaudeLauncher.Command("What's in it? $HOME `n"));
    }

    [Fact]
    public void WithWindowsTerminal_ATabStartsInTheFolder_WithTheShellAndTheEncodedCommand()
    {
        var info = ClaudeLauncher.StartInfo(@"D:\Vault\Work; old", "Read it", "pwsh.exe", @"C:\wt.exe");

        Assert.Equal(@"C:\wt.exe", info.FileName);
        Assert.False(info.UseShellExecute);
        var arguments = info.ArgumentList.ToList();
        // `;` starts another command of Windows Terminal: in the folder and the title it is escaped.
        Assert.Equal(["--title", @"Claude · Work\; old", "-d", @"D:\Vault\Work\; old", "pwsh.exe", "-NoLogo", "-NoExit", "-EncodedCommand"],
            arguments.Take(8));
        Assert.Equal("claude 'Read it'", Encoding.Unicode.GetString(Convert.FromBase64String(arguments[8])));
    }

    [Fact]
    public void WithoutWindowsTerminal_TheShellOpensAWindowOfItsOwn_InTheFolder()
    {
        var info = ClaudeLauncher.StartInfo(@"D:\Vault\Work", null, "powershell.exe", terminal: null);

        Assert.Equal("powershell.exe", info.FileName);
        Assert.Equal(@"D:\Vault\Work", info.WorkingDirectory);
        Assert.Equal(["-NoLogo", "-NoExit", "-EncodedCommand"], info.ArgumentList.Take(3));
        Assert.Equal("claude", Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList[3])));
    }

    [Fact]
    public void ThePrompt_StartsAsInlaysOwn_AndAnEmptyOneStaysEmpty()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, """{ "hotkey": "Alt+E" }""");
            Assert.Equal(AppSettings.DefaultClaudePrompt, AppSettings.Load(path).ClaudePrompt);

            var settings = new AppSettings { SavePath = path, ClaudePrompt = "" };
            settings.Save();
            Assert.Equal("", AppSettings.Load(path).ClaudePrompt);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
