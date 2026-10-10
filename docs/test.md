# Test e verifiche

Come si controlla che Inlay funzioni: i test automatici, le anteprime da riga di comando, la misura della tela, l'archivio di prova e il log. Cosa coprono i test, visto da chi usa Inlay, è anche nel [README](../README.md#test-e-diagnostica).

## I test automatici

```powershell
dotnet test
```

Dalla radice del repository esegue i due progetti di test della soluzione `Inlay.slnx`. Servono Windows e l'SDK di .NET 10: i test dell'app usano WPF, la shell di Windows e thread STA.

Per eseguirne una parte:

```powershell
dotnet test tests/Inlay.Tests
dotnet test tests/Inlay.App.Tests --filter "FullyQualifiedName~MainViewModelTests"
```

Il 10 ottobre 2026 passano tutti: 180 in `Inlay.Tests` (meno di un decimo di secondo) e 153 in `Inlay.App.Tests` (4-6 secondi), 333 in tutto, come dice la nota della 0.3.5; quelle della 0.3.3 e della 0.3.4 ne dicono 330, e 3 sono arrivati con la pagina **About**. Alla 0.3.2 erano 290, come dice la sua nota; poi ne sono arrivati 4 con le correzioni del 9 e 10 ottobre, se ne sono andati quelli di «Da smistare», tolto, e ne sono arrivati 10 con le righe di «Generale», 8 in più con le operazioni che vanno insieme, 10 con i filtri della ricerca, 8 con le correzioni della rilettura che ne è seguita, 5 con il passaggio da Quiver a Inlay e 2 con gli aggiornamenti. Ogni nota di versione riporta quanti sono i test e che passano tutti.

I test sono scritti con xUnit. I nomi dicono, come frasi, cosa deve succedere: `Undo_LeavesAloneWhatChangedSince`, `ATopicDroppedInsideAnother_TakesItsFolderThere_StillATopic_AndCtrlZBringsItBack`.

### `tests/Inlay.Tests`: il Core

Lavora su archivi veri in cartelle temporanee: `TempArchive` crea una cartella in `%TEMP%\inlay-tests\<guid>`, ci aggiunge cartelle e file, dà un `ArchiveReader` e la cancella alla fine.

| File | Cosa controlla |
|---|---|
| `ArchiveReaderTests`, `DestinationsTests` | argomenti, riquadri, «General» e le sue righe, ordini, file sciolti nella radice, destinazioni di «Move to…» |
| `MetadataStoreTests`, `MetadataSnapshotTests` | lettura e scrittura di `archive.json`, chiavi senza maiuscole, voci che seguono le cartelle, copie per annullare, file rovinato messo da parte |
| `TransferRulesTests` | spostare, copiare o collegare, con i tasti e senza |
| `FileTouchesTests` | quali operazioni sui file vanno insieme e quali aspettano quella prima |
| `UndoHistoryTests` | il registro in ordine di richiesta, e i passi trovati per identità |
| `RecycleBinTests` | il ripristino dal Cestino, su un Cestino finto costruito dal test con i suoi `$I…` e `$R…` |
| `SearchIndexTests`, `SearchTextTests`, `SearchQueryTests` | ricerca, punteggi, accenti, nascosti esclusi; i filtri `type:` e `in:`, scritti e tolti, e i più recenti con i soli filtri |
| `ArchiveFoldersTests` | quali cartelle possono essere archivi, anche quelli fatti con Quiver, che passano da `.quiver` a `.inlay`, e il confronto byte per byte di una copia |
| `ItemArrangementTests`, `NaturalStringComparerTests` | ordinamenti e ordine a mano |
| `TopicColorTests`, `TileColorTests` | i colori di argomenti e riquadri |
| `CanvasPlacementTests` | la disposizione delle zone, senza sovrapposizioni |
| `ArchiveNamesTests`, `FileKindTests`, `FolderSizeTests`, `WebShortcutTests`, `ByteSizeTests` | nomi, tipi di file, dimensioni, collegamenti ai siti |

### `tests/Inlay.App.Tests`: l'app

Vede le parti interne dell'app (`InternalsVisibleTo`). Gli archivi stanno in `%TEMP%\inlay-app-tests\<guid>`, le destinazioni recenti in un file accanto, mai in quello del PC.

