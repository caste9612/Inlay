using Inlay.App.Interop;
using Inlay.Core;

namespace Inlay.App.Services;

/// <summary>The disk that holds a folder: how big it is and how much of it is still free.</summary>
/// <param name="Name">How Explorer calls it: "E:", or the share for a folder on the network.</param>
/// <param name="Free">What this user can still write there, quotas included.</param>
/// <param name="Total">How big the disk is.</param>
internal sealed record DiskSpace(string Name, long Free, long Total)
{
    // Short of space under a tenth, as the bars of Explorer turn red, or under 5 GB whatever the size of the disk.
    private const double LowShare = 0.10;
    private const long LowBytes = 5L * 1024 * 1024 * 1024;

    public bool IsLow => Free < LowBytes || Free < Total * LowShare;

    /// <summary>"120 GB free".</summary>
    public string FreeText => $"{ByteSize.Format(Free)} free";

    /// <summary>"120 GB free of 465 GB".</summary>
    public string Room => $"{ByteSize.Format(Free)} free of {ByteSize.Format(Total)}";

    /// <summary>"Drive E: 120 GB free of 465 GB".</summary>
    public string Description => $"Drive {Name} {Room}";

    /// <summary>The disk of this folder; null when Windows cannot say, as for a drive no longer connected.</summary>
    public static DiskSpace? Of(string folder)
    {
        if (!NativeMethods.GetDiskFreeSpaceEx(folder, out var free, out var total, out _))
            return null;
        var root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
        return new DiskSpace(root.TrimEnd('\\'), (long)free, (long)total);
    }
}
