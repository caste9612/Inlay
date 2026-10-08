using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>What a toast tells: its icon and color, and whether it closes by itself.</summary>
public enum ToastKind
{
    /// <summary>An operation went well.</summary>
    Success,

    /// <summary>Something worth knowing, nothing to do about it.</summary>
    Info,

    /// <summary>Done only in part, or not done for a reason that can be fixed.</summary>
    Warning,

    /// <summary>It did not work.</summary>
    Danger,
}

/// <summary>
/// A toast in the bottom right corner (br-toast of Brace): what just happened, with the way back and the way to go and see
/// after an operation. Results and news close by themselves after a few seconds, not while the pointer is on them;
/// problems stay until closed, and are in the log too.
/// </summary>
/// <param name="notice">Where «Mostra» goes, with what to select there; null without «Mostra».</param>
/// <param name="undoStep">The operation that «Annulla» takes back; null without «Annulla».</param>
public sealed partial class ToastViewModel(ToastKind kind, string title, string? body = null, StatusNotice? notice = null, UndoStep? undoStep = null)
    : ObservableObject
{
    public ToastKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string? Body { get; } = body;

    public StatusNotice? Notice { get; } = notice;

    /// <summary>The operation it tells about. A topic moved by dragging turns its move into a step of its own.</summary>
    internal UndoStep? UndoStep { get; set; } = undoStep;

    /// <summary>«Annulla» is there while the operation is the last one done: after another one, Ctrl+Z would undo that instead.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActions))]
    private bool _canUndo = undoStep is not null;

    public bool CanShow => Notice is not null;

    public bool HasActions => CanUndo || CanShow;

    public bool ClosesByItself => Kind is ToastKind.Success or ToastKind.Info;
}
