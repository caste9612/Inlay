using System.Diagnostics;
using System.Text;

namespace Quiver.App.Interop;

/// <summary>Which window is where: for the log, when the focus or a drag goes somewhere unexpected.</summary>
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

    /// <summary>Whether the mouse pointer is on the window's rectangle, whatever window is drawn there at the moment.</summary>
    public static bool IsCursorOn(nint window)
    {
        if (!NativeMethods.GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(window, out var rect))
            return false;
        return cursor.X >= rect.Left && cursor.X < rect.Right && cursor.Y >= rect.Top && cursor.Y < rect.Bottom;
    }

    /// <summary>The window under the mouse pointer, for the log.</summary>
    public static string UnderCursor() =>
        NativeMethods.GetCursorPos(out var cursor) ? Describe(NativeMethods.WindowFromPoint(cursor)) : "?";
}
