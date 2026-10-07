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

    public string? ArchiveRoot { get; set; }

    /// <summary>Written like "Alt+E" or "Ctrl+Shift+Space".</summary>
    public string Hotkey { get; set; } = "Alt+E";

    /// <summary>The files on the Desktop appear among those to sort.</summary>
    public bool InboxDesktop { get; set; } = true;

    /// <summary>The files in Downloads appear among those to sort.</summary>
    public bool InboxDownloads { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (JsonException e)
        {
            Log.Write($"Settings file is not valid JSON, using defaults: {e.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }
}
