using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Inlay.App.Interop;

namespace Inlay.App.Services;

/// <summary>Icon in the notification area: a left click opens the panel, a right click shows the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly System.Drawing.Icon _icon;
    private readonly MenuItem _open;
    private readonly MenuItem _restartToUpdate;

    /// <param name="isTopmost">Whether the window stays above the others now: the pin of its title bar.</param>
    /// <param name="setTopmost">Changes it, as the pin does.</param>
    /// <param name="checkForUpdates">«Check for updates»; null for an Inlay that cannot update itself, as one run from the code.</param>
    /// <param name="restartToUpdate">«Restart to update», shown once a new version is ready (<see cref="ShowUpdateReady"/>).</param>
    public TrayIcon(string shortcut, Action showPanel, Action openArchiveFolder, Action showArchives, Action showSettings,
        Func<bool> isTopmost, Action<bool> setTopmost, Action? checkForUpdates, Action restartToUpdate, Action exit)
    {
        var menu = new ContextMenu();
        menu.Items.Add(_open = CreateItem("Open Inlay", showPanel, shortcut));
        menu.Items.Add(CreateItem("Open the vault in Explorer", openArchiveFolder));
        // As in Hive and Pulse: the pin of the title bar, here too.
        var topmost = new MenuItem { Header = "Always on top", IsCheckable = true };
        topmost.Click += (_, _) => setTopmost(topmost.IsChecked);
        menu.Items.Add(topmost);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Vaults…", showArchives));
        menu.Items.Add(CreateItem("Settings…", showSettings));
        menu.Items.Add(new Separator());
        _restartToUpdate = CreateItem("Restart to update", restartToUpdate);
        _restartToUpdate.Visibility = Visibility.Collapsed;
        menu.Items.Add(_restartToUpdate);
        if (checkForUpdates is not null)
            menu.Items.Add(CreateItem("Check for updates", checkForUpdates));
        menu.Items.Add(CreateItem("Quit", exit));
        // The pin may have changed in the title bar since the menu was last open.
        menu.Opened += (_, _) => topmost.IsChecked = isTopmost();

        _icon = LoadIcon();
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = $"Inlay ({shortcut})",
            Icon = _icon,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _taskbarIcon.TrayLeftMouseUp += (_, _) => showPanel();
        // false: otherwise H.NotifyIcon turns on Windows "efficiency mode", which would slow the panel down.
        _taskbarIcon.ForceCreate(false);
    }

    public void ShowWarning(string message) => _taskbarIcon.ShowNotification("Inlay", message, NotificationIcon.Warning);

    public void ShowInfo(string message) => _taskbarIcon.ShowNotification("Inlay", message, NotificationIcon.Info);

    /// <summary>A new version is downloaded: the menu offers to restart into it.</summary>
    public void ShowUpdateReady(string version)
    {
        _restartToUpdate.Header = $"Restart to update to Inlay {version}";
        _restartToUpdate.Visibility = Visibility.Visible;
    }

    /// <summary>The shortcut changed in the settings: the tooltip and the menu say the new one.</summary>
    public void UpdateShortcut(string shortcut)
    {
        _taskbarIcon.ToolTipText = $"Inlay ({shortcut})";
        _open.InputGestureText = shortcut;
    }

    public void Dispose()
    {
        _taskbarIcon.Dispose();
        _icon.Dispose();
    }

    /// <summary>The notification area uses small icons: 16 px at 100% scaling, 24 px at 150%, and so on.</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var size = (int)Math.Round(16 * NativeMethods.GetDpiForSystem() / 96.0);
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/inlay.ico"))!.Stream;
        return new System.Drawing.Icon(stream, size, size);
    }

    private static MenuItem CreateItem(string header, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcut ?? "" };
        item.Click += (_, _) => action();
        return item;
    }
}
