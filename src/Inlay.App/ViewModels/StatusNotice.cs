namespace Inlay.App.ViewModels;

/// <summary>
/// What the buttons next to the message in the status bar can do after an operation: «Undo» takes it back, and
/// «Show» goes where the items went, with them selected.
/// </summary>
/// <param name="ShowFolder">Where the items went; null when there is nowhere to go, as after a deletion.</param>
/// <param name="Paths">The items, where they are now.</param>
public sealed record StatusNotice(string? ShowFolder, IReadOnlyList<string> Paths);
