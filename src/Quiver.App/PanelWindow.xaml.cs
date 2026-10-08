using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using Quiver.App.Interop;
using Quiver.App.Services;
using Quiver.App.ViewModels;

namespace Quiver.App;

/// <summary>
/// The window of Quiver: an ordinary window with a title bar of Brace, created once at startup and only shown and hidden
/// afterwards, so it appears instantly. It stays where the user puts it, above the others while pinned, and goes back to
/// the notification area with the shortcut, Esc or its close button. It remembers its place, its size and its pin.
/// </summary>
public partial class PanelWindow : Window
{
    /// <summary>The height of the title bar of the view, which moves the window: titlebar-height of Brace, the CaptionHeight of its chrome.</summary>
    private const double TitleBarHeight = 32;

    /// <summary>How the settings say that the canvas was the view in use.</summary>
    private const string CanvasView = "canvas";

    private readonly MainViewModel _viewModel;
    private readonly AppSettings _settings;
    private nint _hwnd;
    private nint _previousWindow;
    private bool _placed;
    private bool _closeAllowed;
    private long _shownAt;
    private WindowState _restoreState = WindowState.Normal;

    internal PanelWindow(MainViewModel viewModel, AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        Topmost = settings.Topmost;
        // The board or the canvas, and the topics on the left open or closed, as left last time.
        viewModel.IsCanvasShown = settings.View == CanvasView;
        viewModel.IsSidebarOpen = !settings.SidebarClosed;
        viewModel.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.IsCanvasShown):
                    _settings.View = viewModel.IsCanvasShown ? CanvasView : null;
                    break;
                case nameof(MainViewModel.IsSidebarOpen):
                    _settings.SidebarClosed = !viewModel.IsSidebarOpen;
                    break;
                default:
                    return;
            }
            SaveSettings();
        };
        View.CloseRequested += (_, _) => HidePanel("closed", returnFocus: true);
        View.ArchivesRequested += (_, _) => ArchivesRequested?.Invoke(this, EventArgs.Empty);
        // The shell's dialogs took the focus and do not give it back. If the user went to another program while the
        // files went, the focus stays there.
        viewModel.FileOperationCompleted += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized && ForegroundIsOurs())
                BringToFront();
        };
        // Back from the taskbar, Alt+Tab or a click: what changed meanwhile shows. Right after being shown, it was read already.
        Activated += (_, _) =>
        {
            if (Stopwatch.GetElapsedTime(_shownAt) > TimeSpan.FromSeconds(1))
                _viewModel.OnWindowActivated();
        };
        // The window the user goes to is where the focus returns when Quiver is put away: not one Windows would pick,
        // which with GlazeWM can be on another workspace. At this moment Windows has not chosen it yet.
        Deactivated += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != 0 && foreground != _hwnd)
                _previousWindow = foreground;
        }, DispatcherPriority.Background);
        StateChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
                _restoreState = WindowState;
            FitMaximized();
        };
        DpiChanged += (_, _) => FitMaximized();
        SourceInitialized += (_, _) => ConfigureNativeWindow();
    }

    /// <summary>The name of the archive was clicked: the window of the archives should open.</summary>
    public event EventHandler? ArchivesRequested;

    /// <summary>Creates the native window without showing it.</summary>
    public void Prepare()
    {
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Build the content now (templates, icons), so the first opening is as quick as the next ones.
        View.Measure(new Size(Width, Height));
        View.Arrange(new Rect(0, 0, Width, Height));
    }

    /// <summary>The shortcut: Quiver comes where the user can use it, and goes away when it is there already.</summary>
    public void Toggle()
    {
        var inFront = NativeMethods.GetForegroundWindow() == _hwnd;
        if (WindowToggle.Decide(IsVisible, WindowState == WindowState.Minimized, inFront) == ToggleAction.Hide)
            HidePanel("shortcut", returnFocus: true);
        else
            ShowPanel();
    }

    /// <summary>Shows the window, brings it back from the taskbar, or brings it forward, whichever it needs.</summary>
    public void ShowPanel()
    {
        if (IsVisible)
        {
            if (WindowState == WindowState.Minimized)
                WindowState = _restoreState;
            BringToFront();
            return;
        }
        var started = Stopwatch.GetTimestamp();
        var foreground = NativeMethods.GetForegroundWindow();
        _previousWindow = foreground != _hwnd ? foreground : 0;
        if (!_placed)
            PlaceFirstTime();
        if (WindowState == WindowState.Minimized)
            WindowState = _restoreState;
        _viewModel.OnPanelShowing();
        _shownAt = started;
        Show();
        BringToFront();
        View.FocusSearch();
        View.AnimateIn();
        Dispatcher.InvokeAsync(
            () => _viewModel.ReportOpenTime(Stopwatch.GetElapsedTime(started)),
            DispatcherPriority.ContextIdle);
    }

    /// <param name="returnFocus">
    /// The user put Quiver away (Esc, the shortcut, the close button): the focus goes back to the window they were working
    /// in, instead of the one Windows would pick, which with GlazeWM can be on another workspace and bring that forward.
    /// </param>
    public void HidePanel(string reason, bool returnFocus = false)
    {
        if (!IsVisible)
            return;
        Log.Write($"Window hidden: {reason}");
        SavePlacement();
        Hide();
        _viewModel.OnPanelHidden();
        if (returnFocus && IsStillThere(_previousWindow))
            NativeMethods.SetForegroundWindow(_previousWindow);
    }

    /// <summary>Quiver is exiting: the window may close now, and remembers where it was.</summary>
    public void AllowClose() => _closeAllowed = true;

    /// <summary>The window is made once and lives as long as Quiver: closing it, as Alt+F4 or the taskbar do, only hides it.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeAllowed)
        {
            SavePlacement();
        }
        else
        {
            e.Cancel = true;
            HidePanel("closed", returnFocus: true);
        }
        base.OnClosing(e);
    }

    /// <summary>The pin, from the title bar or the tray: kept at once, so that Quiver starts above the others or not as it was left.</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == TopmostProperty && _settings is not null && _settings.Topmost != Topmost)
        {
            _settings.Topmost = Topmost;
            SaveSettings();
        }
    }

    /// <summary>Open, shown, not minimized, and not hidden on another workspace (GlazeWM "cloaks" those windows).</summary>
    private static bool IsStillThere(nint window) =>
        window != 0 && NativeMethods.IsWindow(window) && NativeMethods.IsWindowVisible(window) && !NativeMethods.IsIconic(window) &&
        !(NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0);

    /// <summary>Nothing is in front, or a window of Quiver is: its own, or a dialog of the shell on its behalf.</summary>
    private static bool ForegroundIsOurs()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == 0)
            return true;
        NativeMethods.GetWindowThreadProcessId(foreground, out var process);
        return process == Environment.ProcessId;
    }

    private void BringToFront()
    {
        Activate();
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            NativeMethods.SetForegroundWindow(_hwnd);
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            TakeFocusFromForegroundWindow();
        if (NativeMethods.GetForegroundWindow() != _hwnd)
            Log.Write($"The window could not take the focus, in front: {DescribeForegroundWindow()}");
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
            Log.Write("The window took the focus with the thread-input workaround");
    }

    private static string DescribeForegroundWindow() => WindowInfo.Describe(NativeMethods.GetForegroundWindow());

    private void ConfigureNativeWindow()
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _viewModel.OwnerWindow = _hwnd;
        WindowCorners.MakeSmall(_hwnd);
        FitMaximized();
    }

    /// <summary>
    /// Maximized, the window keeps its invisible frame, just beyond the edges of the screen: the content moves in by as much,
    /// or its edges would be cut off, and so does the band that moves the window.
    /// </summary>
    private void FitMaximized()
    {
        var inset = WindowState == WindowState.Maximized ? HiddenFrame() : 0;
        View.Margin = new Thickness(inset);
        if (WindowChrome.GetWindowChrome(this) is { IsFrozen: false } chrome)
            chrome.CaptionHeight = TitleBarHeight + inset;
    }

    /// <summary>How far beyond the screen Windows puts the frame of a maximized window, at the scaling of its monitor.</summary>
    private double HiddenFrame()
    {
        if (_hwnd == 0)
            return 0;
        var dpi = NativeMethods.GetDpiForWindow(_hwnd);
        if (dpi == 0)
            dpi = 96;
        var pixels = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSIZEFRAME, dpi) +
                     NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
        return pixels * 96.0 / dpi;
    }

    /// <summary>The first time it is shown: where it was the last time, or else centered on the monitor the user works on.</summary>
    private void PlaceFirstTime()
    {
        _placed = true;
        if (_settings.Window is { } saved && saved.IsUsable())
        {
            var placement = new NativeMethods.WINDOWPLACEMENT
            {
                length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>(),
                // Still hidden: Show() shows it there. If its screen is gone, Windows moves it onto one that is there.
                showCmd = NativeMethods.SW_HIDE,
                rcNormalPosition = new NativeMethods.RECT { Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom },
            };
            if (NativeMethods.SetWindowPlacement(_hwnd, ref placement))
            {
                WindowState = _restoreState = saved.Maximized ? WindowState.Maximized : WindowState.Normal;
                return;
            }
            Log.Write($"Could not put the window back where it was: {Marshal.GetLastWin32Error()}");
        }
        PlaceOnActiveMonitor();
    }

    /// <summary>Remembers where the window is, as Windows keeps it, for the next time Quiver starts.</summary>
    private void SavePlacement()
    {
        if (_hwnd == 0 || !IsVisible)
            return;
        var placement = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
        if (!NativeMethods.GetWindowPlacement(_hwnd, ref placement))
            return;
        // Minimized, it is kept as it will come back: maximized or not.
        var maximized = placement.showCmd == NativeMethods.SW_SHOWMAXIMIZED ||
                        (placement.showCmd == NativeMethods.SW_SHOWMINIMIZED && (placement.flags & NativeMethods.WPF_RESTORETOMAXIMIZED) != 0);
        var rect = placement.rcNormalPosition;
        var current = new WindowPlacement(rect.Left, rect.Top, rect.Right, rect.Bottom, maximized);
        if (current == _settings.Window)
            return;
        _settings.Window = current;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Could not save the settings: {e.Message}");
        }
    }

    /// <summary>The first time ever: centered on the monitor the user is working on, big but not covering it all.</summary>
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
            NativeMethods.SetWindowPos(_hwnd, 0, x, y, width, height, NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
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
