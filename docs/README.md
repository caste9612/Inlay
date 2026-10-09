# Quiver: la documentazione del codice

Questi documenti spiegano com'è fatto il codice di Quiver e come ci si lavora. Cosa fa Quiver e come si usa è nel [README](../README.md); perché esiste, le sue regole e dove sta andando sono in [PROGETTO.md](../PROGETTO.md); cosa porta ogni versione è in [novita/](../novita).

Scritti il 9 e il 10 ottobre 2026 leggendo tutto il codice della versione 0.3.2, poi controllati contro il codice e aggiornati con le correzioni fatte in quei giorni ([PROGETTO.md §10](../PROGETTO.md#10-prossimi-passi)).

## Da dove cominciare

| Documento | Cosa spiega |
|---|---|
| [architettura.md](architettura.md) | la soluzione, gli strati, dove sta ogni cosa, i thread, le scelte principali |
| [archivio.md](archivio.md) | `Quiver.Core`: come si legge l'archivio, i metadati, le regole, il Cestino, la ricerca |
| [operazioni-sui-file.md](operazioni-sui-file.md) | spostare, copiare, collegare, rinominare, eliminare, trascinare e annullare |
| [interfaccia.md](interfaccia.md) | il pannello: view model, ricarica, ricerca, selezione, tastiera, menu, avvisi, impostazioni, Brace |
| [tela.md](tela.md) | la tela: zone, camera, livelli, righe create quando servono, cosa manca |
| [windows.md](windows.md) | avvio, finestra, istanza unica, scorciatoia, tray, shell di Windows, file sul PC, riga di comando |
| [test.md](test.md) | i test, le anteprime da riga di comando, l'archivio di prova, la misura della tela, il log |
| [rilascio.md](rilascio.md) | versioni, note di versione, installer e release su GitHub |
| [convenzioni.md](convenzioni.md) | lingue, stile del codice e dei testi, principi da non rompere, Brace, documenti, git |
| [come-fare.md](come-fare.md) | ricette: un'operazione che si annulla, un campo nei metadati, una voce di menu, un tasto, un'impostazione… |

## Per partire

Serve Windows 11 con l'SDK di .NET 10.

```powershell
dotnet build Quiver.slnx
dotnet test
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --desktop D:\DesktopDiProva
```

- `--archive` usa quell'archivio solo per questo avvio; `--desktop` mostra una cartella di prova al posto del Desktop, così non si spostano i propri file.
- Se Quiver è già aperto (quello installato, per esempio), un secondo Quiver gli passa il comando e si chiude: per provare il codice nuovo bisogna prima uscire da quello aperto (**Esci** nel menu della tray). Le anteprime (`--render-preview`) e la misura della tela (`--benchmark-canvas`) invece funzionano anche con Quiver aperto.
- Per guardare un cambiamento dell'interfaccia senza aprire finestre: `--render-preview pannello.png` ([test.md](test.md#verificare-linterfaccia-senza-aprirla)).

## Glossario

L'interfaccia e i documenti parlano italiano, il codice inglese. Le parole sono sempre le stesse:

| Nell'interfaccia | Nel codice | Cos'è |
|---|---|---|
| archivio | archive, `Root` | la cartella che Quiver gestisce, segnata dalla cartella nascosta `.quiver` |
| argomento | `Topic`, `TopicViewModel` | una cartella nella barra laterale: quelle della radice, e quelle segnate `"kind": "topic"` |
| sotto-argomento | `Topic.Children` | un argomento dentro un altro |
| riquadro | `Tile`, `TileViewModel` | una sottocartella di un argomento, sulla sua bacheca |
| «Generale» | `Tile.IsGeneral` | il riquadro con i file sciolti dell'argomento |
| elemento | `ArchiveItem`, `ItemViewModel` | un file, una cartella o un collegamento dentro un riquadro |
| bacheca | `Board`, `MainViewModel.Tiles` | i riquadri di un argomento |
| tela | canvas, `CanvasViewModel`, `CanvasSurface` | tutto l'archivio su una superficie da zoomare |
| zona | zone, `ZoneViewModel` | un argomento, o «Da smistare», sulla tela |
| livello (lontano, medio, vicino) | `CanvasLevel` (`Far`, `Middle`, `Near`) | cosa mostra la tela secondo lo zoom |
| «Da smistare» | inbox, `InboxSource`, `_Da smistare` | i file da mettere a posto: nella radice, in `_Da smistare`, sul Desktop, in Download |
| provenienza | `InboxSource` | un posto fuori dall'archivio mostrato in «Da smistare», come il Desktop |
| metadati | `ArchiveMetadata`, `FolderMetadata`, `archive.json` | quello che le cartelle non sanno dire: argomenti, ordine, colori, misure |
| chiave | relative key | il percorso di una cartella relativo alla radice, con le `/` |
| collegamento | shortcut, `.lnk`, `.url`, `TransferKind.Link` | quello con cui entra ciò che ha già un suo posto |
| «Sposta in…» | `DestinationPickerViewModel`, `Destination` | l'elenco dei posti dove mandare la selezione |
| «Invia a Quiver» | `SendToShortcut`, `--sort` | la voce nel menu «Invia a» di Explorer |
| annullare, passo | undo, `UndoStep`, `UndoHistory` | `Ctrl+Z` e quello che ricorda |
| avviso | toast, `ToastViewModel` | il messaggio in basso a destra |
| domanda | prompt, `PromptViewModel` | una conferma o un nome da scrivere, sopra la bacheca |
| ordinamento, ordine a mano | `ItemSort`, `ByHand`, `Items` | come sono messi gli elementi di un riquadro |
| misura | `TileSize` | le colonne e le righe di un riquadro |
| promuovere, riportare a riquadro | promote, demote | far diventare una sottocartella un argomento, e viceversa |
| scorciatoia | hotkey, `GlobalHotkey` | il tasto che porta Quiver davanti (`Alt+E`) |
| area di notifica, tray | `TrayIcon` | dove Quiver aspetta |
| finestra, pannello | `PanelWindow`, `PanelView` | la finestra di Quiver; «pannello» è il nome di quando compariva e spariva |
