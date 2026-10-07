using System.Text.Json;

namespace Quiver.App.Services;

/// <summary>Settings of this PC, kept outside the archive in %APPDATA%\Quiver\settings.json.</summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quiver", "settings.json");

    /// <summary>The archive in use.</summary>
    public string? ArchiveRoot { get; set; }

    /// <summary>The archives this PC knows, as Obsidian knows its vaults: the most recently used first.</summary>
    public List<string> Archives { get; set; } = [];

    /// <summary>Written like "Alt+E" or "Ctrl+Shift+Space".</summary>
    public string Hotkey { get; set; } = "Alt+E";

    /// <summary>The files on the Desktop appear among those to sort.</summary>
    public bool InboxDesktop { get; set; } = true;

    /// <summary>The files in Downloads appear among those to sort.</summary>
    public bool InboxDownloads { get; set; }

    /// <summary>Where these settings are saved; another file only in tests.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string SavePath { get; set; } = FilePath;

    public static AppSettings Load(string? path = null)
    {
        path ??= FilePath;
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? settings;
        }
        catch (JsonException e)
        {
            Log.Write($"Settings file is not valid JSON, using defaults: {e.Message}");
        }
        settings.SavePath = path;
        // Settings from before the list of archives: the one in use is the first known.
        if (settings.ArchiveRoot is { } root && !settings.Archives.Contains(root, StringComparer.OrdinalIgnoreCase))
            settings.Archives.Insert(0, root);
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        File.WriteAllText(SavePath, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>The archive becomes the one in use, and the first of the list.</summary>
    public void UseArchive(string root)
    {
        Archives.RemoveAll(known => SamePath(known, root));
        Archives.Insert(0, root);
        ArchiveRoot = root;
    }

    /// <summary>Out of the list; its folder stays as it is.</summary>
    public void ForgetArchive(string root)
    {
        Archives.RemoveAll(known => SamePath(known, root));
        if (ArchiveRoot is { } current && SamePath(current, root))
            ArchiveRoot = null;
    }

    /// <summary>An archive renamed or moved: same place in the list, new folder.</summary>
    public void ReplaceArchive(string oldRoot, string newRoot)
    {
        var index = Archives.FindIndex(known => SamePath(known, oldRoot));
        if (index >= 0)
            Archives[index] = newRoot;
        else
            Archives.Insert(0, newRoot);
        if (ArchiveRoot is { } current && SamePath(current, oldRoot))
            ArchiveRoot = newRoot;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
