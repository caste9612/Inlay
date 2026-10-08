using Quiver.App.Services;

namespace Quiver.App.Tests;

/// <summary>The places files went to last, in a file of their own.</summary>
public sealed class RecentDestinationsTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheLastPlaceComesFirst_OnceOnly_UpToTheCapacity_AndSurvivesAReload()
    {
        var path = Path.Combine(_folder, "recenti.json");
        var recent = RecentDestinations.Load(path);

        recent.Remember(@"C:\Archivio", "Lavoro/Documenti");
        recent.Remember(@"C:\Archivio", "Personale");
        // The same archive with a trailing separator, and a place used again: first, once.
        recent.Remember(@"C:\Archivio\", "Lavoro/Documenti");
        for (var i = 0; i < RecentDestinations.Capacity + 3; i++)
            recent.Remember(@"D:\Altro", $"Riquadro {i}");

        Assert.Equal(["Lavoro/Documenti", "Personale"], recent.For(@"C:\Archivio"));
        Assert.Equal(RecentDestinations.Capacity, recent.For(@"D:\Altro").Count);
        Assert.Equal($"Riquadro {RecentDestinations.Capacity + 2}", recent.For(@"D:\Altro")[0]);
        Assert.Empty(recent.For(@"E:\Nessuno"));
        Assert.Equal(["Lavoro/Documenti", "Personale"], RecentDestinations.Load(path).For(@"c:\archivio"));
        // Written whole: no temporary file left behind.
        Assert.Equal(["recenti.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void AnUnreadableFile_StartsAfresh()
    {
        var path = Path.Combine(_folder, "recenti.json");
        File.WriteAllText(path, "{ not json");

        var recent = RecentDestinations.Load(path);

        Assert.Empty(recent.For(@"C:\Archivio"));
    }
}
