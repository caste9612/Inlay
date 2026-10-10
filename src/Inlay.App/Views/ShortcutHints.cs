namespace Inlay.App.Views;

/// <summary>What a key does, as the «?» of the status bar lists it, with the keys that do it.</summary>
public sealed record ShortcutHint(string Action, params IReadOnlyList<string> Keys);

/// <summary>The keys worth knowing, by what they act on.</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutHint> Hints)
{
    /// <param name="hotkey">The shortcut that puts Inlay away, as the settings have it now.</param>
    public static IReadOnlyList<ShortcutGroup> For(string hotkey) =>
    [
        new("Items",
        [
            new("open", "Enter"),
            new("show in Explorer", "Ctrl+Enter"),
            new("rename", "F2"),
            new("move to Recycle Bin", "Del"),
            new("properties", "Alt+Enter"),
            new("select the whole tile", "Ctrl+A"),
        ]),
        new("Clipboard",
        [
            new("copy", "Ctrl+C"),
            new("cut", "Ctrl+X"),
            new("paste", "Ctrl+V"),
            new("copy the path", "Ctrl+Shift+C"),
        ]),
        new("Vault",
        [
            new("undo the last operation", "Ctrl+Z"),
            new("new tile", "Ctrl+Shift+N"),
        ]),
        new("Search",
        [
            new("search the whole vault, in names and folders; Esc goes back", "Type"),
            new("one type only: folders, documents, pdf, images… or an extension", "type:pdf"),
            new("only inside a space or a folder", "in:Work"),
        ]),
        new("Mosaic",
        [
            new("the whole vault in view", "Ctrl+0"),
            new("zoom in, zoom out", "Ctrl++", "Ctrl+-"),
        ]),
        new("Window",
        [
            new("show or hide the spaces", "Ctrl+B"),
            new("close: Inlay stays in the tray", "Esc", hotkey),
        ]),
    ];
}
