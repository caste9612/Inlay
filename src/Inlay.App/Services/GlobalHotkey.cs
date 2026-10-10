using System.Windows.Input;
using System.Windows.Interop;
using Inlay.App.Interop;

namespace Inlay.App.Services;

/// <summary>System-wide keyboard shortcut, delivered to a hidden message-only window.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x5156;
    private readonly HwndSource _sink;
    private bool _registered;

    public GlobalHotkey()
    {
        _sink = new HwndSource(new HwndSourceParameters("InlayHotkey")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _sink.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>Registers a shortcut written like "Alt+E" or "Ctrl+Shift+Space".</summary>
    public bool TryRegister(string shortcut)
    {
        Unregister();
        if (!TryParse(shortcut, out var modifiers, out var key))
            return false;
        _registered = NativeMethods.RegisterHotKey(
            _sink.Handle, HotkeyId, ToNative(modifiers) | NativeMethods.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    private static bool TryParse(string shortcut, out ModifierKeys modifiers, out Key key)
    {
        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(shortcut) is KeyGesture gesture)
            {
                (modifiers, key) = (gesture.Modifiers, gesture.Key);
                return true;
            }
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException)
        {
            Log.Write($"Invalid shortcut '{shortcut}': {e.Message}");
        }
        (modifiers, key) = (ModifierKeys.None, Key.None);
        return false;
    }

    private static uint ToNative(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Alt) ? NativeMethods.MOD_ALT : 0) |
        (modifiers.HasFlag(ModifierKeys.Control) ? NativeMethods.MOD_CONTROL : 0) |
        (modifiers.HasFlag(ModifierKeys.Shift) ? NativeMethods.MOD_SHIFT : 0) |
        (modifiers.HasFlag(ModifierKeys.Windows) ? NativeMethods.MOD_WIN : 0);

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }
        return 0;
    }

    private void Unregister()
    {
        if (_registered)
            NativeMethods.UnregisterHotKey(_sink.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _sink.Dispose();
    }
}
