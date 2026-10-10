# Inlay: la documentazione del codice

Questi documenti spiegano com'è fatto il codice di Inlay e come ci si lavora. Cosa fa Inlay e come si usa è nel [README](../README.md); perché esiste, le sue regole e dove sta andando sono in [PROGETTO.md](../PROGETTO.md); cosa porta ogni versione è in [novita/](../novita).

Scritti il 9 e il 10 ottobre 2026 leggendo tutto il codice della versione 0.3.2, poi controllati contro il codice e aggiornati con le correzioni e le novità di quei giorni, uscite con la 0.3.3, e con l'interfaccia in inglese della 0.3.4 ([PROGETTO.md §10](../PROGETTO.md#10-prossimi-passi)).

## Da dove cominciare

| Documento | Cosa spiega |
|---|---|
| [architettura.md](architettura.md) | la soluzione, gli strati, dove sta ogni cosa, i thread, le scelte principali |
| [archivio.md](archivio.md) | `Inlay.Core`: come si legge l'archivio, i metadati, le regole, il Cestino, la ricerca |
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
dotnet build Inlay.slnx
dotnet test
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo
```

- `--archive` usa quell'archivio solo per questo avvio, senza cambiare quello delle impostazioni.
- Se Inlay è già aperto (quello installato, per esempio), un secondo Inlay gli passa il comando e si chiude: per provare il codice nuovo bisogna prima uscire da quello aperto (**Esci** nel menu della tray). Le anteprime (`--render-preview`) e la misura della tela (`--benchmark-canvas`) invece funzionano anche con Inlay aperto.
- Per guardare un cambiamento dell'interfaccia senza aprire finestre: `--render-preview pannello.png` ([test.md](test.md#verificare-linterfaccia-senza-aprirla)).

## Glossario

Il codice e, dalla 0.3.4, l'interfaccia parlano inglese, i documenti italiano. L'interfaccia ha parole sue, legate all'intarsio, diverse da quelle del codice, che sono di prima. Le parole sono sempre le stesse:

| Nei documenti | Nell'interfaccia | Nel codice | Cos'è |
|---|---|---|---|
| archivio | vault, **Vaults** | archive, `Root` | la cartella che Inlay gestisce, segnata dalla cartella nascosta `.inlay` |
| argomento | space, **Spaces** | `Topic`, `TopicViewModel` | una cartella nella barra laterale: quelle della radice, e quelle segnate `"kind": "topic"` |
| sotto-argomento | space inside, **New space inside** | `Topic.Children` | un argomento dentro un altro |
| riquadro | tile | `Tile`, `TileViewModel` | una sottocartella di un argomento, sulla sua bacheca |
| «General» | General | `Tile.IsGeneral`, `GeneralTileTitle` | il riquadro con i file sciolti dell'argomento e le cartelle che ci stanno come righe |
| riga di «General» | **Fold into General**, **Unfold as tile** | `"kind": "item"`, `IsItem`, `CanBecomeTile` | una cartella di un argomento mostrata in «General» invece che come riquadro, come una lasciata sull'argomento |
| elemento | item | `ArchiveItem`, `ItemViewModel` | un file, una cartella o un collegamento dentro un riquadro |
| bacheca | **Board** | `Board`, `MainViewModel.Tiles` | i riquadri di un argomento |
| tela, mosaico | **Mosaic** | canvas, `CanvasViewModel`, `CanvasSurface` | tutto l'archivio su una superficie da zoomare; fino alla 0.3.3 «Tela» anche nell'interfaccia |
| zona | space | zone, `ZoneViewModel` | un argomento sulla tela |
| livello (lontano, medio, vicino) | | `CanvasLevel` (`Far`, `Middle`, `Near`) | cosa mostra la tela secondo lo zoom |
| file sciolti nella radice | loose files in the vault | `ReadRootFiles`, `RootFilesText` | i file lasciati nella cartella dell'archivio, fuori da ogni argomento: li conta la barra di stato |
| metadati | | `ArchiveMetadata`, `FolderMetadata`, `archive.json` | quello che le cartelle non sanno dire: argomenti, ordine, colori, misure |
| chiave | | relative key | il percorso di una cartella relativo alla radice, con le `/` |
| collegamento | shortcut | shortcut, `.lnk`, `.url`, `TransferKind.Link` | quello con cui entra ciò che ha già un suo posto |
| «Move to…» | **Move to…** | `DestinationPickerViewModel`, `Destination` | l'elenco dei posti dove mandare la selezione; fino alla 0.3.3 «Sposta in…» |
| filtri della ricerca | `type:`, `in:` | `SearchQuery`, `SearchKind` | il tipo degli elementi e il posto dove cercarli, come pulsanti sopra i risultati o scritti nella casella; fino alla 0.3.3 `tipo:` |
| «Invia a Inlay» | **Invia a** › **Inlay**, il menu di Windows | `SendToShortcut`, `--sort` | la voce nel menu «Invia a» di Explorer |
| annullare, passo | **Undo** | undo, `UndoStep`, `UndoHistory` | `Ctrl+Z` e quello che ricorda |
| avviso | | toast, `ToastViewModel` | il messaggio in basso a destra |
| domanda | | prompt, `PromptViewModel` | una conferma o un nome da scrivere, sopra la bacheca |
| ordinamento, ordine a mano | **Sort by**, by hand | `ItemSort`, `ByHand`, `Items` | come sono messi gli elementi di un riquadro |
| misura | size, **Auto size** | `TileSize` | le colonne e le righe di un riquadro |
| promuovere, riportare a riquadro | **Make it a space**, **Back to tile** | promote, demote | far diventare una sottocartella un argomento, e viceversa |
| scorciatoia | hotkey, **Hotkey** | hotkey, `GlobalHotkey` | il tasto che porta Inlay davanti (`Alt+E`) |
| area di notifica, tray | tray | `TrayIcon` | dove Inlay aspetta |
| finestra, pannello | | `PanelWindow`, `PanelView` | la finestra di Inlay; «pannello» è il nome di quando compariva e spariva |
