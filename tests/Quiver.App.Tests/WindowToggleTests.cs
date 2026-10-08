using Quiver.App.Services;

namespace Quiver.App.Tests;

/// <summary>The shortcut brings Quiver where it can be used, and puts it away when it is already there.</summary>
public sealed class WindowToggleTests
{
    [Theory]
    [InlineData(false, false, false, nameof(ToggleAction.Show))]
    [InlineData(true, true, false, nameof(ToggleAction.Restore))]
    [InlineData(true, false, false, nameof(ToggleAction.BringForward))]
    [InlineData(true, false, true, nameof(ToggleAction.Hide))]
    public void TheShortcut_DependsOnWhereTheWindowIs(bool visible, bool minimized, bool inFront, string expected) =>
        Assert.Equal(Enum.Parse<ToggleAction>(expected), WindowToggle.Decide(visible, minimized, inFront));
}
