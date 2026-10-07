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
            // Installed: Quiver starts with Windows, as an app of the tray should; its menu turns that off.
            .OnAfterInstallFastCallback(_ => Autostart.TrySet(true))
            // Removed: nothing left in the startup of Windows.
            .OnBeforeUninstallFastCallback(_ => Autostart.TrySet(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
