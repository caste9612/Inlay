using Quiver.App.Services;

namespace Quiver.App.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Save_LeavesOneWholeFile_AndNoTemporaryOne()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "quiver-app-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var settings = new AppSettings { SavePath = path, Hotkey = "Ctrl+Space" };
            settings.Save();
            settings.Hotkey = "Alt+Q";
            settings.Save();

            Assert.Equal(["settings.json"], Directory.GetFiles(folder).Select(Path.GetFileName));
            Assert.Equal("Alt+Q", AppSettings.Load(path).Hotkey);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
