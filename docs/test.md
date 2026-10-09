# Test e verifiche

Come si controlla che Quiver funzioni: i test automatici, le anteprime da riga di comando, la misura della tela, l'archivio di prova e il log. Cosa coprono i test, visto da chi usa Quiver, è anche nel [README](../README.md#test-e-diagnostica).

## I test automatici

```powershell
dotnet test
```

Dalla radice del repository esegue i due progetti di test della soluzione `Quiver.slnx`. Servono Windows e l'SDK di .NET 10: i test dell'app usano WPF, la shell di Windows e thread STA.

Per eseguirne una parte:

```powershell
dotnet test tests/Quiver.Tests
dotnet test tests/Quiver.App.Tests --filter "FullyQualifiedName~MainViewModelTests"
```

Il 10 ottobre 2026 passano tutti: 158 in `Quiver.Tests` (meno di un decimo di secondo) e 136 in `Quiver.App.Tests` (3-4 secondi), 294 in tutto. Alla 0.3.2 erano 290, come dice la sua nota; gli altri 4 sono arrivati con le correzioni del 9 e 10 ottobre. Ogni nota di versione riporta quanti sono i test e che passano tutti.

I test sono scritti con xUnit. I nomi dicono, come frasi, cosa deve succedere: `Undo_LeavesAloneWhatChangedSince`, `ATopicDroppedInsideAnother_TakesItsFolderThere_StillATopic_AndCtrlZBringsItBack`.

### `tests/Quiver.Tests`: il Core

Lavora su archivi veri in cartelle temporanee: `TempArchive` crea una cartella in `%TEMP%\quiver-tests\<guid>`, ci aggiunge cartelle e file, dà un `ArchiveReader` e la cancella alla fine.

| File | Cosa controlla |
|---|---|
| `ArchiveReaderTests`, `DestinationsTests` | argomenti, riquadri, «Generale», ordini, «Da smistare» con il Desktop, destinazioni di «Sposta in…» |
| `MetadataStoreTests`, `MetadataSnapshotTests` | lettura e scrittura di `archive.json`, chiavi senza maiuscole, voci che seguono le cartelle, copie per annullare, file rovinato messo da parte |
| `TransferRulesTests` | spostare, copiare o collegare, con i tasti e senza |
| `RecycleBinTests` | il ripristino dal Cestino, su un Cestino finto costruito dal test con i suoi `$I…` e `$R…` |
| `SearchIndexTests`, `SearchTextTests` | ricerca, punteggi, accenti, nascosti esclusi, Desktop compreso |
| `ArchiveFoldersTests` | quali cartelle possono essere archivi, e il confronto byte per byte di una copia |
| `ItemArrangementTests`, `NaturalStringComparerTests` | ordinamenti e ordine a mano |
| `TopicColorTests`, `TileColorTests` | i colori di argomenti e riquadri |
| `CanvasPlacementTests` | la disposizione delle zone, senza sovrapposizioni |
| `ArchiveNamesTests`, `FileKindTests`, `FolderSizeTests`, `WebShortcutTests`, `ByteSizeTests` | nomi, tipi di file, dimensioni, collegamenti ai siti |

### `tests/Quiver.App.Tests`: l'app

Vede le parti interne dell'app (`InternalsVisibleTo`). Gli archivi stanno in `%TEMP%\quiver-app-tests\<guid>`, le destinazioni recenti in un file accanto, mai in quello del PC.

