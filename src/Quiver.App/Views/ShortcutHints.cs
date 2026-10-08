namespace Quiver.App.Views;

/// <summary>What a key does, as the «?» of the status bar lists it, with the keys that do it.</summary>
public sealed record ShortcutHint(string Action, params IReadOnlyList<string> Keys);

/// <summary>The keys worth knowing, by what they act on.</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutHint> Hints)
{
    public static IReadOnlyList<ShortcutGroup> All { get; } =
    [
        new("Elementi",
        [
            new("apri", "Invio"),
            new("mostra in Explorer", "Ctrl+Invio"),
            new("rinomina", "F2"),
            new("sposta nel Cestino", "Canc"),
            new("proprietà", "Alt+Invio"),
            new("seleziona tutto il riquadro", "Ctrl+A"),
        ]),
        new("Appunti",
        [
            new("copia", "Ctrl+C"),
            new("taglia", "Ctrl+X"),
            new("incolla", "Ctrl+V"),
            new("copia il percorso", "Ctrl+Maiusc+C"),
        ]),
        new("Archivio",
        [
            new("annulla l'ultima operazione", "Ctrl+Z"),
            new("nuovo riquadro", "Ctrl+Maiusc+N"),
            new("cerca in tutto l'archivio; Esc torna indietro", "Scrivi"),
        ]),
        new("Tela",
        [
            new("tutto l'archivio in vista", "Ctrl+0"),
            new("avvicina, allontana", "Ctrl++", "Ctrl+-"),
        ]),
        new("Finestra",
        [
            new("mostra o nascondi gli argomenti", "Ctrl+B"),
            new("chiudi: Quiver resta nell'area di notifica", "Esc", "Alt+E"),
        ]),
    ];
}
