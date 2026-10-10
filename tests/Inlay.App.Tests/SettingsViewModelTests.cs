using Inlay.App.Services;
using Inlay.App.ViewModels;
using Inlay.Core;

namespace Inlay.App.Tests;

/// <summary>The settings, each applied the moment it changes, on a settings file of the test and a Windows made up.</summary>
public sealed class SettingsViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    private readonly FakeHost _host = new();
    private readonly FakeWindows _windows = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ANewShortcut_IsTriedAtOnce_AndKeptWhenWindowsGivesIt()
    {
        var settings = Settings();
        var viewModel = Open(settings);

        Assert.Null(viewModel.UseHotkey("Ctrl+Shift+Space"));

        Assert.Equal(["Ctrl+Shift+Space"], _host.Tried);
        Assert.Equal("Ctrl+Shift+Space", viewModel.Hotkey);
        Assert.False(viewModel.HasHotkeyProblem);
        Assert.Equal("Ctrl+Shift+Space", AppSettings.Load(settings.SavePath).Hotkey);
    }

    [Fact]
    public void AShortcutAlreadyTaken_LeavesTheOldOne_AndSaysWhy()
    {
        var settings = Settings();
        var viewModel = Open(settings);
        _host.Accepts = false;

        var problem = viewModel.UseHotkey("Alt+Q");

        Assert.Contains("Alt+Q", problem);
        Assert.Contains("Alt+E", problem);
        Assert.Equal(problem, viewModel.HotkeyProblem);
        Assert.Equal("Alt+E", viewModel.Hotkey);
        Assert.Equal("Alt+E", AppSettings.Load(settings.SavePath).Hotkey);
    }

    [Fact]
    public void TheSameShortcut_OrNoneAtAll_ChangesNothing()
    {
        var viewModel = Open(Settings());

        Assert.Null(viewModel.UseHotkey("alt+e"));
        Assert.NotNull(viewModel.UseHotkey("  "));

        Assert.Empty(_host.Tried);
        Assert.Equal("Alt+E", viewModel.Hotkey);
    }

    [Fact]
    public void AutostartAndSendTo_FollowTheSwitches_AndFallBackWhenWindowsRefuses()
    {
        _windows.AutostartEnabled = true;
        var viewModel = Open(Settings());
        Assert.True(viewModel.Autostart);
        Assert.False(viewModel.SendTo);

        viewModel.SendTo = true;
        Assert.True(_windows.SendToInstalled);
        Assert.Equal("", viewModel.WindowsProblem);

        _windows.Refuses = true;
        viewModel.Autostart = false;

        Assert.True(viewModel.Autostart);
        Assert.True(_windows.AutostartEnabled);
        Assert.Contains("avvio automatico", viewModel.WindowsProblem);
    }

    [Fact]
    public void TheFirstTime_TheWindowGreets_AndOpensOnTheArchives()
    {
        var viewModel = Open(Settings());

        Assert.True(viewModel.IsWelcome);
        Assert.Equal(SettingsSection.Archives, viewModel.Section);
        Assert.Equal("Benvenuto in Inlay", viewModel.Heading);

        _host.CurrentRoot = _root;
        var later = Open(Settings(), SettingsSection.Hotkey);
        Assert.False(later.IsWelcome);
        Assert.Equal("Impostazioni", later.Heading);
    }

    private SettingsViewModel Open(AppSettings settings, SettingsSection section = SettingsSection.Archives) =>
        new(settings, _host, [], _windows, section);

    private AppSettings Settings() => AppSettings.Load(Path.Combine(_root, "settings.json"));

    private sealed class FakeHost : ISettingsHost
    {
        public string? CurrentRoot { get; set; }

        public bool Accepts { get; set; } = true;

        public List<string> Tried { get; } = [];

        public void Use(string root) => CurrentRoot = root;

        public void Release()
        {
        }

        public bool TryUseHotkey(string shortcut)
        {
            Tried.Add(shortcut);
            return Accepts;
        }
    }

    private sealed class FakeWindows : IWindowsIntegration
    {
        public bool Refuses { get; set; }

        public bool AutostartEnabled { get; set; }

        public bool SendToInstalled { get; set; }

        public bool TrySetAutostart(bool enabled)
        {
            if (Refuses)
                return false;
            AutostartEnabled = enabled;
            return true;
        }

        public bool TrySetSendTo(bool installed)
        {
            if (Refuses)
                return false;
            SendToInstalled = installed;
            return true;
        }
    }
}
