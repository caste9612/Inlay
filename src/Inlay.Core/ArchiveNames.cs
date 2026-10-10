namespace Inlay.Core;

/// <summary>Which names Windows, and Inlay, accept for new files and folders.</summary>
public static class ArchiveNames
{
    private static readonly char[] ForbiddenCharacters = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <returns>Why the name cannot be used, or null when it is fine.</returns>
    public static string? Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Type a name.";
        if (name.IndexOfAny(ForbiddenCharacters) >= 0 || name.Any(char.IsControl))
            return "A name can't contain these characters: \\ / : * ? \" < > |";
        if (name.EndsWith('.') || name.EndsWith(' '))
            return "A name can't end with a dot or a space.";
        if (ArchiveLayout.IsHiddenName(name))
            return "Inlay doesn't show names that start with a dot.";
        if (ReservedNames.Contains(name.Split('.')[0].TrimEnd()))
            return $"“{name}” is a name reserved by Windows.";
        if (name.Length > 255)
            return "The name is too long.";
        return null;
    }

    /// <summary>A name not taken yet in the folder: the name itself, else "name (2)", "name (3)"… like Explorer.</summary>
    /// <param name="extension">For files: kept at the end, as in "Offer (2).lnk".</param>
    public static string Unique(string folder, string name, string extension = "")
    {
        if (!Path.Exists(Path.Combine(folder, name + extension)))
            return name + extension;
        for (var n = 2; ; n++)
        {
            var candidate = $"{name} ({n}){extension}";
            if (!Path.Exists(Path.Combine(folder, candidate)))
                return candidate;
        }
    }
}
