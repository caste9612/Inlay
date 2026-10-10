namespace Inlay.App.Interop;

/// <summary>The corners of a window drawn by Inlay, as in Pulse on Brace.</summary>
internal static class WindowCorners
{
    /// <summary>
    /// Windows 11 rounds the frame by 4 px (DWMWCP_ROUNDSMALL), as the light corners of Brace; brace:Chrome.Dark colors its
    /// border. On Windows 10 the attribute does not exist, and the call does nothing.
    /// </summary>
    public static void MakeSmall(nint window)
    {
        var roundSmall = NativeMethods.DWMWCP_ROUNDSMALL;
        NativeMethods.DwmSetWindowAttribute(window, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref roundSmall, sizeof(int));
    }
}
