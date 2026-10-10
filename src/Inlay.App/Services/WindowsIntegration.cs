using Inlay.App.ViewModels;

namespace Inlay.App.Services;

/// <summary>What Inlay sets in Windows for real: the Run key of the user and the shortcut in the «Send to» menu.</summary>
internal sealed class WindowsIntegration : IWindowsIntegration
{
    public bool AutostartEnabled => Autostart.IsEnabled;

    public bool TrySetAutostart(bool enabled) => Autostart.TrySet(enabled);

    public bool SendToInstalled => SendToShortcut.IsInstalled();

    public bool TrySetSendTo(bool installed) => installed ? SendToShortcut.TryInstall() : SendToShortcut.TryRemove();
}
