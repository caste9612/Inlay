# L'interfaccia

Come è fatto il pannello: view model, ricarica, ricerca, selezione, tastiera e mouse, domande, avvisi, menu, impostazioni e aspetto. Com'è fatta la tela è in [tela.md](tela.md), i trascinamenti in [operazioni-sui-file.md](operazioni-sui-file.md#trascinare), la finestra in [windows.md](windows.md#la-finestra-panelwindow).

## Come è diviso il codice

- **MVVM con CommunityToolkit.Mvvm**: proprietà con `[ObservableProperty]` e `[NotifyPropertyChangedFor]`, comandi con `[RelayCommand]`, reazioni nei metodi parziali `On…Changed`.
- **I view model** stanno in `ViewModels`. `MainViewModel` è lo stato del pannello; attorno a lui `TopicViewModel`, `TileViewModel`, `ItemViewModel` (e `SearchResultViewModel`), `ZoneViewModel` e `CanvasViewModel`, più `PromptViewModel`, `DestinationPickerViewModel` e `ToastViewModel` per domande, «Sposta in…» e avvisi.
- **I gesti stanno nel code-behind** di `PanelView` (`Views/PanelView.xaml.cs`): tastiera, mouse, trascinamenti e menu decidono cosa fare e chiamano i metodi di `MainViewModel`. I comandi sono pochi (`SelectTopicCommand` e quelli delle domande).
- **Il view model parla alla vista con eventi**: `FileOperationCompleted` (la finestra si riprende il focus), `SelectionRestored` (il focus va sulla selezione), `ShowOnCanvasRequested` (volo a un riquadro), `Reloaded` (il focus torna dov'era).
- **I testi dell'interfaccia** sono in italiano, scritti direttamente nel XAML e nel codice; i commenti e i nomi sono in inglese.

## `MainViewModel`: lo stato del pannello

| Cosa | Proprietà |
|---|---|
| argomenti, sotto-argomenti della bacheca, riquadri della bacheca | `Topics`, `SubTopics`, `Tiles` |
| ricerca | `Query`, `IsSearching`, `Results`, `ResultsHeader` |
| vista | `IsCanvasShown`, `ViewIndex`, `IsBoardVisible`, `IsCanvasVisible`, `Canvas`, `IsSidebarOpen` |
| bacheca in vista | `SelectedTopic`, `BoardTitle`, `BoardPath`, `EmptyMessage`, `BoardFolder` |
| domande | `Prompt`, `Picker` |
| avvisi e barra di stato | `Toasts`, `Status`, `Activity`, `IsBusy`, `ArchiveSummary`, `SelectionSummary`, `DiskText`, `IsDiskLow`, `RootFilesText` |
| annullare | `CanUndo`, `UndoLabel` |

Il costruttore riceve l'archivio e alcune dipendenze sostituibili: `live` (in tempo reale, con indice in background, watcher e coda delle operazioni; falso nelle anteprime e nei test), il Cestino, le destinazioni recenti e `readOnly` (le anteprime non scrivono niente nell'archivio). `DiskOf` permette ai test di fingere lo spazio libero.

Il ciclo di vita:

- `LoadArchive(root)` riparte da zero: nuovo lettore, nessun indice, nessuna selezione, registro e avvisi svuotati; poi ricarica e, se `live`, indice e watcher.
- `ReleaseArchive()` smette di guardare e indicizzare la cartella, perché si possa rinominare o spostare.
- `OnPanelShowing()` svuota la ricerca, le domande e gli avvisi e ricarica; `OnPanelHidden()` conferma i nomi in modifica; `OnWindowActivated()` ricarica se non c'è niente in corso.

## Ricaricare (`Reload`)

