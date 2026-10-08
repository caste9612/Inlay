using Quiver.App.Services;

namespace Quiver.App.Tests;

public sealed class DiskSpaceTests
{
    [Fact]
    public void TheDiskOfAFolder_SaysHowMuchIsFree()
    {
        var disk = DiskSpace.Of(Path.GetTempPath());

        Assert.NotNull(disk);
        Assert.InRange(disk.Free, 1, disk.Total);
        Assert.Equal(Path.GetPathRoot(Path.GetTempPath())!.TrimEnd('\\'), disk.Name);
    }

    [Fact]
    public void AFolderThatIsNotThere_HasNoDisk() =>
        Assert.Null(DiskSpace.Of(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Non esiste")));

    [Theory]
    // Under a tenth, or under 5 GB whatever the size.
    [InlineData(40, 500, true)]
    [InlineData(60, 500, false)]
    [InlineData(4, 20, true)]
    [InlineData(300, 4000, true)]
    [InlineData(500, 4000, false)]
    public void ShortOfSpace_UnderATenthOrUnder5GB(long freeGb, long totalGb, bool low)
    {
        const long gb = 1024L * 1024 * 1024;

        Assert.Equal(low, new DiskSpace("E:", freeGb * gb, totalGb * gb).IsLow);
    }

    [Fact]
    public void ItsDescription_IsAsExplorerWritesIt()
    {
        const long gb = 1024L * 1024 * 1024;
        var disk = new DiskSpace("E:", 120 * gb, 465 * gb);

        Assert.Equal("120 GB liberi", disk.FreeText);
        Assert.Equal("Disco E: 120 GB liberi su 465 GB", disk.Description);
    }
}
