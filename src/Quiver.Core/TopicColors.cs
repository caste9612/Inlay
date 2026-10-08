namespace Quiver.Core;

/// <summary>
/// The colors of the topics. A topic of the root takes one of the palette of Brace the first time Quiver sees it, the one
/// least used by the others, and keeps it in the archive: moving or renaming it does not change it. A topic inside another
/// one shows that of its topic, unless one was chosen for it.
/// </summary>
public static class TopicColors
{
    /// <summary>Kept for a topic left without a color on purpose: it is not given one again.</summary>
    public const string None = "none";

    /// <summary>
    /// The order Quiver gives them in: blue first, the amber of the accent later, red last; grey only by hand, since it
    /// looks like no color at all.
    /// </summary>
    public static IReadOnlyList<string> Automatic { get; } = ["#5DA9FF", "#36CFB0", "#B48CFF", "#FF7EB6", "#F2D24B", "#FFA133", "#FF7272"];

    /// <summary>The automatic color least used among these, the first in order among equals.</summary>
    public static string LeastUsed(IEnumerable<string?> used)
    {
        var counts = Automatic.ToDictionary(color => color, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var color in used)
        {
            if (color is not null && counts.ContainsKey(color))
                counts[color]++;
        }
        // OrderBy keeps the order of the palette among equals.
        return Automatic.OrderBy(color => counts[color]).First();
    }

    /// <summary>Gives a color to the topics of the root that have none yet, one after the other in their order.</summary>
    /// <param name="rootTopicKeys">The metadata keys of the topics of the root, in the order of the sidebar.</param>
    /// <returns>Whether anything changed.</returns>
    public static bool AssignMissing(ArchiveMetadata metadata, IReadOnlyList<string> rootTopicKeys)
    {
        var changed = false;
        foreach (var key in rootTopicKeys)
        {
            if (metadata.ColorOf(key) is not null)
                continue;
            var others = rootTopicKeys.Where(other => !string.Equals(other, key, StringComparison.OrdinalIgnoreCase)).Select(metadata.ColorOf);
            changed |= metadata.SetColor(key, LeastUsed(others), automatic: true);
        }
        return changed;
    }
}
