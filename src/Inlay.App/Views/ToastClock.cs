using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Inlay.App.Views;

/// <summary>
/// Closes a toast that closes by itself after six seconds, as Brace wants: the count stands still while the pointer or
/// the focus is on it, and goes on from there when they leave.
/// </summary>
internal sealed class ToastClock
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(6);

    private readonly FrameworkElement _toast;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _running = new();
    private TimeSpan _left = Lifetime;

    private ToastClock(FrameworkElement toast, Action close)
    {
        _toast = toast;
        _timer = new DispatcherTimer(DispatcherPriority.Normal, toast.Dispatcher);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            close();
        };
        toast.MouseEnter += (_, _) => Pause();
        toast.MouseLeave += (_, _) => Resume();
        toast.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            if (toast.IsKeyboardFocusWithin)
                Pause();
            else
                Resume();
        };
        toast.Unloaded += (_, _) => _timer.Stop();
    }

    public static void Start(FrameworkElement toast, Action close) => new ToastClock(toast, close).Resume();

    private void Pause()
    {
        if (!_timer.IsEnabled)
            return;
        _timer.Stop();
        _left -= _running.Elapsed;
        _running.Reset();
    }

    private void Resume()
    {
        if (_timer.IsEnabled || _toast.IsMouseOver || _toast.IsKeyboardFocusWithin)
            return;
        _timer.Interval = _left > TimeSpan.Zero ? _left : TimeSpan.FromMilliseconds(1);
        _running.Restart();
        _timer.Start();
    }
}