| File | Cosa controlla |
|---|---|
| `MainViewModelTests` (57 test) | rinomina, selezione dopo le ricariche, nuovi riquadri e cartelle, `Ctrl+Z` (per spostamenti, rinomine, creazioni, ordini, misure, ordinamenti, promozioni e colori: non per copie, collegamenti ed eliminazioni, che userebbero il Cestino vero), argomenti trascinati, «Sposta in…», avvisi, barra di stato e tasti da sapere, spazio su disco, colori, la tela |
| `BackgroundOperationsTests` | le operazioni sul thread a parte, con un dispatcher come quello della finestra: la finestra resta libera, e un `Ctrl+Z` dato nel frattempo aspetta e annulla |
| `FileOperationQueueTests` | la coda: un'operazione dopo l'altra, su un thread STA |
| `ShellFileOperationsTests` | spostare, copiare e rinominare con la shell vera, senza finestre (`interactive: false`); eliminare no, perché riempirebbe il Cestino |
| `ShellLinksTests`, `VirtualFilesTests`, `ShellIconsTests` | collegamenti, allegati e link trascinati, icone di Windows |
| `ArchivesViewModelTests`, `SettingsViewModelTests`, `AppSettingsTests` | archivi (creare, aprire, rinominare, spostare anche su un altro disco, con la copia su un thread a parte), impostazioni, salvataggio, file delle impostazioni rovinato messo da parte |
| `DestinationPickerViewModelTests`, `RecentDestinationsTests` | l'elenco di «Sposta in…» e le destinazioni recenti |
| `CanvasCameraTests`, `CanvasWakeTests`, `TileListTests` | la camera, e le righe dei riquadri che arrivano quando servono |
| `CoverPanelTests` | la scheda vista da lontano non fa mai crescere il riquadro che copre |
| `PanelMenusTests` | costruisce tutti i menu del tasto destro con i loro dati e ne prepara le voci, senza mostrarli: un binding che non si può attaccare fa fallire il test (è nato dall'errore della 0.3.0) |
| `SendToShortcutTests`, `SingleInstanceProtocolTests`, `CommandLineTests`, `WindowToggleTests`, `DiskSpaceTests` | «Invia a», il passaggio dei comandi tra istanze, la riga di comando, la scorciatoia, lo spazio su disco |

Gli aiuti comuni:

- **`StaThread.Run`** esegue un test su un thread STA, come vuole la shell; **`StaThread.RunAsync`** ci mette anche un dispatcher, così le continuazioni tornano su quel thread come nell'app.
- **`TestLog`** manda il log dei test in `%TEMP%\quiver-app-tests\quiver.log`, una volta sola quando si carica l'assembly dei test, prima di qualunque test (un *module initializer*): il log dell'utente parla solo di Quiver.
- **`AppResources.Load`** carica i dizionari di Brace e di Quiver in una `Application` WPF qualunque, mai in quella di Quiver: il suo avvio troverebbe il Quiver dell'utente in esecuzione e gli manderebbe un comando. I test che costruiscono le viste stanno nella collezione `AppViews` («Viste dell'app») e girano uno alla volta, perché le risorse sono di una sola applicazione.
- **Le dipendenze finte**: `MainViewModel` con `live: false` (le operazioni avvengono subito, sul thread del test), `DiskOf` per fingere lo spazio libero, una cartella al posto del Desktop; `ISettingsHost` e `IWindowsIntegration` finti per le impostazioni; `sameDrive` e `diskOf` per spostare un archivio «su un altro disco» senza averne uno.

### Scrivere un test nuovo

- Una regola del Core va in `Quiver.Tests`, con `TempArchive` se serve il disco.
- Un comportamento del pannello va in `MainViewModelTests`: si crea l'archivio nel costruttore, si apre con `Open()` dentro `StaThread.Run`, si agisce sul view model come farebbe la vista e si controllano file, metadati e view model.
- Un test che costruisce viste WPF chiama `AppResources.Load()` e va nella collezione `AppViews`.
- Niente Cestino vero, niente impostazioni, registro o cartelle dell'utente: tutto passa da cartelle temporanee e da dipendenze finte.

## Verificare l'interfaccia senza aprirla

`Quiver.exe --archive <cartella> --render-preview <file.png>` disegna il pannello in un'immagine, senza finestre, ed esce; senza `--archive` non disegna niente. Si può guardare l'immagine per controllare un cambiamento dell'interfaccia. Da sorgente:

