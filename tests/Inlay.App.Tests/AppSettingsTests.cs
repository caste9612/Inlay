using Inlay.App.Services;

namespace Inlay.App.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "inlay-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheSettingsOfQuiver_ComeOverOnce_AndNeverOverThoseOfInlay()
    {
        var quiver = Directory.CreateDirectory(Path.Combine(_folder, "Quiver")).FullName;
        File.WriteAllText(Path.Combine(quiver, "settings.json"), """{ "hotkey": "Ctrl+Q" }""");
        File.WriteAllText(Path.Combine(quiver, "recenti.json"), "{}");
        var inlay = Path.Combine(_folder, "Inlay");

        FromQuiver.TakeOver(quiver, inlay);

        Assert.Equal("Ctrl+Q", AppSettings.Load(Path.Combine(inlay, "settings.json")).Hotkey);
        Assert.True(File.Exists(Path.Combine(inlay, "recenti.json")));
        // Quiver keeps its own, in case it is still installed.
        Assert.True(File.Exists(Path.Combine(quiver, "settings.json")));

        File.WriteAllText(Path.Combine(quiver, "settings.json"), """{ "hotkey": "Alt+X" }""");
        FromQuiver.TakeOver(quiver, inlay);
        Assert.Equal("Ctrl+Q", AppSettings.Load(Path.Combine(inlay, "settings.json")).Hotkey);
    }

    [Fact]
    public void Save_LeavesOneWholeFile_AndNoTemporaryOne()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = new AppSettings { SavePath = path, Hotkey = "Ctrl+Space" };
        settings.Save();
        settings.Hotkey = "Alt+Q";
        settings.Save();

        Assert.Equal(["settings.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
        Assert.Equal("Alt+Q", AppSettings.Load(path).Hotkey);
    }

    [Fact]
    public void ThePlaceOfTheWindow_AndThePin_AreKept()
    {
        var path = Path.Combine(_folder, "settings.json");
        Assert.Null(AppSettings.Load(path).Window);
        Assert.False(AppSettings.Load(path).Topmost);

        Assert.Null(AppSettings.Load(path).View);
        Assert.False(AppSettings.Load(path).SidebarClosed);
        new AppSettings
        {
            SavePath = path, Window = new WindowPlacement(-1600, 120, -200, 980, Maximized: true), Topmost = true, View = "canvas", SidebarClosed = true,
        }.Save();

        var loaded = AppSettings.Load(path);
        Assert.Equal(new WindowPlacement(-1600, 120, -200, 980, true), loaded.Window);
        Assert.True(loaded.Topmost);
        Assert.Equal("canvas", loaded.View);
        Assert.True(loaded.SidebarClosed);
        Assert.True(loaded.Window!.IsUsable());
        Assert.False(new WindowPlacement(0, 0, 40, 30, false).IsUsable());
    }

    [Fact]
    public void AnUnreadableFile_IsSetAside_AndTheDefaultsAreUsed()
    {
        var path = Path.Combine(_folder, "settings.json");
        const string cutShort = "{ \"archiveRoot\": \"D:\\\\Archivio\", \"archives\": [";
        File.WriteAllText(path, cutShort);

        var settings = AppSettings.Load(path);

        Assert.Equal(AppSettings.DefaultHotkey, settings.Hotkey);
        Assert.Null(settings.ArchiveRoot);
        // The next save writes a new file: the old one, as it was, waits next to it.
        settings.Save();
        Assert.Equal(cutShort, File.ReadAllText(path + ".corrupt"));
    }
}
