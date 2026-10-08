using Quiver.App.ViewModels;
using Quiver.Core;

namespace Quiver.App.Tests;

/// <summary>«Sposta in…»: the list of places, the recent ones first, narrowed down by what is typed.</summary>
public sealed class DestinationPickerViewModelTests
{
    private static readonly Destination[] Places =
    [
        new("Lavoro", @"C:\A\Lavoro", "Lavoro", IsTopic: true, ""),
        new("Documenti", @"C:\A\Lavoro\Documenti", "Lavoro/Documenti", IsTopic: false, "Lavoro"),
        new("Clienti", @"C:\A\Lavoro\Clienti", "Lavoro/Clienti", IsTopic: true, "Lavoro"),
        new("Rossi", @"C:\A\Lavoro\Clienti\Rossi", "Lavoro/Clienti/Rossi", IsTopic: false, "Lavoro › Clienti"),
        new("Personale", @"C:\A\Personale", "Personale", IsTopic: true, ""),
    ];

    [Fact]
    public void NothingTyped_TheRecentPlacesComeFirst_ThenTheOrderOfTheSidebar()
    {
        var picker = Make(recent: ["Lavoro/Clienti/Rossi", "Personale"]);

        Assert.Equal(["Rossi", "Personale", "Lavoro", "Documenti", "Clienti"], picker.Candidates.Select(c => c.Title));
        Assert.True(picker.Candidates[0].IsRecent);
        Assert.False(picker.Candidates[2].IsRecent);
        Assert.Same(picker.Candidates[0], picker.Selected);
        Assert.Equal("Lavoro › Clienti", picker.Candidates[0].Detail);
        Assert.Equal("Argomento", picker.Candidates[1].Detail);
        Assert.Equal("Argomento in Lavoro", picker.Candidates[4].Detail);
    }

    [Fact]
    public void Typing_NarrowsTheList_NamesFirst_ThenPlacesUnderThatPath()
    {
        var picker = Make(recent: []);

        picker.Filter = "clie";

        // Clienti by its name; Rossi because it sits in Clienti.
        Assert.Equal(["Clienti", "Rossi"], picker.Candidates.Select(c => c.Title));
        Assert.Equal("Clienti", picker.Selected?.Title);

        picker.Filter = "xyz";
        Assert.Empty(picker.Candidates);
        Assert.Null(picker.Selected);
        Assert.Contains("Nessun riquadro", picker.EmptyMessage);
    }

    [Fact]
    public void TheArrowKeys_MoveTheChoice_AndStayInsideTheList()
    {
        var picker = Make(recent: []);

        picker.MoveSelection(+1);
        picker.MoveSelection(+1);
        Assert.Equal("Clienti", picker.Selected?.Title);
        picker.MoveSelection(-5);
        Assert.Equal("Lavoro", picker.Selected?.Title);
    }

    [Fact]
    public void Confirming_HandsOverTheChosenPlace_AndCloses_UnlessThereIsAProblem()
    {
        Destination? chosen = null;
        var problem = "Non ora.";
        var closed = 0;
        var picker = new DestinationPickerViewModel("Sposta in…", "Sposta", Places, [], destination =>
        {
            chosen = destination;
            return problem;
        });
        picker.Closed += (_, _) => closed++;
        picker.Filter = "pers";

        picker.ConfirmCommand.Execute(null);
        Assert.Equal("Personale", chosen?.Title);
        Assert.Equal("Non ora.", picker.Error);
        Assert.Equal(0, closed);

        problem = null!;
        picker.ConfirmCommand.Execute(null);
        Assert.Null(picker.Error);
        Assert.Equal(1, closed);

        picker.CancelCommand.Execute(null);
        Assert.Equal(2, closed);
    }

    [Fact]
    public void WithNowhereToGo_TheListSaysSo()
    {
        var picker = new DestinationPickerViewModel("Sposta in…", "Sposta", [], [], _ => null);

        Assert.False(picker.HasCandidates);
        Assert.Contains("crea prima", picker.EmptyMessage);
    }

    private static DestinationPickerViewModel Make(IReadOnlyList<string> recent) =>
        new("Sposta in…", "Sposta", Places, recent, _ => null);
}
