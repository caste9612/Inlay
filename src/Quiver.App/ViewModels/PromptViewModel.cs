using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Quiver.App.ViewModels;

/// <summary>
/// A question asked inside the panel: a name to type, or a confirmation. It is not a separate window
/// because the panel hides as soon as it loses the focus.
/// </summary>
public sealed partial class PromptViewModel : ObservableObject
{
    private readonly Func<string, string?> _confirm;

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="confirm">Does the work with the typed text; returns a problem to show, or null when done.</param>
    /// <param name="initialText">Null for a plain confirmation, without a text box.</param>
    /// <param name="selectionLength">How much of the initial text starts selected; all of it by default.</param>
    public PromptViewModel(string title, string confirmLabel, Func<string, string?> confirm,
        string? initialText = null, int? selectionLength = null, string? message = null)
    {
        Title = title;
        ConfirmLabel = confirmLabel;
        Message = message;
        HasInput = initialText is not null;
        Text = initialText ?? "";
        SelectionLength = selectionLength ?? Text.Length;
        _confirm = confirm;
    }

    public event EventHandler? Closed;

    public string Title { get; }

    public string ConfirmLabel { get; }

    public string? Message { get; }

    public bool HasInput { get; }

    public int SelectionLength { get; }

    public bool HasError => Error is not null;

    [RelayCommand]
    private void Confirm()
    {
        Error = _confirm(Text);
        if (Error is null)
            Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}
