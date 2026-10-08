using Quiver.Core;

namespace Quiver.Tests;

public sealed class ByteSizeTests
{
    [Theory]
    [InlineData(0, "0 byte")]
    [InlineData(512, "512 byte")]
    [InlineData(1024, "1,00 KB")]
    [InlineData(1536, "1,50 KB")]
    [InlineData(13_002_342, "12,4 MB")]
    [InlineData(2_523_000_000, "2,35 GB")]
    [InlineData(128_849_018_880, "120 GB")]
    // Never four figures: 1.016 KB is written 0,99 MB.
    [InlineData(1_023_000, "999 KB")]
    [InlineData(1_040_000, "0,99 MB")]
    [InlineData(5_497_558_138_880, "5,00 TB")]
    public void AsExplorerWritesThem_InItalian(long bytes, string expected) =>
        Assert.Equal(expected, ByteSize.Format(bytes));
}
