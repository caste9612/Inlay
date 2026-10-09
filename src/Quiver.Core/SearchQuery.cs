namespace Quiver.Core;

/// <summary>
/// A kind of item the search can be narrowed to: written "tipo:pdf" in the search box, or chosen among the filters above
/// the results. The start of its name, or of another way to say it, is enough: "tipo:imm", "tipo:foto".
/// </summary>
public sealed class SearchKind
{
    private readonly Func<ArchiveItem, FileKind, bool> _matches;

    private SearchKind(string name, string label, string[] aliases, Func<ArchiveItem, FileKind, bool> matches)
    {
        Name = name;
        Label = label;
        Aliases = aliases;
        _matches = matches;
    }

    /// <summary>What follows "tipo:", as "pdf".</summary>
    public string Name { get; }

    /// <summary>As the filters above the results show it, as "PDF".</summary>
    public string Label { get; }

    /// <summary>Other ways to write it, as "foto" for the images.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>In the order of the filters above the results: the most common first.</summary>
    public static IReadOnlyList<SearchKind> All { get; } =
    [
        new("cartelle", "Cartelle", ["cartella"], (item, _) => item.IsFolder),
        new("documenti", "Documenti", ["documento", "testi", "testo", "word"], Of(FileKind.Word, FileKind.Text)),
        new("pdf", "PDF", [], Of(FileKind.Pdf)),
        new("fogli", "Fogli di calcolo", ["foglio", "calcolo", "excel"], Of(FileKind.Spreadsheet)),
        new("presentazioni", "Presentazioni", ["presentazione", "powerpoint"], Of(FileKind.Presentation)),
        new("immagini", "Immagini", ["immagine", "foto"], Of(FileKind.Image)),
        new("video", "Video", ["filmati"], Of(FileKind.Video)),
        new("audio", "Audio", ["musica"], Of(FileKind.Audio)),
        new("collegamenti", "Collegamenti", ["collegamento", "link", "programmi"], (item, _) => !item.IsFolder && ArchiveItem.IsShortcut(item.Name)),
        new("compressi", "Compressi", ["zip"], Of(FileKind.Archive)),
        new("posta", "Posta", ["mail", "email"], Of(FileKind.Mail)),
        new("codice", "Codice", [], Of(FileKind.Code)),
        new("3d", "Modelli 3D", ["modelli"], Of(FileKind.Model3D)),
    ];

    /// <param name="kind">Its kind, for its icon: <see cref="FileKinds.Of"/>.</param>
    public bool Matches(ArchiveItem item, FileKind kind) => _matches(item, kind);

    /// <summary>The kinds a value after "tipo:" stands for: those whose name, or another way to say it, starts with it.</summary>
    public static IReadOnlyList<SearchKind> For(string value)
    {
        var key = SearchText.Normalize(value.Trim());
        return key.Length == 0
            ? []
            : All.Where(kind => kind.Name.StartsWith(key, StringComparison.Ordinal) ||
                                kind.Aliases.Any(alias => alias.StartsWith(key, StringComparison.Ordinal)))
                .ToList();
    }

    public override string ToString() => Name;

    private static Func<ArchiveItem, FileKind, bool> Of(params FileKind[] kinds) => (_, kind) => kinds.Contains(kind);
}

/// <summary>
/// What is typed in the search box: words to find in the names or in the folders around them, and filters written
/// "tipo:pdf" or "in:Lavoro", in quotes when the value has spaces: in:"Foto vacanze". Keys and values go without case or
/// accents, as the words. Of several filters of the same sort one is enough; the words, the kinds and the places must
/// all hold.
/// </summary>
public sealed class SearchQuery
{
    public const string KindKey = "tipo";
    public const string PlaceKey = "in";

    private static readonly char[] PlaceSeparators = ['/', '\\', '›'];

    private readonly string[] _suffixes;

    private SearchQuery(List<string> words, List<SearchKind> kinds, List<string> extensions, List<string> places)
    {
        Words = words;
        Kinds = kinds;
        Extensions = extensions;
        Places = places;
        PlaceKeys = places.Select(KeyOfPlace).Where(key => key.Length > 0).ToList();
        _suffixes = extensions.Select(extension => "." + extension).ToArray();
    }

    /// <summary>Normalized, as <see cref="SearchText.Words"/>.</summary>
    public IReadOnlyList<string> Words { get; }

    public IReadOnlyList<SearchKind> Kinds { get; }

    /// <summary>What followed "tipo:" without being a kind: an extension, without its dot, as "dwg".</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>What followed "in:", as written: "Lavoro", "Lavoro/Clienti".</summary>
    public IReadOnlyList<string> Places { get; }

    /// <summary>The places as the paths of the search have them: "lavoro\clienti".</summary>
    public IReadOnlyList<string> PlaceKeys { get; }

    public bool HasFilters => Kinds.Count > 0 || Extensions.Count > 0 || PlaceKeys.Count > 0;

    /// <summary>Nothing to look for: no words, no filters.</summary>
    public bool IsEmpty => Words.Count == 0 && !HasFilters;

    public static SearchQuery Parse(string text)
    {
        List<string> words = [];
        List<SearchKind> kinds = [];
        List<string> extensions = [];
        List<string> places = [];
        foreach (var token in Tokens(text))
        {
            switch (token.Key)
            {
                case KindKey when SearchKind.For(token.Value) is { Count: > 0 } found:
                    kinds.AddRange(found.Where(kind => !kinds.Contains(kind)));
                    break;
                case KindKey when ExtensionOf(token.Value) is { } extension:
                    extensions.Add(extension);
                    break;
                case PlaceKey when token.Value.Trim().Length > 0:
                    places.Add(token.Value.Trim());
                    break;
                case null:
                    words.AddRange(SearchText.Words(token.Value));
                    break;
            }
        }
        return new SearchQuery(words, kinds, extensions, places);
    }

