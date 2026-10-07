using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A topic in the sidebar tree.</summary>
public sealed partial class TopicViewModel : RenameableViewModel
{
    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    public TopicViewModel(Topic topic, TopicViewModel? parent, IReadOnlySet<string> collapsedPaths)
    {
        Topic = topic;
        Parent = parent;
        IsExpanded = !collapsedPaths.Contains(topic.RelativePath);
        Children = [.. topic.Children.Select(child => new TopicViewModel(child, this, collapsedPaths))];
    }

    public Topic Topic { get; }

    public TopicViewModel? Parent { get; }

    public IReadOnlyList<TopicViewModel> Children { get; }

    public string Name => Topic.Name;

    /// <summary>Only nested topics can go back to being tiles: folders in the root are always topics.</summary>
    [MemberNotNullWhen(true, nameof(Parent))]
    public bool CanDemote => Parent is not null;

    public override string RenamePath => Topic.FullPath;

    protected override bool RenamesFolder => true;

    public IEnumerable<TopicViewModel> Ancestors()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
            yield return ancestor;
    }

    public IEnumerable<TopicViewModel> SelfAndDescendants() =>
        Children.SelectMany(child => child.SelfAndDescendants()).Prepend(this);

    public IEnumerable<string> PathNames() => Ancestors().Reverse().Select(a => a.Name).Append(Name);
}
