using CommunityToolkit.Mvvm.ComponentModel;
using Inlay.App.Services;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>What the settings ask of the app, beyond the archives.</summary>
public interface ISettingsHost : IArchiveHost
{
    /// <summary>Registers the shortcut now. False when another program holds it: the one in use stays.</summary>
    bool TryUseHotkey(string shortcut);
}

/// <summary>The two things Inlay sets in Windows: its start with the session, and its place in the «Send to» menu.</summary>
public interface IWindowsIntegration
{
    bool AutostartEnabled { get; }

    /// <returns>False when Windows refused the change.</returns>
    bool TrySetAutostart(bool enabled);

    bool SendToInstalled { get; }

    /// <returns>False when the shortcut could not be written or removed.</returns>
    bool TrySetSendTo(bool installed);
}

/// <summary>The pages of the window of the settings, in the order of its sidebar.</summary>
public enum SettingsSection
{
    Archives,
    Hotkey,
    Windows,
    Claude,
    About,
}

/// <summary>
/// The settings of this PC, each applied the moment it changes: the archives (as the vaults of Obsidian), the shortcut
/// that opens the panel, what Inlay sets in Windows and the prompt of the chats of Claude; and the page «About», with the
/// version and its updates. The first
/// time, before any archive, the window greets and only the archives matter.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ISettingsHost _host;
    private readonly IWindowsIntegration _windows;
    private bool _applying;

    [ObservableProperty]
    private SettingsSection _section;

    /// <summary>The shortcut in use, written like "Alt+E".</summary>
    [ObservableProperty]
    private string _hotkey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHotkeyProblem))]
    private string? _hotkeyProblem;

    [ObservableProperty]
    private bool _autostart;

    [ObservableProperty]
    private bool _sendTo;

    /// <summary>The first message of the chats of Claude asked about a folder; empty for a chat that starts empty.</summary>
    [ObservableProperty]
    private string _claudePrompt;

    [ObservableProperty]
    private string _windowsProblem = "";

    /// <param name="updates">The version and its updates, kept by the app, for the page «About».</param>
    internal SettingsViewModel(AppSettings settings, ISettingsHost host, IReadOnlyList<ReservedFolder> reserved, IWindowsIntegration windows,
        UpdatesViewModel updates, SettingsSection section = SettingsSection.Archives)
    {
        _settings = settings;
        _host = host;
        _windows = windows;
        Updates = updates;
        Archives = new ArchivesViewModel(settings, host, reserved);
        _section = section;
        _hotkey = settings.Hotkey;
        _autostart = windows.AutostartEnabled;
        _sendTo = windows.SendToInstalled;
        _claudePrompt = settings.ClaudePrompt ?? "";
    }

    partial void OnClaudePromptChanged(string value)
    {
        _settings.ClaudePrompt = value.Trim();
        Save();
    }

    /// <summary>The prompt Inlay comes with, in place of the one written in the field.</summary>
    public void ResetClaudePrompt() => ClaudePrompt = AppSettings.DefaultClaudePrompt;

    public ArchivesViewModel Archives { get; }

    public UpdatesViewModel Updates { get; }

    /// <summary>The first time, before any archive: the window greets, and the other pages wait.</summary>
    public bool IsWelcome => Archives.IsWelcome;

    /// <summary>What the title bar of the window says.</summary>
    public string Heading => IsWelcome ? "Welcome to Inlay" : "Settings";

    public bool HasHotkeyProblem => HotkeyProblem is not null;

    /// <summary>A combination pressed in the field: tried at once, kept only if Windows gives it to Inlay.</summary>
    /// <returns>Why it was not taken, shown under the field; null when it is in use now.</returns>
    public string? UseHotkey(string shortcut)
    {
        shortcut = shortcut.Trim();
        if (shortcut.Length == 0)
            return HotkeyProblem = "Press a combination with Ctrl, Alt or Win and a key.";
        if (string.Equals(shortcut, _settings.Hotkey, StringComparison.OrdinalIgnoreCase))
        {
            HotkeyProblem = null;
            return null;
        }
        if (!_host.TryUseHotkey(shortcut))
            return HotkeyProblem = $"{shortcut} isn't available: another program may be using it. {_settings.Hotkey} stays.";
        _settings.Hotkey = shortcut;
        Save();
        Hotkey = shortcut;
        HotkeyProblem = null;
        return null;
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_applying)
            return;
        if (_windows.TrySetAutostart(value))
        {
            WindowsProblem = "";
            return;
        }
        WindowsProblem = "Windows didn't allow changing the startup.";
        Revert(() => Autostart = _windows.AutostartEnabled);
    }

    partial void OnSendToChanged(bool value)
    {
        if (_applying)
            return;
        if (_windows.TrySetSendTo(value))
        {
            WindowsProblem = "";
            return;
        }
        WindowsProblem = "Couldn't change the Send to menu of Explorer.";
        Revert(() => SendTo = _windows.SendToInstalled);
    }

    /// <summary>Puts a switch back to what Windows says, without trying to apply that again.</summary>
    private void Revert(Action restore)
    {
        _applying = true;
        try
        {
            restore();
        }
        finally
        {
            _applying = false;
        }
    }

    private void Save()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save the settings: {e.Message}");
            WindowsProblem = $"Couldn't save the settings: {e.Message}";
        }
    }
}