A ogni ricarica `MainViewModel` legge i metadati, crea un nuovo `ArchiveReader`, legge gli argomenti (dando un colore a quelli nuovi della radice), riallinea l'albero, conta i file sciolti nella radice, rilegge la bacheca in vista (o l'argomento più vicino che c'è ancora, o il primo), la tela se è in vista, rimette la selezione chiesta, aggiorna la barra di stato e avvisa la vista.

- **Quando**: all'apertura del pannello e tornando alla finestra (il watcher vede solo i nomi, non le date), dopo ogni operazione, dopo ogni cambiamento dei metadati, tornando dalla tela alla bacheca (verso la tela si rilegge solo la tela), e quando il watcher segnala un cambiamento (dopo 400 ms di calma). La lettura avviene sul thread dell'interfaccia: una bacheca si legge in un attimo, la tela intera richiede tutte le bacheche.
- **Quando no**: se il pannello è nascosto, se c'è una domanda aperta, se un'operazione è in corso o se un nome è in modifica, la ricarica viene rimandata (`_reloadPending`) e fatta appena possibile, perché butterebbe via quel lavoro.
- **`Reconciler.Reconcile`** riallinea una collezione con la lista appena letta, tenendo i view model la cui chiave c'è ancora: selezione, nome in modifica, rami aperti, controllo e focus sopravvivono, e la vista riceve solo spostamenti, inserimenti e rimozioni. Le chiavi (senza distinzione di maiuscole):

| View model | Chiave |
|---|---|
| `TopicViewModel` | la chiave dei metadati dell'argomento |
| `TileViewModel` | `general:` o `folder:` più la cartella |
| `ItemViewModel` | `file:` o `folder:` più il percorso |
| `ZoneViewModel` | la chiave dell'argomento |

- **La selezione dopo una ricarica**: un'operazione chiama `SelectAfterReload(paths)`; la ricarica seleziona quegli elementi e solleva `SelectionRestored`, e la vista mette il focus sul primo. Se l'elemento con il focus è sparito, `PanelView` lo rimette dov'era (nell'albero, sul riquadro, sulla bacheca o nella ricerca).

## La ricerca

