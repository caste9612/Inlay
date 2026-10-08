namespace Quiver.App.Services;

/// <summary>Plain-text log in %LOCALAPPDATA%\Quiver, to diagnose problems on the user's machine.</summary>
internal static class Log
{
    private const long MaxSize = 1024 * 1024;
    private static readonly Lock Gate = new();

    /// <summary>Where it writes; the tests send it to a file of their own, so that the user's log tells only about Quiver.</summary>
    public static string FilePath { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quiver", "quiver.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                    File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
