using CommunityToolkit.Mvvm.ComponentModel;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>A kind the search can be narrowed to, as a filter above the results: a click adds or takes away "type:…".</summary>
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
