using System.Globalization;
using System.Text;

namespace Quiver.Core;

/// <summary>How names are matched to what the user types: without case or accents, word by word, the start of a name first.</summary>
public static class SearchText
{
    /// <summary>Lower case without accents, so "perche" finds "Perché".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>The words of a query, normalized; none for a blank query.</summary>
    public static string[] Words(string query) => Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// How well a name, with the path it sits in, matches the words of a query: 0 when a word is in neither. Every word
    /// must appear. Matches at the start of the name, or of a word in it, rank higher; among equal matches, shorter
    /// names come first.
    /// </summary>
    /// <param name="nameKey">The name, normalized.</param>
    /// <param name="pathKey">The path, normalized.</param>
    public static int Score(string nameKey, string pathKey, string[] words)
    {
        var score = 0;
        foreach (var word in words)
        {
            var index = nameKey.IndexOf(word, StringComparison.Ordinal);
            if (index == 0) score += 100;
            else if (index > 0) score += IsWordStart(nameKey, index) ? 70 : 40;
            else if (pathKey.Contains(word, StringComparison.Ordinal)) score += 10;
            else return 0;
        }
        return Math.Max(1, score - nameKey.Length / 4);
    }

    private static bool IsWordStart(string text, int index) =>
        !char.IsLetterOrDigit(text[index - 1]) || char.IsDigit(text[index]) != char.IsDigit(text[index - 1]);
}
