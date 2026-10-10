using System.Diagnostics;
using System.Text;

namespace Inlay.App.Interop;

/// <summary>Which window is which: for the log, when the focus goes somewhere unexpected.</summary>
internal static class WindowInfo
{
    /// <summary>Like: ms-teams "Calendar | Microsoft Teams" (TeamsWebView).</summary>
    public static string Describe(nint window)
    {
        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var title = new char[256];
        var length = NativeMethods.GetWindowText(window, title, title.Length);
        var className = new StringBuilder(256);
        NativeMethods.GetClassName(window, className, className.Capacity);
        string process;
        try
        {
            using var owner = Process.GetProcessById((int)processId);
            process = owner.ProcessName;
        }
        catch (ArgumentException)
        {
            process = "?";
        }
        return $"{process} \"{new string(title, 0, length)}\" ({className})";
    }
}
