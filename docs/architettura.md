# Architettura

Inlay è un'app WPF per Windows 11 (.NET 10, x64) che vive nell'area di notifica e mostra, con una scorciatoia, una finestra che fa da scrivania sopra un archivio di cartelle vere. L'architettura vista da chi decide è in [PROGETTO.md §8](../PROGETTO.md#8-architettura-tecnica); qui c'è la mappa per chi lavora sul codice.

## La soluzione

`Inlay.slnx` (il formato XML delle soluzioni) contiene quattro progetti:

```
src/Inlay.Core          net10.0          la logica dell'archivio, senza interfaccia
src/Inlay.App           net10.0-windows  l'app WPF: finestre, view model, servizi, chiamate a Windows → Inlay.exe
tests/Inlay.Tests       net10.0          i test del Core
tests/Inlay.App.Tests   net10.0-windows  i test dell'app, con WPF e la shell vera
```

Dipendenze: l'app usa il Core; i test del Core usano il Core; i test dell'app usano l'app (che ne mostra le parti interne con `InternalsVisibleTo`).

| Pacchetto | Versione | Dove | Per cosa |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | app | proprietà osservabili e comandi |
| H.NotifyIcon.Wpf | 2.4.1 | app | l'icona nella tray |
| Velopack | 1.2.161 | app | installazione e disinstallazione |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector | 2.9.3, 3.1.4, 17.14.1, 6.0.4 | test | i test |
| vpk (strumento .NET) | 1.2.161 | `dotnet-tools.json` | costruire l'installer |

Il progetto dell'app:

