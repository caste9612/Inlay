using Inlay.App.ViewModels;

namespace Inlay.App.Tests;

/// <summary>The page «About» of the settings: the version, and where the updates are, as the app tells it.</summary>
public sealed class UpdatesViewModelTests
{
    [Fact]
    public void TheVersion_AndWhereTheUpdatesAre_StepByStep()
    {
        var updates = new UpdatesViewModel("0.3.4", canUpdate: true, () => { }, () => { });
        Assert.Equal("Version 0.3.4", updates.VersionText);
        Assert.StartsWith("Not checked yet", updates.StatusText);
        Assert.True(updates.CheckCommand.CanExecute(null));

        updates.IsChecking = true;
        Assert.Equal("Checking for updates…", updates.StatusText);
        Assert.False(updates.CheckCommand.CanExecute(null));

        updates.IsChecking = false;
        updates.LastCheck = DateTime.Today.AddHours(14).AddMinutes(56);
        Assert.Equal("Inlay is up to date: checked today at 14:56.", updates.StatusText);

        updates.LastCheckFailed = true;
        Assert.StartsWith("Couldn't check for updates", updates.StatusText);

        updates.ReadyVersion = "0.3.5";
        Assert.True(updates.IsReady);
        Assert.Equal("Inlay 0.3.5 is ready: it updates when you quit, or right away with Restart to update.", updates.StatusText);
    }

    [Fact]
    public void TheButtons_AskTheApp_ToCheckAndToRestart()
    {
        var checks = 0;
        var restarts = 0;
        var updates = new UpdatesViewModel("0.3.4", canUpdate: true, () => checks++, () => restarts++);

        updates.CheckCommand.Execute(null);
        updates.RestartCommand.Execute(null);

        Assert.Equal(1, checks);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public void AnInlayRunFromTheCode_SaysSo_AndDoesNotCheck()
    {
        var updates = new UpdatesViewModel(UpdatesViewModel.VersionOfTheCode(), canUpdate: false, () => { }, () => { });

        Assert.Matches(@"^\d+\.\d+\.\d+$", updates.Version);
        Assert.StartsWith("This Inlay runs from the code", updates.StatusText);
        Assert.False(updates.CheckCommand.CanExecute(null));
    }
}
