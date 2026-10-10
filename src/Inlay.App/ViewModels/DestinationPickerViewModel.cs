using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>
/// «Sposta in…»: the places where the chosen files can go, the recent ones first, narrowed down by what is typed.
/// Asked inside the panel, like a confirmation, because the panel hides as soon as it loses the focus.
/// </summary>
public sealed partial class DestinationPickerViewModel : ObservableObject
{
    private readonly IReadOnlyList<DestinationViewModel> _all;
    private readonly Func<Destination, string?> _confirm;

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private DestinationViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <param name="destinations">Where the files can go, in the order of the sidebar.</param>
    /// <param name="recentKeys">The metadata keys of the places used last, newest first.</param>
    /// <param name="confirm">Does the move; returns a problem to show, or null when done.</param>
    public DestinationPickerViewModel(string title, string confirmLabel, IReadOnlyList<Destination> destinations,
        IReadOnlyList<string> recentKeys, Func<Destination, string?> confirm)
    {
        Title = title;
        ConfirmLabel = confirmLabel;
        _confirm = confirm;
        var recency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < recentKeys.Count; i++)
            recency.TryAdd(recentKeys[i], i);
        _all = destinations
            .Select((destination, order) => new DestinationViewModel(destination, order,
                recency.TryGetValue(destination.RelativeKey, out var recent) ? recent : null))
            .ToList();
        Refresh();
    }

    public event EventHandler? Closed;

    public string Title { get; }

    public string ConfirmLabel { get; }

    public ObservableCollection<DestinationViewModel> Candidates { get; } = [];

    public bool HasCandidates => Candidates.Count > 0;

    public bool HasError => Error is not null;

    /// <summary>Why the list is empty.</summary>
    public string EmptyMessage => _all.Count == 0
        ? "Non c'è nessun posto dove mandarli: crea prima un riquadro o un argomento."
        : "Nessun riquadro o argomento con questo nome.";

    partial void OnFilterChanged(string value) => Refresh();

    /// <summary>Moves the choice up or down the list, as the arrow keys do.</summary>
    public void MoveSelection(int delta)
    {
        if (Candidates.Count == 0)
            return;
        var index = Selected is null ? -1 : Candidates.IndexOf(Selected);
        Selected = Candidates[Math.Clamp(index + delta, 0, Candidates.Count - 1)];
    }

    private void Refresh()
    {
        var words = SearchText.Words(Filter);
        IEnumerable<DestinationViewModel> shown = words.Length == 0
            // Nothing typed: the recent places, newest first, then everything in the order of the sidebar.
            ? _all.OrderBy(destination => destination.Recency ?? int.MaxValue).ThenBy(destination => destination.Order)
            : _all.Select(destination => (Destination: destination, Score: SearchText.Score(destination.NameKey, destination.PathKey, words)))
                .Where(match => match.Score > 0)
                .OrderByDescending(match => match.Score + (match.Destination.IsRecent ? 30 : 0))
                .ThenBy(match => match.Destination.Order)
                .Select(match => match.Destination);
        var kept = Selected;
        Candidates.Clear();
        foreach (var destination in shown)
            Candidates.Add(destination);
        Selected = kept is not null && Candidates.Contains(kept) ? kept : Candidates.FirstOrDefault();
        OnPropertyChanged(nameof(HasCandidates));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (Selected is not { } chosen)
            return;
        Error = _confirm(chosen.Destination);
        if (Error is null)
            Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}

/// <summary>One place in the list: a tile, or a topic for its loose files.</summary>
public sealed class DestinationViewModel(Destination destination, int order, int? recency)
{
    public Destination Destination { get; } = destination;

    public string Title => Destination.Title;

    /// <summary>"Lavoro › Clienti" for a tile; "Argomento", or "Argomento in Lavoro", for a topic, which gets the files among its loose ones.</summary>
    public string Detail => Destination.IsTopic
        ? Destination.Location.Length == 0 ? "Argomento" : $"Argomento in {Destination.Location}"
        : Destination.Location;

    public bool IsTopic => Destination.IsTopic;

    public bool IsRecent => Recency is not null;

    /// <summary>Its place among the recent ones, 0 for the newest; null when it is not recent.</summary>
    internal int? Recency { get; } = recency;

    /// <summary>Its place in the sidebar.</summary>
    internal int Order { get; } = order;

    internal string NameKey { get; } = SearchText.Normalize(destination.Title);

    internal string PathKey { get; } = SearchText.Normalize(destination.Location);
}
