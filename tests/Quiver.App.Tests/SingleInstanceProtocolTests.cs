using Quiver.App.Services;

namespace Quiver.App.Tests;

/// <summary>What a second Quiver.exe writes to the running one, read back: never through the real pipe, which would reach a running Quiver.</summary>
public sealed class SingleInstanceProtocolTests
{
    [Fact]
    public async Task ACommandTravelsWithItsPaths_OnlyTheCommandIsLowercased()
    {
        var text = new StringWriter();
        SingleInstance.Protocol.Write(text, "Sort", [@"C:\Users\a\Offerta V3.pdf", @"D:\Foto.jpg"]);

        var (command, arguments) = await SingleInstance.Protocol.ReadAsync(new StringReader(text.ToString()), CancellationToken.None);

        Assert.Equal("sort", command);
        Assert.Equal([@"C:\Users\a\Offerta V3.pdf", @"D:\Foto.jpg"], arguments);
    }

    [Fact]
    public async Task ACommandAlone_ComesWithNoArguments()
    {
        var text = new StringWriter();
        SingleInstance.Protocol.Write(text, "toggle", []);

        var (command, arguments) = await SingleInstance.Protocol.ReadAsync(new StringReader(text.ToString()), CancellationToken.None);

        Assert.Equal("toggle", command);
        Assert.Empty(arguments);
    }
}
