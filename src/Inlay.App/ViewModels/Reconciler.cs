using System.Collections.ObjectModel;

namespace Inlay.App.ViewModels;

/// <summary>
/// Brings an observable collection in line with a list read again, keeping the view models whose key is still there.
/// Their state survives: the selection, a name being typed, a branch left open, the control that shows them and its
/// focus. The view hears only about what moved, arrived or went away.
/// </summary>
internal static class Reconciler
{
    /// <param name="keyOf">The key of a model, like a path: the same key means the same thing.</param>
    /// <param name="keyOfViewModel">The key of the model a view model shows.</param>
    /// <param name="create">A view model for a model that was not there.</param>
    /// <param name="update">Gives a kept view model the model read again.</param>
    public static void Reconcile<TModel, TViewModel>(ObservableCollection<TViewModel> collection, IReadOnlyList<TModel> models,
        Func<TModel, string> keyOf, Func<TViewModel, string> keyOfViewModel, Func<TModel, TViewModel> create,
        Action<TViewModel, TModel> update)
        where TViewModel : class
    {
        var kept = new Dictionary<string, TViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var viewModel in collection)
            kept.TryAdd(keyOfViewModel(viewModel), viewModel);

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            TViewModel viewModel;
            if (kept.Remove(keyOf(model), out var existing))
            {
                viewModel = existing;
                update(viewModel, model);
            }
            else
            {
                viewModel = create(model);
            }
            // The places before this one are settled already: a kept view model can only be here or further on.
            var at = collection.IndexOf(viewModel);
            if (at == index)
                continue;
            if (at >= 0)
                collection.Move(at, index);
            else
                collection.Insert(index, viewModel);
        }
        while (collection.Count > models.Count)
            collection.RemoveAt(collection.Count - 1);
    }
}
