using System.Text;

namespace Inlay.Core;

/// <summary>Shortcuts to websites (.url), written the way Windows and the browsers write them.</summary>
public static class WebShortcut
{
    /// <summary>An address typed by hand, with or without "https://"; null when it does not look like a website.</summary>
    public static Uri? Parse(string text)
    {
        text = text.Trim().Trim('"').Trim();
        // Spaces and backslashes belong to paths, like \\server\share or C:\Program Files.
        if (text.Length == 0 || text.Contains(' ') || text.Contains('\\'))
            return null;
        // "www.example.com" or "example.com/page" are addresses too, as in the browser's address bar.
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var address) &&
               address.Scheme is "http" or "https" &&
               (address.Host.Contains('.') || address.IsLoopback)
            ? address
            : null;
    }

    /// <summary>Writes a shortcut to <paramref name="address"/> in <paramref name="folder"/>, named after the site.</summary>
    /// <returns>The new file.</returns>
    public static string Create(string folder, Uri address)
    {
        var host = address.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? address.Host[4..] : address.Host;
        var name = ArchiveNames.Validate(host) is null ? host : "Website";
        var path = Path.Combine(folder, ArchiveNames.Unique(folder, name, ".url"));
        // AbsoluteUri is plain ASCII: accented addresses become percent-encoded, as every reader of .url files expects.
        File.WriteAllText(path, $"[InternetShortcut]\r\nURL={address.AbsoluteUri}\r\n", Encoding.ASCII);
        return path;
    }
}
