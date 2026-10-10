using System.Text.Json;

namespace Inlay.App.Services;

/// <summary>
/// Where files were sent last, by archive, newest first: the first choices «Move to…» offers. Kept on this PC, outside
/// the archive, so that sorting a file never makes the archive itself change.
/// </summary>
internal sealed class RecentDestinations
{
    public const int Capacity = 8;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Inlay", "recenti.json");

    private readonly string _path;
    private readonly Dictionary<string, List<string>> _byArchive;

    private RecentDestinations(string path, Dictionary<string, List<string>> byArchive)
    {
        _path = path;
        _byArchive = byArchive;
    }

    /// <param name="path">Another file only in tests and previews.</param>
    public static RecentDestinations Load(string? path = null)
    {
        path ??= FilePath;
        Dictionary<string, List<string>?>? loaded = null;
        try
        {
            if (File.Exists(path))
                loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>?>>(File.ReadAllText(path), Options);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Write($"Recent destinations could not be read, starting afresh: {e.Message}");
        }
        var byArchive = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (archive, keys) in loaded ?? [])
            byArchive[archive] = (keys ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Take(Capacity).ToList();
        return new RecentDestinations(path, byArchive);
    }

    /// <summary>The metadata keys of the folders files went to last in this archive, newest first.</summary>
    public IReadOnlyList<string> For(string archiveRoot) =>
        _byArchive.TryGetValue(Normalize(archiveRoot), out var keys) ? keys : [];

    /// <summary>A folder files were just sent to: first of the list, which is saved.</summary>
    public void Remember(string archiveRoot, string relativeKey)
    {
        var root = Normalize(archiveRoot);
        if (!_byArchive.TryGetValue(root, out var keys))
            _byArchive[root] = keys = [];
        keys.RemoveAll(key => string.Equals(key, relativeKey, StringComparison.OrdinalIgnoreCase));
        keys.Insert(0, relativeKey);
        if (keys.Count > Capacity)
            keys.RemoveRange(Capacity, keys.Count - Capacity);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_byArchive, Options));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"Recent destinations could not be saved: {e.Message}");
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