- L'indice (`SearchIndex`) si costruisce in background quando si apre l'archivio e quando il watcher vede un cambiamento; una costruzione nuova annulla quella in corso. Mentre un'operazione sposta file, aspetta e si fa una volta sola alla fine. Finché non c'è, la ricerca dice «Indicizzazione in corso…».
- Scrivere in qualunque punto del pannello scrive nella ricerca (`OnPreviewTextInput`). `Query` produce al massimo 60 risultati, ognuno con il suo percorso («Lavoro › Clienti › Rossi», o «Archivio»).
- Le frecce scelgono, `Invio` apre, `Ctrl+Invio` mostra in Explorer, `Esc` svuota la ricerca e una seconda volta chiude la finestra. Sulla tela un clic su un risultato ci vola sopra ([tela.md](tela.md#i-gesti-in-panelview)).
- Con la ricerca vuota, `Ctrl+Z` e `Ctrl+V` (di file) agiscono sulla bacheca: il pannello si apre con il focus nella ricerca, e devono funzionare subito.

## Selezione, tastiera e mouse

- **Una sola selezione per tutto il pannello**, come sul desktop: scegliere in un riquadro deseleziona gli altri, con `Ctrl` si aggiunge. `SelectedItems()` sono quelli selezionati della bacheca, della tela o dei risultati.
- **Clic su un elemento già selezionato**: la selezione resta, per poterla trascinare, e il clic conta al rilascio. Un secondo clic lento sull'unico elemento selezionato, dopo il tempo del doppio clic di Windows, lo rinomina.
- **Tasto destro**: su un elemento non selezionato seleziona solo quello; il menu agisce sulla selezione.
- **Il rettangolo** (solo sulla bacheca): trascinando sul vuoto, non dal titolo di un riquadro né da un pulsante, `SelectionBandAdorner` disegna il rettangolo e seleziona le righe che tocca, solo nella parte visibile di ogni riquadro; con `Ctrl` tiene quelle di prima.
- **La rotella** su un riquadro ne fa scorrere la lista; agli estremi fa scorrere la bacheca.

I tasti, gestiti quasi tutti in `OnPreviewKeyDown` (`Invio` nelle liste in `ItemList_KeyDown`, nella ricerca in `SearchBox_PreviewKeyDown`):

| Tasto | Cosa fa |
|---|---|
| `Ctrl+Maiusc+N` | nuovo riquadro |
| `Ctrl+B` | chiude o apre la barra degli argomenti |
| `Ctrl+0`, `Ctrl++`, `Ctrl+-` | sulla tela: tutto in vista, avvicina, allontana |
| `Esc` | svuota la ricerca; senza ricerca, rimette Quiver nella tray |
| `Ctrl+Z` | annulla |
| `F2` | rinomina l'argomento con il focus o l'unico elemento selezionato |
| `Canc` | Cestino per l'argomento con il focus o la selezione |
| `Ctrl+A` | seleziona tutto il riquadro con il focus |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | appunti; si incolla nell'argomento o nel riquadro con il focus, se no sulla bacheca |
| `Ctrl+Maiusc+C` | copia il percorso |
| `Alt+Invio` | proprietà |
| `Invio`, `Ctrl+Invio` (in una lista) | apre, mostra in Explorer |

Mentre una domanda è aperta, i tasti vanno a lei: `Esc` annulla, `Invio` conferma (o preme il pulsante su cui si è arrivati con `Tab`), le frecce scorrono l'elenco di «Sposta in…».

**La rinomina sul posto**: `Invio` conferma (se il nome non va, un avviso lo dice e il campo resta aperto), `Esc` annulla, perdere il focus conferma (tranne verso il menu del campo stesso), e un clic altrove conferma dopo che il clic ha fatto il suo lavoro (`CommitAfterInput`).

## Le domande sopra la bacheca

- **`PromptViewModel`**: titolo, etichetta del pulsante, messaggio, un campo di testo facoltativo, pulsante rosso per le azioni che tolgono qualcosa (`IsDanger`). La funzione di conferma restituisce un problema da mostrare sotto il campo, o null per chiudere. Sono dentro il pannello e non finestre a parte, perché fino alla 0.2.1 il pannello si nascondeva appena perdeva il focus.
- **`DestinationPickerViewModel`** («Sposta in…»): senza filtro, prima le destinazioni recenti e poi le altre nell'ordine della barra laterale; con un filtro, il punteggio di `SearchText`, con 30 punti in più per le recenti. `Detail` dice dove sta ogni posto.
- Entrano con `Motion.Enter`, il focus va al campo o al pulsante di conferma, e alla chiusura torna dov'era.

## Avvisi e barra di stato

- **`ToastViewModel`**: tipo (`Success`, `Info`, `Warning`, `Danger`), titolo, testo, e facoltativi **Mostra** (`Notice`) e **Annulla** (`UndoStep`, finché è l'ultimo passo del registro).
- **`Tell`**: un avviso uguale a uno già in vista lo sostituisce; al massimo tre, il più vecchio esce. Attenzioni ed errori vanno anche nel log. Gli avvisi si svuotano a ogni apertura del pannello e cambiando archivio.
- **`ToastClock`**: successi e notizie si chiudono dopo sei secondi, con il conto fermo mentre il puntatore o il focus sono sopra. Attenzioni ed errori restano finché non si chiudono.
- **La barra di stato**: a sinistra l'archivio (un clic lo apre in Explorer) con `ArchiveSummary` e, quando ce ne sono, i file sciolti nella radice (`RootFilesText`: un clic apre la radice), oppure, durante un'operazione, i LED di `Brace.Activity` con `Activity`; a destra `SelectionSummary` (quanti elementi sono selezionati e quanto pesano i loro file: come in Explorer, il peso delle cartelle non si conta), lo spazio libero (con l'icona di avviso quando è poco) e **?**, i tasti da sapere.
- **I tasti da sapere** sono `MainViewModel.Shortcuts`, costruiti da `ShortcutGroup.For(Hotkey)`: tra loro c'è la scorciatoia in uso, che `App` passa al view model all'avvio e ogni volta che cambia nelle impostazioni.

## I menu

Sono risorse di `PanelView.xaml` con `x:Shared="False"`. `ItemMenu`, `TileMenu` e `TopicMenu` sono assegnati con `DynamicResource`: ogni elemento ha il suo menu, creato quando serve. `BoardMenu` è uno solo, assegnato alla bacheca con `StaticResource`. Nei menu che hanno `Opened="ContextMenu_Opened"` (riquadro, argomento, bacheca), le voci «Incolla» (`Tag="paste"`) sono attive solo se negli appunti ci sono file. Il pulsante … di un riquadro apre il suo stesso menu.

| Menu | Voci |
|---|---|
| `ItemMenu`, elemento | Apri, Apri con…, Mostra in Explorer, Sposta in…, Taglia, Copia, Copia percorso, Rinomina, Promuovi a riquadro (solo per le cartelle di «Generale», `CanBecomeTile`), Proprietà, Sposta nel Cestino |
| `TileMenu`, riquadro | Incolla, Incolla collegamento, Nuova cartella, Nuovo collegamento…, Apri in Explorer, Rinomina riquadro, Promuovi ad argomento, Metti in Generale, Colore, Ordina per, Misura automatica, Sposta il riquadro nel Cestino |
| `TopicMenu`, argomento | Apri in Explorer, Incolla qui, Incolla collegamento qui, Nuovo riquadro, Nuovo argomento qui dentro, Rinomina, Riporta a riquadro, Colore (con Automatico e Nessuno), Sposta nel Cestino |
| `BoardMenu`, bacheca | Annulla (con l'etichetta di `UndoLabel`), Nuovo riquadro, Nuovo collegamento…, Incolla, Incolla collegamento, Apri in Explorer |

Il menu completo di Windows (7-Zip e simili) non c'è ancora: è quello che resta della v0.2.

## I riquadri sulla bacheca

- **`TileTemplate`**: un `GroupBox` con lo stile `Brace.Panel`. Nel titolo la maniglia a puntini (visibile al passaggio del mouse, solo per i riquadri che sono una cartella), la striscia del colore, il nome (o il campo per rinominarlo), il numero di elementi e il pulsante …; sotto, la lista o il messaggio di riquadro vuoto; nell'angolo, la maniglia per la misura.
- **`TileList`**, la lista di un riquadro: con `Rows` è alta esattamente quelle righe, misurate sullo schermo (`RowPitch`: al 125 % Windows arrotonda una riga da 22 a 22,4 punti); senza, segue i suoi elementi fino a 268 punti (`TileViewModel.UsualListHeight`), poco meno di dodici righe. Prende gli elementi da `Source` invece che da `ItemsSource`, perché sulla tela nasce addormentata ([tela.md](tela.md#le-righe-create-quando-servono)).
- **L'angolo** (`ResizeGrip_*`): in larghezza scatta alle colonne della bacheca (al massimo 3, e non più di quelle che ci sono), in altezza a righe, ma solo se il trascinamento si è mosso di almeno mezza riga o il riquadro aveva già un'altezza. Alla fine `ResizeTile`; se il trascinamento viene interrotto, il riquadro torna com'era. Il doppio clic torna alla misura automatica.
- **`TileGridPanel`** dispone i riquadri in colonne di almeno 280 punti, con 16 punti tra l'una e l'altra.
- Sopra i riquadri, i sotto-argomenti come pulsanti; sopra ancora, il percorso, il titolo, l'interruttore «Bacheca | Tela» e **Nuovo riquadro**.

### Le icone degli elementi

`ItemViewModel` sceglie tra due icone, sovrapposte nella riga:

- **`Glyph`**, l'icona di Brace: per le cartelle e i tipi comuni (`FileGlyphs.For(kind)`), e il documento di Brace per i file che nessun programma del PC apre (`FileAssociations`).
- **`Icon`**, l'icona di Windows: per programmi, collegamenti e gli altri file. Si chiede la prima volta che la riga si vede, su un thread a parte (`ShellIcons`); intanto la riga mostra il documento di Brace.

## La barra laterale

- Larga 264 punti; chiusa (`Ctrl+B` o il pulsante nella barra del titolo) non occupa spazio, e Quiver ricorda com'era.
- **Gli argomenti** sono un `TreeView` con lo stile `TopicItem`: righe da 32 punti e nomi da 14 (`NavRowHeight`, `NavTextSize`), quelli della radice un po' più marcati, l'icona del colore dell'argomento. Quello scelto ha la cartella piena e lo sfondo `accent-subtle`, che diventa `Brace.SelectionInactive` quando il focus non è nell'albero.
- In alto il **+** per un argomento nuovo.

## La barra del titolo

Disegnata da Quiver come `br-titlebar` di Brace, perché Brace per WPF non ne ha ancora una: logo e nome, il pulsante della barra laterale, la ricerca al centro (al massimo 280 punti), il nome dell'archivio (apre Impostazioni › Archivi), il pin (legato a `Window.Topmost`), riduci, ingrandisci o ripristina, chiudi (che nasconde). Gli elementi che devono ricevere i clic dentro la zona della cornice hanno `WindowChrome.IsHitTestVisibleInChrome`.

## Colori

- **Gli otto colori** (Ambra, Azzurro, Viola, Rosa, Verde, Giallo, Rosso, Grigio) sono scritti nei due menu **Colore** di `PanelView.xaml`, quello del riquadro e quello dell'argomento. `TileColors.All` li elenca con il loro nome, ma oggi nessuno lo usa: cambiarlo non cambia i menu.
- **`TileColors.BrushFor`** dà un pennello congelato per un colore «#RRGGBB»; un valore scritto a mano che non è un colore vale come nessuno.
- **`TopicViewModel.Color`**: `none` vuol dire nessun colore; un argomento della radice mostra il suo; uno interno il suo se scelto a mano, altrimenti quello del genitore. `ColorChoice` dice quale voce del menu Colore ha la spunta (`auto`, `none` o il colore).
- Le zone della tela prendono il colore del loro argomento.

## Movimento

- **`Motion.Enter`**: dissolvenza più un piccolo scorrimento o ingrandimento, con la durata e la curva di Brace (`Brace.DurationNormal`, `Brace.EaseEnter.Function`). Niente si muove se Windows ha le animazioni spente. Si usa per il pannello che compare, le domande e gli avvisi.
- **Gli adorner**: `DragLabelAdorner` (l'etichetta accanto al puntatore), `InsertionAdorner` (la riga dove andrà un riquadro, una riga o un argomento), `SelectionBandAdorner` (il rettangolo).

## Le impostazioni (`SettingsViewModel`, `ArchivesViewModel`)

`SettingsView` ha le pagine a sinistra (`SettingsSection`: Archivi, Scorciatoia, Avvio ed Explorer) e la pagina scelta a destra. Ogni cambiamento vale subito. La prima volta (`IsWelcome`) c'è solo la pagina degli archivi.

- **Scorciatoia**: `HotkeyBox` registra la combinazione premuta (con `Ctrl`, `Alt` o `Win`; `Maiusc` da solo no; `Esc` lascia quella di prima). `UseHotkey` la prova subito tramite `ISettingsHost.TryUseHotkey` e la salva solo se Windows la dà a Quiver; se no, sotto il campo compare il motivo.
- **Avvio ed Explorer**: avvio con Windows e voce di «Invia a», tramite `IWindowsIntegration`. Se Windows rifiuta, l'interruttore torna com'era e un messaggio lo dice.
- **Archivi** (`ArchivesViewModel`):
  - l'elenco viene dalle impostazioni, il più recente per primo, con «In uso» o «Non trovato»; sotto ogni archivio, contato in background, quanto occupa e quanto spazio ha il suo disco;
  - **Crea** (nome e cartella, che di partenza è quella dell'utente, sullo stesso disco del Desktop), **Apri una cartella** (`ArchiveFolders.Adopt`), **Apri**, **Mostra in Explorer**, **Togli dall'elenco** (non quello in uso; la cartella resta);
  - **Rinomina** e **Sposta…** sullo stesso disco sono un `Directory.Move` della cartella, dopo aver lasciato libero l'archivio in uso (`IArchiveHost.Release`), che poi si riapre dal posto nuovo;
  - **Sposta…** su un altro disco (`MoveToOtherDriveAsync`): conta quanto occupa l'archivio e controlla lo spazio, lo copia con la shell, confronta byte per byte la copia con l'originale (`CopyCheck`) e passa alla copia solo se è identica; poi chiede se mandare la cartella vecchia nel Cestino. In ogni altro caso l'archivio resta dov'era.
  - Il conto e il confronto girano in background. La copia gira su un thread STA a parte (`CopyAsync`, con una `FileOperationQueue` sua), con la finestra di avanzamento di Windows: Quiver resta usabile. Mentre la shell copia (`IsCopying`) la finestra Impostazioni non si chiude (`TryClose`) ed **Esci** aspetta (`WhenCopied`), perché una copia interrotta lascerebbe mezzo archivio sull'altro disco. Durante il confronto (`IsChecking`) compaiono l'avanzamento e «Interrompi il controllo».
  - Restano sul thread della finestra la rinomina e lo spostamento sullo stesso disco (un `Directory.Move`) e il Cestino della cartella vecchia, che di solito sono rapidi.
  - Le domande della pagina sono anche qui dei `PromptViewModel`.

## Brace e il tema di Quiver

- `App.xaml` unisce `Brace/Brace.xaml` (che porta con sé `Brace.Controls.xaml`, e da lì i token e le icone) e poi `Themes/Quiver.xaml`.
- **I file XAML e C# di `Brace/` sono generati** dal design system (lo dice la loro prima riga) e non si modificano qui: le correzioni vanno fatte in Brace, in `strumenti/modelli/wpf/`, e poi ricopiate. Li accompagnano la licenza delle icone Fluent e `PROVENIENZA.md`, che dice da quale commit vengono.
- `Brace/Brace.Wpf.cs` (namespace `Brace`) ha gli aiuti in C# del design system: `Chrome.Dark` (i colori di Brace sulla cornice di Windows, messo dallo stile `Brace.Window`), `TreeIndentConverter`, `SwitchThumbMarginConverter`, `BezierEase` e altri.
- **`Themes/Quiver.xaml`** ha quello che Quiver aggiunge a Brace: le icone che Brace non ha ancora (`Quiver.Icon.Restore16`, `Quiver.Icon.Grip16`, `Quiver.Icon.ResizeCorner16`), gli stili della barra del titolo e dei suoi pulsanti, il pin, il badge dell'archivio, la maniglia della misura dei riquadri e il logo (`Quiver.Logo`, lo stesso disegno di `Assets/quiver.ico`). L'icona per ripristinare e la barra del titolo andrebbero portate in Brace.
- I caratteri Inter e Cascadia Mono sono dentro l'eseguibile (`Fonts/*.ttf`), con le licenze copiate accanto.
- L'icona dell'app si rigenera con `python scripts/genera-icona.py src/Quiver.App/Assets/quiver.ico`.
- Il testo del pannello è in modalità `Display`, nitido a scala 1; sulla tela è `Ideal`.
