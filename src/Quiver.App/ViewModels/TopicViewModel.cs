using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using Quiver.Core;

namespace Quiver.App.ViewModels;

/// <summary>A topic in the sidebar tree.</summary>
public sealed partial class TopicViewModel : RenameableViewModel
{
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

    public IEnumerable<TopicViewModel> Ancestors()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
            yield return ancestor;
    }

    public IEnumerable<TopicViewModel> SelfAndDescendants() =>
        Children.SelectMany(child => child.SelfAndDescendants()).Prepend(this);

    public IEnumerable<string> PathNames() => Ancestors().Reverse().Select(a => a.Name).Append(Name);
}