- `AssemblyName` è `Inlay`: il processo si chiama `Inlay.exe`, ed è su questo nome che le regole di GlazeWM riconoscono la finestra.
- `StartupObject` è `Inlay.App.Program`, perché Velopack deve partire prima di WPF ([windows.md](windows.md#lavvio-program-app)).
- `AllowUnsafeBlocks` serve al generatore di `[LibraryImport]`.
- `app.manifest` dichiara la consapevolezza dei DPI per monitor (PerMonitorV2), per testo e icone nitidi su monitor con scale diverse.
- L'icona e i caratteri sono risorse dell'eseguibile.

## A strati

```
 PanelWindow / SettingsWindow          la finestra: mostrare, nascondere, focus, posto, cornice
        │
 PanelView / SettingsView (XAML + code-behind)   tastiera, mouse, trascinamenti, menu, tela
        │  binding ed eventi
 MainViewModel  ─ Topic/Tile/Item/ZoneViewModel, CanvasViewModel, Prompt/Picker/Toast
        │                 │
        │                 └─ Services, Interop: shell, icone, appunti, collegamenti, Cestino, dischi
        │
 Inlay.Core: ArchiveReader, ArchiveMetadata, TransferRules, UndoHistory, RecycleBin,
              SearchIndex, ArchiveWatcher, CanvasPlacement, ...
        │
 il file system: le cartelle dell'archivio e .inlay\archive.json
```

## Dove sta ogni cosa

| Cartella | Cosa contiene | Documento |
|---|---|---|
| `src/Inlay.Core` | il modello e le regole dell'archivio | [archivio.md](archivio.md) |
| `src/Inlay.App` (radice) | `Program`, `App`, `PanelWindow`, `SettingsWindow`, `app.manifest` | [windows.md](windows.md) |
| `src/Inlay.App/Services` | impostazioni, scorciatoia, tray, istanza unica, coda delle operazioni, icone, appunti, log, anteprime, misura della tela | [windows.md](windows.md) |
| `src/Inlay.App/Interop` | le chiamate a Windows: operazioni della shell, collegamenti, file trascinati da altri programmi, finestre | [windows.md](windows.md#la-shell-di-windows-interop) |
| `src/Inlay.App/ViewModels` | lo stato del pannello, della tela e delle impostazioni | [interfaccia.md](interfaccia.md), [tela.md](tela.md) |
| `src/Inlay.App/Views` | `PanelView`, `SettingsView`, `ArchivesView`, i pannelli di disposizione, gli adorner, i convertitori | [interfaccia.md](interfaccia.md), [tela.md](tela.md) |
| `src/Inlay.App/Themes` | `Inlay.xaml`: quello che Inlay aggiunge a Brace | [interfaccia.md](interfaccia.md#brace-e-il-tema-di-inlay) |
| `src/Inlay.App/Brace`, `Fonts` | il design system, copiato e da non modificare | [interfaccia.md](interfaccia.md#brace-e-il-tema-di-inlay) |
| `src/Inlay.App/Assets` | `inlay.ico` | |
| `tests` | i due progetti di test | [test.md](test.md) |
| `scripts` | installer, archivio di prova, icona | [rilascio.md](rilascio.md), [test.md](test.md) |
| `novita` | le note di versione | [rilascio.md](rilascio.md) |

I file più grandi, dove sta il grosso del comportamento: `ViewModels/MainViewModel.cs` (circa 2.100 righe), `Views/PanelView.xaml.cs` (circa 2.000) e `Views/PanelView.xaml` (circa 1.200).

## Come scorre il lavoro

**Leggere.** `MainViewModel.Reload()` legge `archive.json` (`MetadataStore`), crea un `ArchiveReader` e ne ricava argomenti, bacheca, file sciolti nella radice ed eventualmente la tela. `Reconciler` porta le liste osservabili in linea con quello che ha letto, tenendo i view model che ci sono ancora; la vista, legata con i binding, riceve solo le differenze ([interfaccia.md](interfaccia.md#ricaricare-reload)).

**Agire.** Un gesto arriva al code-behind di `PanelView`, che decide cosa significa (per i trascinamenti con `PlanDrop`) e chiama un metodo di `MainViewModel`. Quello usa le regole del Core (`TransferRules`, `ArchiveNames`…), aspetta il suo turno, fa lavorare la shell sul thread delle operazioni, aggiorna i metadati, registra il passo per annullare, mostra un avviso e ricarica ([operazioni-sui-file.md](operazioni-sui-file.md)).

**Accorgersi dei cambiamenti.** `ArchiveWatcher` segnala, da un thread del pool, le modifiche fatte da fuori; `MainViewModel` torna sul thread dell'interfaccia e ricarica, o rimanda se c'è qualcosa in corso. L'indice della ricerca si ricostruisce in background.

## I thread

| Thread | Cosa ci gira |
|---|---|
| interfaccia (il dispatcher di WPF) | tutto lo stato dei view model, le viste, la lettura delle cartelle a ogni ricarica, le rinomine, lo spostamento di un archivio sullo stesso disco, la scorciatoia (una finestra solo per i messaggi) |
| «Inlay file operation», STA | uno per ogni spostamento, copia, eliminazione o annullamento della shell (`ShellThread`), anche per la copia di un archivio su un altro disco; quelli su file diversi girano insieme |
| «Inlay icons», STA | le icone di Windows (`ShellIcons`) |
| pool | l'indice della ricerca, i conteggi delle dimensioni, il confronto delle copie, gli eventi del watcher, l'ascolto della named pipe |

La regola: i view model si toccano solo dal thread dell'interfaccia; quello che arriva da altri thread ci torna con il dispatcher.

## Dove sta lo stato

| Dove | Cosa |
|---|---|
| l'archivio | le cartelle e i file, e `.inlay\archive.json` con quello che le cartelle non sanno dire; viaggia con l'archivio |
| il PC | `%APPDATA%\Inlay\settings.json` e `recenti.json`, il log in `%LOCALAPPDATA%\Inlay`, la chiave Run del registro, il collegamento di «Invia a» |
| la memoria | il registro per annullare, l'indice della ricerca, la cache delle icone, gli elementi tagliati, la camera della tela |

## Scelte e motivi

- **La finestra si crea una volta sola** e poi si mostra e si nasconde: così compare all'istante. Chiuderla la nasconde.
- **Le operazioni sui file passano dalla shell** (`SHFileOperation`): finestre di avanzamento e di conflitto come in Explorer, e il Cestino. Su un thread a parte dalla 0.2.3, perché la finestra si bloccava durante le copie; dopo la 0.3.2 anche la copia di un archivio su un altro disco, e ogni operazione su un thread suo, perché quelle su file diversi non si aspettino.
- **I view model si riconciliano, non si sostituiscono**: selezione, nomi in modifica, rami aperti e focus sopravvivono alle ricariche, che sono frequenti.
- **I metadati si rileggono dal disco a ogni cambiamento**: niente copie in memoria da tenere allineate con il file.
- **Niente virtualizzazione di WPF**: la selezione è legata ai view model e deve valere anche per le righe fuori vista. Sulla tela le righe si creano quando servono ([tela.md](tela.md#le-righe-create-quando-servono)).
- **Le domande sono dentro il pannello**, non finestre a parte: è nato quando il pannello si nascondeva perdendo il focus.
- **`WindowChrome`** per la barra del titolo di Brace. Ha un costo misurato in un altro progetto (Reticle); se Inlay risultasse pesante, la strada è gestire direttamente i messaggi della cornice (PROGETTO.md §8).