    /// <summary>Whether an item passes the filters: of one of the kinds asked, and in one of the places.</summary>
    /// <param name="kind">Its kind: <see cref="FileKinds.Of"/>.</param>
    /// <param name="pathKey">The folder it is in, relative to the root, normalized as <see cref="SearchText.Normalize"/>.</param>
    public bool Admits(ArchiveItem item, FileKind kind, string pathKey) => IsOfAKindAsked(item, kind) && IsInAPlaceAsked(pathKey);

    private bool IsOfAKindAsked(ArchiveItem item, FileKind kind) =>
        (Kinds.Count == 0 && _suffixes.Length == 0) ||
        Kinds.Any(wanted => wanted.Matches(item, kind)) ||
        (!item.IsFolder && _suffixes.Any(suffix => item.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));

    private bool IsInAPlaceAsked(string pathKey) => PlaceKeys.Count == 0 || PlaceKeys.Any(place => IsIn(pathKey, place));

    /// <summary>The text with the filter of this kind added or, when it is there, taken away: what a filter above the results does.</summary>
    public static string ToggleKind(string text, SearchKind kind)
    {
        var tokens = Tokens(text);
        var filtering = tokens.Where(token => token.Key == KindKey && SearchKind.For(token.Value).Contains(kind)).ToList();
        return filtering.Count > 0 ? Without(text, tokens, filtering) : With(text, $"{KindKey}:{kind.Name}");
    }

    /// <summary>The text looking only in this place, or everywhere with null: the places written before go.</summary>
    /// <param name="place">A path relative to the root, as "Lavoro/Clienti".</param>
    public static string WithPlace(string text, string? place)
    {
        var tokens = Tokens(text);
        var rest = Without(text, tokens, tokens.Where(token => token.Key == PlaceKey).ToList());
        return place is null ? rest : With(rest, $"{PlaceKey}:{(place.Any(char.IsWhiteSpace) ? $"\"{place}\"" : place)}");
    }

    /// <summary>Whether two places are the same, however written: "Lavoro/Clienti" and "lavoro › clienti".</summary>
    public static bool SamePlace(string a, string b) => KeyOfPlace(a) == KeyOfPlace(b);

    /// <summary>A place as the user reads it: "Lavoro › Clienti".</summary>
    public static string Display(string place) =>
        string.Join(ArchiveReader.PathSeparator, place.Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Whether a folder, or one around it, is the place: whole names, as "clienti" in "lavoro\clienti\rossi".</summary>
    private static bool IsIn(string pathKey, string placeKey)
    {
        for (var index = pathKey.IndexOf(placeKey, StringComparison.Ordinal); index >= 0;
             index = pathKey.IndexOf(placeKey, index + 1, StringComparison.Ordinal))
        {
            var end = index + placeKey.Length;
            if ((index == 0 || pathKey[index - 1] == Path.DirectorySeparatorChar) &&
                (end == pathKey.Length || pathKey[end] == Path.DirectorySeparatorChar))
                return true;
        }
        return false;
    }

    private static string KeyOfPlace(string place) =>
        string.Join(Path.DirectorySeparatorChar,
            SearchText.Normalize(place).Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>"dwg" for "dwg" or ".DWG": letters and digits only, as the ends of file names.</summary>
    private static string? ExtensionOf(string value)
    {
        var extension = SearchText.Normalize(value.Trim()).TrimStart('.');
        return extension.Length is > 0 and <= 10 && extension.All(char.IsLetterOrDigit) ? extension : null;
    }

    private static string Without(string text, List<Token> tokens, List<Token> removed) =>
        string.Join(' ', tokens.Except(removed).Select(token => text.Substring(token.Start, token.Length)));

    private static string With(string text, string filter) => text.TrimEnd() is { Length: > 0 } rest ? $"{rest} {filter}" : filter;

    /// <summary>The pieces of the text, each where it is: words, and filters with their key and value.</summary>
    private static List<Token> Tokens(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ':')
                i++;
            var key = i < text.Length && text[i] == ':' ? KeyOf(text[start..i]) : null;
            if (key is null)
            {
                // A word, colons and all, up to the next space.
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                tokens.Add(new Token(start, i - start, null, text[start..i]));
                continue;
            }
            i++;
            string value;
            if (i < text.Length && text[i] == '"')
            {
                // In quotes, spaces and all; a quote left open goes to the end.
                var close = text.IndexOf('"', i + 1);
                value = close < 0 ? text[(i + 1)..] : text[(i + 1)..close];
                i = close < 0 ? text.Length : close + 1;
            }
            else
            {
                var valueStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                value = text[valueStart..i];
            }
            tokens.Add(new Token(start, i - start, key, value));
        }
        return tokens;
    }

    /// <summary>"tipo" or "in", however written; null for the rest, which makes a word.</summary>
    private static string? KeyOf(string text) => SearchText.Normalize(text) switch
    {
        KindKey => KindKey,
        PlaceKey => PlaceKey,
        _ => null,
    };

    /// <param name="Key">Null for a word.</param>
    private sealed record Token(int Start, int Length, string? Key, string Value);
}
