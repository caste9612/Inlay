namespace Inlay.Core;

/// <summary>
/// A kind of item the search can be narrowed to: written "type:pdf" in the search box, or chosen among the filters above
/// the results. The start of its name, or of another way to say it, is enough: "type:ima", "type:photos".
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

    /// <summary>What follows "type:", as "pdf".</summary>
    public string Name { get; }

    /// <summary>As the filters above the results show it, as "PDF".</summary>
    public string Label { get; }

    /// <summary>Other ways to write it, as "photos" for the images.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>In the order of the filters above the results: the most common first.</summary>
    public static IReadOnlyList<SearchKind> All { get; } =
    [
        new("folders", "Folders", ["folder"], (item, _) => item.IsFolder),
        new("documents", "Documents", ["document", "docs", "texts", "text", "word"], Of(FileKind.Word, FileKind.Text)),
        new("pdf", "PDF", [], Of(FileKind.Pdf)),
        new("spreadsheets", "Spreadsheets", ["spreadsheet", "sheets", "excel"], Of(FileKind.Spreadsheet)),
        new("presentations", "Presentations", ["presentation", "slides", "powerpoint"], Of(FileKind.Presentation)),
        new("images", "Images", ["image", "photos", "pictures"], Of(FileKind.Image)),
        new("video", "Video", ["videos", "movies"], Of(FileKind.Video)),
        new("audio", "Audio", ["music"], Of(FileKind.Audio)),
        new("shortcuts", "Shortcuts", ["shortcut", "links", "programs"], (item, _) => !item.IsFolder && ArchiveItem.IsShortcut(item.Name)),
        new("compressed", "Compressed", ["zip", "archives"], Of(FileKind.Archive)),
        new("mail", "Mail", ["email"], Of(FileKind.Mail)),
        new("code", "Code", [], Of(FileKind.Code)),
        new("3d", "3D models", ["models"], Of(FileKind.Model3D)),
    ];

    /// <param name="kind">Its kind, for its icon: <see cref="FileKinds.Of"/>.</param>
    public bool Matches(ArchiveItem item, FileKind kind) => _matches(item, kind);

    /// <summary>The kinds a value after "type:" stands for: those whose name, or another way to say it, starts with it.</summary>
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
/// "type:pdf" or "in:Work", in quotes when the value has spaces: in:"Holiday photos". Keys and values go without case or
/// accents, as the words. Of several filters of the same sort one is enough; the words, the kinds and the places must
/// all hold.
/// </summary>
public sealed class SearchQuery
{
    public const string KindKey = "type";
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

    /// <summary>What followed "type:" without being a kind: an extension, without its dot, as "dwg".</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>What followed "in:", as written: "Work", "Work/Clients".</summary>
    public IReadOnlyList<string> Places { get; }

    /// <summary>The places as the paths of the search have them: "work\clients".</summary>
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
        if (filtering.Count == 0)
            return With(text, tokens, $"{KindKey}:{kind.Name}");
        // A value that stood for several kinds, as "type:p" while it is written, leaves the others written out.
        return Rebuilt(text, tokens, token => filtering.Contains(token)
            ? string.Join(' ', SearchKind.For(token.Value).Where(other => other != kind).Select(other => $"{KindKey}:{other.Name}"))
            : null);
    }

    /// <summary>The text looking only in this place, or everywhere with null: the places written before go.</summary>
    /// <param name="place">
    /// A folder, or a path of folders, as "Work/Clients"; starting with "/", from the root of the archive only, as the
    /// topics chosen from the menu.
    /// </param>
    public static string WithPlace(string text, string? place)
    {
        var tokens = Tokens(text);
        var rest = Rebuilt(text, tokens, token => token.Key == PlaceKey ? "" : null);
        return place is null ? rest : With(rest, Tokens(rest), $"{PlaceKey}:{(place.Any(char.IsWhiteSpace) ? $"\"{place}\"" : place)}");
    }

    /// <summary>Whether two places are the same, however written: "Work/Clients" and "work › clients", but not "/Work".</summary>
    public static bool SamePlace(string a, string b) => KeyOfPlace(a) == KeyOfPlace(b);

    /// <summary>A place as the user reads it: "Work › Clients".</summary>
    public static string Display(string place) =>
        string.Join(ArchiveReader.PathSeparator, place.Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>
    /// Whether a folder, or one around it, is the place: whole names, as "clienti" in "lavoro\clienti\rossi"; from the
    /// root only for a place written from it, as "\lavoro".
    /// </summary>
    private static bool IsIn(string pathKey, string placeKey)
    {
        if (placeKey[0] == Path.DirectorySeparatorChar)
        {
            var fromRoot = placeKey[1..];
            return pathKey == fromRoot || pathKey.StartsWith(fromRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
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

    /// <summary>"work\clients" for "Work › Clients"; "\work" for "/Work", from the root; empty for no folder at all.</summary>
    private static string KeyOfPlace(string place)
    {
        var names = SearchText.Normalize(place).Split(PlaceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
            return "";
        var fromRoot = place.TrimStart().StartsWith('/') || place.TrimStart().StartsWith('\\');
        return (fromRoot ? Path.DirectorySeparatorChar.ToString() : "") + string.Join(Path.DirectorySeparatorChar, names);
    }

    /// <summary>"dwg" for "dwg" or ".DWG": letters and digits only, as the ends of file names.</summary>
    private static string? ExtensionOf(string value)
    {
        var extension = SearchText.Normalize(value.Trim()).TrimStart('.');
        return extension.Length is > 0 and <= 10 && extension.All(char.IsLetterOrDigit) ? extension : null;
    }

    /// <summary>The text again, piece by piece, one space between them.</summary>
    /// <param name="replace">What takes the place of a piece: empty to drop it, null to keep it as it was.</param>
    private static string Rebuilt(string text, List<Token> tokens, Func<Token, string?> replace) =>
        string.Join(' ', tokens.Select(token => replace(token) ?? text.Substring(token.Start, token.Length)).Where(piece => piece.Length > 0));

    /// <summary>A filter after the rest of the text; a quote left open there is closed first, or the filter would go inside it.</summary>
    private static string With(string text, List<Token> tokens, string filter)
    {
        var rest = text.TrimEnd();
        if (tokens.Count > 0 && tokens[^1].IsOpen)
            rest += "\"";
        return rest.Length > 0 ? $"{rest} {filter}" : filter;
    }

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
            var isOpen = false;
            if (i < text.Length && text[i] == '"')
            {
                // In quotes, spaces and all; a quote left open goes to the end.
                var close = text.IndexOf('"', i + 1);
                isOpen = close < 0;
                value = isOpen ? text[(i + 1)..] : text[(i + 1)..close];
                i = isOpen ? text.Length : close + 1;
            }
            else
            {
                var valueStart = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                    i++;
                value = text[valueStart..i];
            }
            tokens.Add(new Token(start, i - start, key, value, isOpen));
        }
        return tokens;
    }

    /// <summary>"type" or "in", however written; null for the rest, which makes a word.</summary>
    private static string? KeyOf(string text) => SearchText.Normalize(text) switch
    {
        KindKey => KindKey,
        PlaceKey => PlaceKey,
        _ => null,
    };

    /// <param name="Key">Null for a word.</param>
    /// <param name="IsOpen">Its value is in quotes never closed: it runs to the end of the text.</param>
    private sealed record Token(int Start, int Length, string? Key, string Value, bool IsOpen = false);
}