| File | Cosa controlla |
|---|---|
| `MainViewModelTests` (66 test) | rinomina, selezione dopo le ricariche, nuovi riquadri e cartelle, cartelle che diventano righe di «General» e tornano riquadri, `Ctrl+Z` (per spostamenti, rinomine, creazioni, ordini, misure, ordinamenti, promozioni e colori: non per copie, collegamenti ed eliminazioni, che userebbero il Cestino vero), argomenti trascinati, «Move to…», i filtri della ricerca, avvisi, barra di stato e tasti da sapere, spazio su disco, colori, la tela |
| `BackgroundOperationsTests` | le operazioni sui thread a parte, con un dispatcher come quello della finestra: la finestra resta libera; con le operazioni trattenute (`ShellRunner`), quelle su file diversi vanno insieme, le altre aspettano, `Ctrl+Z` aspetta tutte, e due copie che insieme non ci starebbero non partono insieme |
| `ShellThreadTests` | ogni operazione su un thread STA suo, insieme alle altre |
| `UpdaterTests` | un Inlay non installato non cerca aggiornamenti; l'aggiornamento vero si prova solo con un Inlay installato e una release nuova |
| `ShellFileOperationsTests` | spostare, copiare e rinominare con la shell vera, senza finestre (`interactive: false`); eliminare no, perché riempirebbe il Cestino |
| `ShellLinksTests`, `VirtualFilesTests`, `ShellIconsTests` | collegamenti, allegati e link trascinati, icone di Windows |
| `ArchivesViewModelTests`, `SettingsViewModelTests`, `AppSettingsTests` | archivi (creare, aprire, rinominare, spostare anche su un altro disco, con la copia su un thread a parte), impostazioni, salvataggio, file delle impostazioni rovinato messo da parte, impostazioni di Quiver prese al primo avvio |
| `UpdatesViewModelTests` | la pagina **About**: la versione, la frase sugli aggiornamenti a ogni passo, i pulsanti, un Inlay lanciato dal codice |
| `DestinationPickerViewModelTests`, `RecentDestinationsTests` | l'elenco di «Move to…» e le destinazioni recenti |
| `CanvasCameraTests`, `CanvasWakeTests`, `TileListTests` | la camera, e le righe dei riquadri che arrivano quando servono |
| `CoverPanelTests` | la scheda vista da lontano non fa mai crescere il riquadro che copre |
| `PanelMenusTests` | costruisce tutti i menu del tasto destro con i loro dati e ne prepara le voci, senza mostrarli: un binding che non si può attaccare fa fallire il test (è nato dall'errore della 0.3.0) |
| `SendToShortcutTests`, `SingleInstanceProtocolTests`, `CommandLineTests`, `WindowToggleTests`, `DiskSpaceTests` | «Invia a», il passaggio dei comandi tra istanze, la riga di comando, la scorciatoia, lo spazio su disco |

Gli aiuti comuni:

- **`StaThread.Run`** esegue un test su un thread STA, come vuole la shell; **`StaThread.RunAsync`** ci mette anche un dispatcher, così le continuazioni tornano su quel thread come nell'app.
- **`TestLog`** manda il log dei test in `%TEMP%\inlay-app-tests\inlay.log`, una volta sola quando si carica l'assembly dei test, prima di qualunque test (un *module initializer*): il log dell'utente parla solo di Inlay.
- **`AppResources.Load`** carica i dizionari di Brace e di Inlay in una `Application` WPF qualunque, mai in quella di Inlay: il suo avvio troverebbe l'Inlay dell'utente in esecuzione e gli manderebbe un comando. I test che costruiscono le viste stanno nella collezione `AppViews` («Viste dell'app») e girano uno alla volta, perché le risorse sono di una sola applicazione.
- **Le dipendenze finte**: `MainViewModel` con `live: false` (le operazioni avvengono subito, sul thread del test), `DiskOf` per fingere lo spazio libero; `ISettingsHost` e `IWindowsIntegration` finti per le impostazioni; `sameDrive` e `diskOf` per spostare un archivio «su un altro disco» senza averne uno.

### Scrivere un test nuovo

- Una regola del Core va in `Inlay.Tests`, con `TempArchive` se serve il disco.
- Un comportamento del pannello va in `MainViewModelTests`: si crea l'archivio nel costruttore, si apre con `Open()` dentro `StaThread.Run`, si agisce sul view model come farebbe la vista e si controllano file, metadati e view model.
- Un test che costruisce viste WPF chiama `AppResources.Load()` e va nella collezione `AppViews`.
- Niente Cestino vero, niente impostazioni, registro o cartelle dell'utente: tutto passa da cartelle temporanee e da dipendenze finte.

## Verificare l'interfaccia senza aprirla

`Inlay.exe --archive <cartella> --render-preview <file.png>` disegna il pannello in un'immagine, senza finestre, ed esce; senza `--archive` non disegna niente. Si può guardare l'immagine per controllare un cambiamento dell'interfaccia. Da sorgente:

```powershell
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo --render-preview pannello.png --topic Lavoro/Clienti
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo --render-preview tela.png --canvas --zoom 0.5
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo --render-preview impostazioni.png --settings --section hotkey
```

- L'anteprima usa `MainViewModel` con `live: false` e `readOnly: true`: legge l'archivio e non scrive niente, nemmeno i colori nuovi degli argomenti. Le destinazioni recenti stanno in un file temporaneo suo (`inlay-preview-recenti.json`), le impostazioni sono inventate (l'archivio dato più uno che non c'è), e quello che Windows direbbe (avvio, «Invia a») è finto.
- Il pannello è grande 1240 × 780 punti, la finestra Impostazioni 960 × 650. Le icone di Windows si leggono subito (`ShellIcons.Synchronous`), e sulla tela le righe dei riquadri in vista si creano tutte (`WakeInViewNow`).
- Le opzioni sono elencate in [windows.md](windows.md#la-riga-di-comando-commandlineoptions). Se qualcosa va storto, l'errore finisce in `<file.png>.error.txt` e il codice di uscita è 1.

### L'archivio di prova

```powershell
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo
```

Lo script si rifiuta di usare una cartella che esiste già. Crea gli argomenti Lavoro (con i sotto-argomenti Clienti, Rossi, Bianchi e Interni, segnati nei metadati), Personale e Strumenti, con file, cartelle, collegamenti a programmi di Windows e a una cartella, un `.url`, e un file sciolto nella radice, che fa comparire il suo avviso nella barra di stato. `--archive` vale solo per quell'avvio.

### Misurare la tela

`Inlay.exe --archive <cartella> --benchmark-canvas <file.txt>` (`Services/CanvasBenchmark.cs`) apre la tela dell'archivio in una finestra grande come l'area di lavoro, senza prendere il focus (è una finestra di strumenti, che GlazeWM lascia stare), e la muove per otto secondi sempre sullo stesso percorso: avvicinamento, spostamento da vicino, allontanamento, zoom avanti e indietro, due secondi ciascuno. Nel file scrive, in inglese come l'interfaccia:

- la misura della tela in pixel, la scala e il livello di accelerazione grafica di WPF;
- quante zone, riquadri ed elementi ha l'archivio, e quanto ci ha messo la tela ad aprirsi;
- per ogni parte i fotogrammi al secondo, la mediana, il 95° percentile, il peggiore e quanti superano 33 ms;
- per ogni fotogramma oltre 100 ms cosa è successo prima: cambio di livello, liste svegliate, raccolte della memoria;
- la memoria all'inizio e alla fine.

L'archivio viene solo letto, e funziona anche con Inlay già aperto.

## Prima di pubblicare

Dal piano della tela ([PROGETTO.md §11](../PROGETTO.md#fasi)): ogni fase si chiude con i test e le anteprime e, prima di pubblicare, con la prova che l'archivio vero si legge come prima. Nel repository non c'è uno strumento per quest'ultima: le note di versione dicono che è stata fatta confrontando, in sola lettura, quello che leggono la versione vecchia e quella nuova.

Per la 0.3.3 è stata fatta così: due worktree di git, uno sul tag della versione prima e uno sul commit nuovo, e in ognuno un test usa e getta, mai nel commit, che apre l'archivio vero con `new MainViewModel(root, live: false, readOnly: true)`, sceglie ogni argomento con `SelectTopicByPath` e scrive in un file argomenti, riquadri (titolo, colore, ordinamento) ed elementi, nell'ordine in cui si vedono. I due file devono essere uguali, e `archive.json` dell'archivio lo stesso di prima, byte per byte. Poi `--render-preview` della bacheca e della tela sull'archivio vero, per vederlo.

Molte novità sono verificate con i test e le anteprime ma non ancora provate dal vivo: l'elenco di cosa provare è in [PROGETTO.md §10](../PROGETTO.md#10-prossimi-passi).

## Il log

`%LOCALAPPDATA%\Inlay\inlay.log` (`Services/Log.cs`): una riga per evento, con data e ora; oltre 1 MB ricomincia da capo. Ci finiscono:

- l'avvio, con l'archivio e la scorciatoia, e i cambi di archivio e di scorciatoia;
- quanto ci ha messo il pannello ad aprirsi (`Panel shown in … ms`) e perché si è nascosto (`Window hidden: …`);
- quale finestra teneva il focus quando Inlay non è riuscito a prenderlo;
- le attenzioni e gli errori detti negli avvisi (`Told: …`) e gli errori imprevisti, con lo stack;
- i problemi con impostazioni, metadati, indice, watcher, collegamenti e comandi delle altre istanze.