```powershell
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --render-preview pannello.png --topic Lavoro/Clienti
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --render-preview tela.png --canvas --zoom 0.5
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --render-preview impostazioni.png --settings --section hotkey
```

- L'anteprima usa `MainViewModel` con `live: false` e `readOnly: true`: legge l'archivio e non scrive niente, nemmeno i colori nuovi degli argomenti. Le destinazioni recenti stanno in un file temporaneo suo (`quiver-preview-recenti.json`), le impostazioni sono inventate (l'archivio dato più uno che non c'è), e quello che Windows direbbe (avvio, «Invia a») è finto.
- Il pannello è grande 1240 × 780 punti, la finestra Impostazioni 960 × 650. Le icone di Windows si leggono subito (`ShellIcons.Synchronous`), e sulla tela le righe dei riquadri in vista si creano tutte (`WakeInViewNow`).
- Le opzioni sono elencate in [windows.md](windows.md#la-riga-di-comando-commandlineoptions). Se qualcosa va storto, l'errore finisce in `<file.png>.error.txt` e il codice di uscita è 1.

### L'archivio di prova

```powershell
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --desktop D:\DesktopDiProva
```

Lo script si rifiuta di usare una cartella che esiste già. Crea gli argomenti Lavoro (con i sotto-argomenti Clienti, Rossi, Bianchi e Interni, segnati nei metadati), Personale e Strumenti, con file, cartelle, collegamenti a programmi di Windows e a una cartella, un `.url`, e qualcosa da smistare. `--archive` vale solo per quell'avvio; `--desktop` mostra una cartella di prova al posto del Desktop, così trascinando non si spostano i propri file.

### Misurare la tela

`Quiver.exe --archive <cartella> --benchmark-canvas <file.txt>` (`Services/CanvasBenchmark.cs`) apre la tela dell'archivio in una finestra grande come l'area di lavoro, senza prendere il focus (è una finestra di strumenti, che GlazeWM lascia stare), e la muove per otto secondi sempre sullo stesso percorso: avvicinamento, spostamento da vicino, allontanamento, zoom avanti e indietro, due secondi ciascuno. Nel file scrive:

- la misura della tela in pixel, la scala e il livello di accelerazione grafica di WPF;
- quante zone, riquadri ed elementi ha l'archivio, e quanto ci ha messo la tela ad aprirsi;
- per ogni parte i fotogrammi al secondo, la mediana, il 95° percentile, il peggiore e quanti superano 33 ms;
- per ogni fotogramma oltre 100 ms cosa è successo prima: cambio di livello, liste svegliate, raccolte della memoria;
- la memoria all'inizio e alla fine.

L'archivio viene solo letto, e funziona anche con Quiver già aperto.

## Prima di pubblicare

Dal piano della tela ([PROGETTO.md §11](../PROGETTO.md#fasi)): ogni fase si chiude con i test e le anteprime e, prima di pubblicare, con la prova che l'archivio vero si legge come prima. Nel repository non c'è uno strumento per quest'ultima: le note di versione dicono che è stata fatta confrontando, in sola lettura, quello che leggono la versione vecchia e quella nuova.

Molte novità sono verificate con i test e le anteprime ma non ancora provate dal vivo: l'elenco di cosa provare è in [PROGETTO.md §10](../PROGETTO.md#10-prossimi-passi).

## Il log

`%LOCALAPPDATA%\Quiver\quiver.log` (`Services/Log.cs`): una riga per evento, con data e ora; oltre 1 MB ricomincia da capo. Ci finiscono:

- l'avvio, con l'archivio e la scorciatoia, e i cambi di archivio e di scorciatoia;
- quanto ci ha messo il pannello ad aprirsi (`Panel shown in … ms`) e perché si è nascosto (`Window hidden: …`);
- quale finestra teneva il focus quando Quiver non è riuscito a prenderlo;
- le attenzioni e gli errori detti negli avvisi (`Told: …`) e gli errori imprevisti, con lo stack;
- i problemi con impostazioni, metadati, indice, watcher, collegamenti e comandi delle altre istanze.
