using Quiver.App.Services;

namespace Quiver.App.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

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
