using Quiver.App.Services;
using Velopack;

namespace Quiver.App;

/// <summary>
/// Where Quiver starts. Velopack, the installer, comes first: when Windows runs Quiver to install, update or remove it,
/// Velopack does its part and ends the process before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build()
            // Installed: Quiver starts with Windows, as an app of the tray should, and Explorer gets «Invia a Quiver».
            .OnAfterInstallFastCallback(_ =>
            {
                Autostart.TrySet(true);
                SendToShortcut.TryInstall();
            })
            // Updated: the shortcut of «Invia a», if the user kept it, points to the Quiver.exe of now.
            .OnAfterUpdateFastCallback(_ =>
            {
                if (SendToShortcut.IsPresent())
                    SendToShortcut.TryInstall();
            })
            // Removed: nothing left in the startup of Windows, nor in the menu of Explorer.
            .OnBeforeUninstallFastCallback(_ =>
            {
                Autostart.TrySet(false);
                SendToShortcut.TryRemove();
            })
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
