using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A kind the search can be narrowed to, as a filter above the results: a click adds or takes away "tipo:…".</summary>
public sealed partial class SearchKindFilter(SearchKind kind) : ObservableObject
{
    /// <summary>Written in the search box: the filter shows it.</summary>
    [ObservableProperty]
    private bool _isChecked;

    public SearchKind Kind { get; } = kind;

    public string Label => Kind.Label;

    /// <summary>What it writes in the search box, for its tooltip.</summary>
    public string ToolTip => $"{SearchQuery.KindKey}:{Kind.Name}";
}
