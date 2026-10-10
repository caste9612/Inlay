using Inlay.App.Services;

namespace Inlay.App.Tests;

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
    public void TheCanvasOfAPreview_TakesItsZoom_WrittenEitherWay()
    {
        var options = CommandLineOptions.Parse(["--render-preview", "tela.png", "--zoom", "0.4"]);

        Assert.True(options.PreviewCanvas);
        Assert.Equal(0.4, options.PreviewZoom);
        Assert.True(CommandLineOptions.Parse(["--canvas"]).PreviewCanvas);
        Assert.Null(CommandLineOptions.Parse(["--canvas"]).PreviewZoom);
        Assert.False(CommandLineOptions.Parse(["--toggle"]).PreviewCanvas);
    }

    [Fact]
    public void WithoutSort_ThereAreNoPaths()
    {
        var options = CommandLineOptions.Parse(["--toggle"]);

        Assert.Empty(options.SortPaths);
        Assert.Equal(Commands.Toggle, options.Command);
    }
}
