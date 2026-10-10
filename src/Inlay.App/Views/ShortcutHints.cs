namespace Inlay.App.Views;

/// <summary>What a key does, as the «?» of the status bar lists it, with the keys that do it.</summary>
public sealed record ShortcutHint(string Action, params IReadOnlyList<string> Keys);

/// <summary>The keys worth knowing, by what they act on.</summary>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutHint> Hints)
{
    /// <param name="hotkey">The shortcut that puts Inlay away, as the settings have it now.</param>
    public static IReadOnlyList<ShortcutGroup> For(string hotkey) =>
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
        ]),
        new("Ricerca",
        [
            new("cerca in tutto l'archivio, nei nomi e nelle cartelle; Esc torna indietro", "Scrivi"),
            new("solo un tipo: cartelle, documenti, pdf, immagini… o un'estensione", "tipo:pdf"),
            new("solo dentro un argomento o una cartella", "in:Lavoro"),
        ]),
        new("Tela",
        [
            new("tutto l'archivio in vista", "Ctrl+0"),
            new("avvicina, allontana", "Ctrl++", "Ctrl+-"),
        ]),
        new("Finestra",
        [
            new("mostra o nascondi gli argomenti", "Ctrl+B"),
            new("chiudi: Inlay resta nell'area di notifica", "Esc", hotkey),
        ]),
    ];
}
