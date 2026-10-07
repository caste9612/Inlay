using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Quiver.App.Interop;

namespace Quiver.App.Services;

/// <summary>Icon in the notification area: a left click opens the panel, a right click shows the menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly System.Drawing.Icon _icon;

    public TrayIcon(string shortcut, Action showPanel, Action openArchiveFolder, Action changeArchive, Action exit)
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateItem("Apri Quiver", showPanel, shortcut));
        menu.Items.Add(CreateItem("Apri l'archivio in Explorer", openArchiveFolder));
        menu.Items.Add(CreateItem("Cambia archivio…", changeArchive));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateAutostartItem());
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateItem("Esci", exit));

        _icon = LoadIcon();
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = $"Quiver ({shortcut})",
            Icon = _icon,
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _taskbarIcon.TrayLeftMouseUp += (_, _) => showPanel();
        // false: otherwise H.NotifyIcon turns on Windows "efficiency mode", which would slow the panel down.
        _taskbarIcon.ForceCreate(false);
    }

    public void ShowWarning(string message) => _taskbarIcon.ShowNotification("Quiver", message, NotificationIcon.Warning);

    public void Dispose()
    {
        _taskbarIcon.Dispose();
        _icon.Dispose();
    }

    /// <summary>The notification area uses small icons: 16 px at 100% scaling, 24 px at 150%, and so on.</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        var size = (int)Math.Round(16 * NativeMethods.GetDpiForSystem() / 96.0);
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/quiver.ico"))!.Stream;
        return new System.Drawing.Icon(stream, size, size);
    }

    private static MenuItem CreateAutostartItem()
    {
        var item = new MenuItem { Header = "Avvia con Windows", IsCheckable = true, IsChecked = Autostart.IsEnabled };
        item.Click += (_, _) =>
        {
            if (!Autostart.TrySet(item.IsChecked))
                item.IsChecked = Autostart.IsEnabled;
        };
        return item;
    }

    private static MenuItem CreateItem(string header, Action action, string? shortcut = null)
    {
        var item = new MenuItem { Header = header, InputGestureText = shortcut ?? "" };
        item.Click += (_, _) => action();
        return item;
    }
}
