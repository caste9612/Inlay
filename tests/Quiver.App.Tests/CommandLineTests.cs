using Quiver.App.Services;

namespace Quiver.App.Tests;

public sealed class CommandLineTests
{
    [Fact]
    public void Sort_TakesEveryPathThatFollows_SpacesIncluded()
    {
        var options = CommandLineOptions.Parse(["--sort", @"C:\Users\a\Documenti\offerta v3.pdf", @"D:\foto.jpg"]);

        Assert.Equal([@"C:\Users\a\Documenti\offerta v3.pdf", @"D:\foto.jpg"], options.SortPaths);
        Assert.Null(options.Command);
    }

    [Fact]
    public void WithoutSort_ThereAreNoPaths()
    {
        var options = CommandLineOptions.Parse(["--toggle"]);

        Assert.Empty(options.SortPaths);
        Assert.Equal(Commands.Toggle, options.Command);
    }
}
