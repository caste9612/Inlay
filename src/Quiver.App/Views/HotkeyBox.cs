using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Quiver.App.Views;

/// <summary>
/// A field that records the combination pressed in it, instead of typing: Ctrl, Alt or Win with a key, written as
/// "Alt+E". Modifiers alone wait for their key, Esc keeps what was there, Tab moves on as usual.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
        nameof(Shortcut), typeof(string), typeof(HotkeyBox),
        new PropertyMetadata("", (box, e) => ((HotkeyBox)box).Text = (string?)e.NewValue ?? ""));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Arrow;
    }

    /// <summary>The shortcut in use, shown in the field.</summary>
    public string Shortcut
    {
        get => (string)GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    /// <summary>A whole combination was pressed: the field shows it, and whoever listens decides whether it stays.</summary>
    public event EventHandler<string>? ShortcutCaptured;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab)
        {
            base.OnPreviewKeyDown(e);
            return;
        }
        // Nothing is ever typed here: every key is a candidate for the shortcut.
        e.Handled = true;
        if (key == Key.Escape)
        {
            Text = Shortcut;
            return;
        }
        if (IsModifier(key) || key == Key.None)
            return;
        var modifiers = Keyboard.Modifiers;
        // Shift alone, or no modifier at all, would steal ordinary keys from every program.
        if (modifiers is ModifierKeys.None or ModifierKeys.Shift)
            return;
        var shortcut = new KeyGesture(key, modifiers).GetDisplayStringForCulture(CultureInfo.InvariantCulture);
        Text = shortcut;
        ShortcutCaptured?.Invoke(this, shortcut);
    }

    private static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
