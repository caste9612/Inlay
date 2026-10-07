using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Quiver.App.Interop;
using Quiver.App.Services;
using Quiver.App.ViewModels;

namespace Quiver.App;

/// <summary>
/// The panel: a borderless window created once at startup and only shown and hidden afterwards,
/// so it appears instantly. It hides itself as soon as it loses focus or something is opened.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly MainViewModel _viewModel;
    private nint _hwnd;
    private nint _previousWindow;

    public PanelWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        View.CloseRequested += (_, _) => HidePanel("Esc", returnFocus: true);
        View.DragLeftPanel += (_, _) => HidePanel("drag left the panel");
        viewModel.ItemOpened += (_, _) => HidePanel("item opened");
        viewModel.FileOperationCompleted += (_, _) =>
        {
            if (IsVisible)
                BringToFront();
        };
        // The dialogs of a file operation (progress, name conflicts) take the focus: the panel stays meanwhile.
        Deactivated += (_, _) =>
        {
            Log.Write($"Panel deactivated, now in front: {DescribeForegroundWindow()}");
            if (!_viewModel.IsBusy)
                HidePanel("lost the focus");
        };
        SourceInitialized += (_, _) => ConfigureNativeWindow();
    }

    /// <summary>Creates the native window without showing it.</summary>
    public void Prepare()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Build the content now (templates, icons), so the first opening is as quick as the next ones.
        View.Measure(new Size(Width, Height));
        View.Arrange(new Rect(0, 0, Width, Height));
    }

    public void Toggle()
    {
        if (IsVisible)
            HidePanel("shortcut", returnFocus: true);
        else
            ShowPanel();
    }

    public void ShowPanel()
    {
        if (IsVisible)
        {
            BringToFront();
            return;
        }
        var started = Stopwatch.GetTimestamp();
        var foreground = NativeMethods.GetForegroundWindow();
        _previousWindow = foreground != _hwnd ? foreground : 0;
        PlaceOnActiveMonitor();
        _viewModel.OnPanelShowing();
        Show();
        BringToFront();
        View.FocusSearch();
        Dispatcher.InvokeAsync(
            () => _viewModel.ReportOpenTime(Stopwatch.GetElapsedTime(started)),
            DispatcherPriority.ContextIdle);
    }

    /// <param name="returnFocus">
    /// The user closed the panel (Esc, the shortcut): the focus goes back to the window they were working in, instead of
    /// the one Windows would pick, which with GlazeWM can be on another workspace and bring that forward.
    /// </param>
    public void HidePanel(string reason, bool returnFocus = false)
    {
        if (!IsVisible)
            return;
        Log.Write($"Panel hidden: {reason}");
        Hide();
        _viewModel.OnPanelHidden();
        if (returnFocus && IsStillThere(_previousWindow))
            NativeMethods.SetForegroundWindow(_previousWindow);
    }

    /// <summary>Open, shown, not minimized, and not hidden on another workspace (GlazeWM "cloaks" those windows).</summary>
    private static bool IsStillThere(nint window) =>
        window != 0 && NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window) && !NativeMethods.IsIconic(window) &&
        !(NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0);

    private void BringToFront()
    {
        Activate();
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            NativeMethods.SetForegroundWindow(_hwnd);
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            TakeFocusFromForegroundWindow();
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            Log.Write($"The panel could not take the focus, in front: {DescribeForegroundWindow()}");
    }

    /// <summary>
    /// Windows refuses the focus to a program while another one, like Teams, holds it. Sharing the keyboard input
    /// with that window's thread for a moment makes the request come from "inside" the foreground, which is allowed.
    /// </summary>
    private void TakeFocusFromForegroundWindow()
    {
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
        var ownThread = NativeMethods.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == ownThread)
            return;
        NativeMethods.AttachThreadInput(ownThread, foregroundThread, true);
        try
        {
            NativeMethods.BringWindowToTop(_hwnd);
            NativeMethods.SetForegroundWindow(_hwnd);
        }
        finally
        {
            NativeMethods.AttachThreadInput(ownThread, foregroundThread, false);
        }
        if (NativeMethods.GetForegroundWindow() == _hwnd)
            Log.Write("The panel took the focus with the thread-input workaround");
    }

    private static string DescribeForegroundWindow() => WindowInfo.Describe(NativeMethods.GetForegroundWindow());

    private void ConfigureNativeWindow()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.OwnerWindow = _hwnd;
        // A tool window has no taskbar button and no Alt+Tab entry, and window managers like GlazeWM leave it alone.
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE,
            (style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);
        var corners = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
    }

    /// <summary>Centers the panel on the monitor the user is working on.</summary>
    private void PlaceOnActiveMonitor()
    {
        var monitor = ActiveMonitor();
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;
        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0
            ? dpi / 96.0
            : 1.0;
        var work = info.rcWork;
        var width = (int)Math.Min(work.Width * 0.82, 1320 * scale);
        var height = (int)Math.Min(work.Height * 0.8, 840 * scale);
        var x = work.Left + (work.Width - width) / 2;
        var y = work.Top + (work.Height - height) / 2;
        // Twice: after moving onto a monitor with a different scaling, WPF resizes the window once more.
        for (var i = 0; i < 2; i++)
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height, NativeMethods.SWP_NOACTIVATE);
    }

    private nint ActiveMonitor()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground != 0 && foreground != _hwnd)
            return NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        NativeMethods.GetCursorPos(out var cursor);
        return NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }
}
