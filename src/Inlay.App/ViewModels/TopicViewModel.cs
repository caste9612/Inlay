using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Inlay.App.Views;
using Inlay.Core;

namespace Inlay.App.ViewModels;

/// <summary>A topic in the sidebar tree.</summary>
public sealed partial class TopicViewModel : RenameableViewModel
{
    /// <summary>For «Colore»: the color Inlay gives, or for a topic inside another the one of its topic.</summary>
    public const string AutomaticChoice = "auto";

    private string? _shownColor;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelected;

    public TopicViewModel(Topic topic, TopicViewModel? parent)
    {
        Topic = topic;
        Parent = parent;
        Children = new ObservableCollection<TopicViewModel>(topic.Children.Select(child => new TopicViewModel(child, this)));
    }

    public Topic Topic { get; private set; }

    public TopicViewModel? Parent { get; }

    public ObservableCollection<TopicViewModel> Children { get; }

    public string Name => Topic.Name;

    /// <summary>Only nested topics can go back to being tiles: folders in the root are always topics.</summary>
    [MemberNotNullWhen(true, nameof(Parent))]
    public bool CanDemote => Parent is not null;

    public override string RenamePath => Topic.FullPath;

    /// <summary>
    /// The color it shows, as "#RRGGBB"; null for none. A topic of the root shows its own, given by Inlay or chosen; one
    /// inside another, the one chosen for it, or else that of its topic.
    /// </summary>
    public string? Color => Topic.Color switch
    {
        TopicColors.None => null,
        { } own when Parent is null || !Topic.ColorIsAutomatic => own,
        _ => Parent?.Color,
    };

    public Brush? ColorBrush => TileColors.BrushFor(Color);

    public bool HasColor => ColorBrush is not null;

    /// <summary>What «Colore» has checked: <see cref="AutomaticChoice"/>, <see cref="TopicColors.None"/>, or the color chosen.</summary>
    public string ColorChoice => Topic.Color is { } own && (own == TopicColors.None || !Topic.ColorIsAutomatic) ? own : AutomaticChoice;

    protected override bool RenamesFolder => true;

    /// <summary>
    /// The topic read again: the children still there keep their view models, open or closed as they were. Under the
    /// same key only the case of the name can differ.
    /// </summary>
    public void Update(Topic topic)
    {
        var renamed = !string.Equals(Topic.FullPath, topic.FullPath, StringComparison.Ordinal);
        Topic = topic;
        if (renamed)
            OnPropertyChanged(nameof(Name));
        Reconciler.Reconcile(Children, topic.Children, child => child.RelativePath, child => child.Topic.RelativePath,
            child => new TopicViewModel(child, this), (viewModel, child) => viewModel.Update(child));
    }

    /// <summary>After a reload: the color shown, and those of the topics inside, which may follow it.</summary>
    internal void RefreshColors()
    {
        if (!string.Equals(_shownColor, Color, StringComparison.OrdinalIgnoreCase))
        {
            _shownColor = Color;
            OnPropertyChanged(nameof(Color));
            OnPropertyChanged(nameof(ColorBrush));
            OnPropertyChanged(nameof(HasColor));
        }
        OnPropertyChanged(nameof(ColorChoice));
        foreach (var child in Children)
            child.RefreshColors();
    }

    public IEnumerable<TopicViewModel> Ancestors()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
            yield return ancestor;
    }

    public IEnumerable<TopicViewModel> SelfAndDescendants() =>
        Children.SelectMany(child => child.SelfAndDescendants()).Prepend(this);

    public IEnumerable<string> PathNames() => Ancestors().Reverse().Select(a => a.Name).Append(Name);
}
