namespace Quiver.App.Services;

/// <summary>What the shortcut does to the window of Quiver.</summary>
internal enum ToggleAction
{
    /// <summary>Hidden in the notification area: it appears.</summary>
    Show,

    /// <summary>Minimized in the taskbar: it comes back as it was.</summary>
    Restore,

    /// <summary>Open behind another window: it comes forward.</summary>
    BringForward,

    /// <summary>Already the window the user is working in: it goes back to the notification area.</summary>
    Hide,
}

/// <summary>
/// One key for the window, as in the apps that live in the notification area: it brings Quiver where the user can use it,
/// and when Quiver is already there, it puts it away.
/// </summary>
internal static class WindowToggle
{
    /// <param name="visible">Shown, minimized or not.</param>
    /// <param name="minimized">In the taskbar only.</param>
    /// <param name="inFront">The window that has the focus.</param>
    public static ToggleAction Decide(bool visible, bool minimized, bool inFront) =>
        !visible ? ToggleAction.Show
        : minimized ? ToggleAction.Restore
        : inFront ? ToggleAction.Hide
        : ToggleAction.BringForward;
}
