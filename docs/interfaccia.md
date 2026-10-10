# L'interfaccia

Come è fatto il pannello: view model, ricarica, ricerca, selezione, tastiera e mouse, domande, avvisi, menu, impostazioni e aspetto. Com'è fatta la tela è in [tela.md](tela.md), i trascinamenti in [operazioni-sui-file.md](operazioni-sui-file.md#trascinare), la finestra in [windows.md](windows.md#la-finestra-panelwindow).

## Come è diviso il codice

- **MVVM con CommunityToolkit.Mvvm**: proprietà con `[ObservableProperty]` e `[NotifyPropertyChangedFor]`, comandi con `[RelayCommand]`, reazioni nei metodi parziali `On…Changed`.
- **I view model** stanno in `ViewModels`. `MainViewModel` è lo stato del pannello; attorno a lui `TopicViewModel`, `TileViewModel`, `ItemViewModel` (e `SearchResultViewModel`), `ZoneViewModel` e `CanvasViewModel`, più `PromptViewModel`, `DestinationPickerViewModel` e `ToastViewModel` per domande, «Move to…» e avvisi.
- **I gesti stanno nel code-behind** di `PanelView` (`Views/PanelView.xaml.cs`): tastiera, mouse, trascinamenti e menu decidono cosa fare e chiamano i metodi di `MainViewModel`. I comandi sono pochi (`SelectTopicCommand` e quelli delle domande).
- **Il view model parla alla vista con eventi**: `FileOperationCompleted` (la finestra si riprende il focus), `SelectionRestored` (il focus va sulla selezione), `ShowOnCanvasRequested` (volo a un riquadro), `Reloaded` (il focus torna dov'era).
- **I testi dell'interfaccia** sono in inglese dalla 0.3.4 (fino alla 0.3.3 erano in italiano), scritti direttamente nel XAML e nel codice, come i commenti e i nomi. Numeri e date si scrivono all'inglese qualunque sia la lingua di Windows: `ByteSize` e `DateLabel` usano la cultura `en-US`, e così i conteggi di `MainViewModel` e `ZoneViewModel` («18,481 items», «12.4 MB», «5 Oct»).

## `MainViewModel`: lo stato del pannello

| Cosa | Proprietà |
|---|---|
| argomenti, sotto-argomenti della bacheca, riquadri della bacheca | `Topics`, `SubTopics`, `Tiles` |
| ricerca | `Query`, `IsSearching`, `Results`, `ResultsHeader`, `KindFilters`, `PlaceFilterText`, `HasPlaceFilter` |
| vista | `IsCanvasShown`, `ViewIndex`, `IsBoardVisible`, `IsCanvasVisible`, `Canvas`, `IsSidebarOpen` |
| bacheca in vista | `SelectedTopic`, `BoardTitle`, `BoardPath`, `EmptyMessage`, `BoardFolder` |
| domande | `Prompt`, `Picker` |
| avvisi e barra di stato | `Toasts`, `Status`, `Activity`, `ActivityDetails`, `IsBusy`, `ArchiveSummary`, `SelectionSummary`, `DiskText`, `IsDiskLow`, `RootFilesText` |
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

- L'indice (`SearchIndex`) si costruisce in background quando si apre l'archivio e quando il watcher vede un cambiamento; una costruzione nuova annulla quella in corso. Mentre un'operazione sposta file, aspetta e si fa una volta sola alla fine. Finché non c'è, la ricerca dice «Indexing…».
- Scrivere in qualunque punto del pannello scrive nella ricerca (`OnPreviewTextInput`). `Query` produce al massimo 60 risultati, ognuno con il suo percorso («Lavoro › Clienti › Rossi», o «Vault» per i file sciolti nella radice). `RunSearch` la legge con `SearchQuery.Parse` ([archivio.md](archivio.md#la-ricerca-searchindex-searchquery-searchtext)); con i soli filtri l'intestazione dice «The 60 most recent» o quanti sono («2 items, most recent first»).
- **I filtri sopra i risultati**: il pulsante del posto («Everywhere», o «In: Lavoro», `PlaceFilterText`) apre un menu con gli argomenti, rientrati come nella barra laterale; i tipi sono `KindFilters`, un `SearchKindFilter` per tipo, nel colore dell'accento quando sono scritti nella casella (`IsChecked`). Un clic scrive o toglie `type:…` (`ToggleKindFilter`) o `in:/…` (`SetSearchPlace`, dalla radice: un argomento scelto è quello, non un'altra cartella con il suo nome) e rimette il cursore in fondo alla casella: la casella resta l'unico posto dove stanno i filtri, e quello che vi si scrive a mano accende gli stessi pulsanti.
- **Il pulsante a imbuto** nella casella apre un menu con i tipi (**Type**) e i posti (**Where**), anche prima di scrivere: un filtro scelto lì fa comparire i risultati.
- Le frecce scelgono, `Enter` apre, `Ctrl+Enter` mostra in Explorer, `Esc` svuota la ricerca e una seconda volta chiude la finestra. Sulla tela un clic su un risultato ci vola sopra ([tela.md](tela.md#i-gesti-in-panelview)).
- Con la ricerca vuota, `Ctrl+Z` e `Ctrl+V` (di file) agiscono sulla bacheca: il pannello si apre con il focus nella ricerca, e devono funzionare subito.

## Selezione, tastiera e mouse

- **Una sola selezione per tutto il pannello**, come sul desktop: scegliere in un riquadro deseleziona gli altri, con `Ctrl` si aggiunge. `SelectedItems()` sono quelli selezionati della bacheca, della tela o dei risultati.
- **Clic su un elemento già selezionato**: la selezione resta, per poterla trascinare, e il clic conta al rilascio. Un secondo clic lento sull'unico elemento selezionato, dopo il tempo del doppio clic di Windows, lo rinomina.
- **Tasto destro**: su un elemento non selezionato seleziona solo quello; il menu agisce sulla selezione.
- **Il rettangolo** (solo sulla bacheca): trascinando sul vuoto, non dal titolo di un riquadro né da un pulsante, `SelectionBandAdorner` disegna il rettangolo e seleziona le righe che tocca, solo nella parte visibile di ogni riquadro; con `Ctrl` tiene quelle di prima.
- **La rotella** su un riquadro ne fa scorrere la lista; agli estremi fa scorrere la bacheca.

I tasti, gestiti quasi tutti in `OnPreviewKeyDown` (`Enter` nelle liste in `ItemList_KeyDown`, nella ricerca in `SearchBox_PreviewKeyDown`):

| Tasto | Cosa fa |
|---|---|
| `Ctrl+Shift+N` | nuovo riquadro |
| `Ctrl+B` | chiude o apre la barra degli argomenti |
| `Ctrl+0`, `Ctrl++`, `Ctrl+-` | sulla tela: tutto in vista, avvicina, allontana |
| `Esc` | svuota la ricerca; senza ricerca, rimette Inlay nella tray |
| `Ctrl+Z` | annulla |
| `F2` | rinomina l'argomento con il focus o l'unico elemento selezionato |
| `Del` | Cestino per l'argomento con il focus o la selezione |
| `Ctrl+A` | seleziona tutto il riquadro con il focus |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | appunti; si incolla nell'argomento o nel riquadro con il focus, se no sulla bacheca |
| `Ctrl+Shift+C` | copia il percorso |
| `Alt+Enter` | proprietà |
| `Enter`, `Ctrl+Enter` (in una lista) | apre, mostra in Explorer |

Mentre una domanda è aperta, i tasti vanno a lei: `Esc` annulla, `Enter` conferma (o preme il pulsante su cui si è arrivati con `Tab`), le frecce scorrono l'elenco di «Move to…».

**La rinomina sul posto**: `Enter` conferma (se il nome non va, un avviso lo dice e il campo resta aperto), `Esc` annulla, perdere il focus conferma (tranne verso il menu del campo stesso), e un clic altrove conferma dopo che il clic ha fatto il suo lavoro (`CommitAfterInput`).

## Le domande sopra la bacheca

- **`PromptViewModel`**: titolo, etichetta del pulsante, messaggio, un campo di testo facoltativo, pulsante rosso per le azioni che tolgono qualcosa (`IsDanger`). La funzione di conferma restituisce un problema da mostrare sotto il campo, o null per chiudere. Sono dentro il pannello e non finestre a parte, perché fino alla 0.2.1 il pannello si nascondeva appena perdeva il focus.
- **`DestinationPickerViewModel`** («Move to…»): senza filtro, prima le destinazioni recenti e poi le altre nell'ordine della barra laterale; con un filtro, il punteggio di `SearchText`, con 30 punti in più per le recenti. `Detail` dice dove sta ogni posto: «Lavoro › Clienti» per un riquadro, «Space» o «Space in Lavoro» per un argomento.
- Entrano con `Motion.Enter`, il focus va al campo o al pulsante di conferma, e alla chiusura torna dov'era.

## Avvisi e barra di stato

- **`ToastViewModel`**: tipo (`Success`, `Info`, `Warning`, `Danger`), titolo, testo, e facoltativi **Show** (`Notice`) e **Undo** (`UndoStep`, finché è l'ultimo passo del registro).
- **`Tell`**: un avviso uguale a uno già in vista lo sostituisce; al massimo tre, il più vecchio esce. Attenzioni ed errori vanno anche nel log. Gli avvisi si svuotano a ogni apertura del pannello e cambiando archivio.
- **`ToastClock`**: successi e notizie si chiudono dopo sei secondi, con il conto fermo mentre il puntatore o il focus sono sopra. Attenzioni ed errori restano finché non si chiudono.
- **La barra di stato**: a sinistra un pulsante con l'icona dell'archivio e `ArchiveSummary` («18,481 items · 6.93 GB»), senza il nome, che compare una volta sola, nella barra del titolo: il tooltip (`ArchiveToolTip`) dice dov'è l'archivio, e un clic lo apre in Explorer. Poi, quando ce ne sono, i file sciolti nella radice (`RootFilesText`, «3 loose files in the vault»: un clic apre la radice), oppure, durante le operazioni, i LED di `Brace.Activity` con `Activity` (l'ultima partita, e quante altre sono in corso o in attesa; il tooltip le elenca); a destra `SelectionSummary` (quanti elementi sono selezionati e quanto pesano i loro file: come in Explorer, il peso delle cartelle non si conta), lo spazio libero (con l'icona di avviso quando è poco), «Inlay X is ready» quando una versione nuova è scaricata (`UpdateText`: un clic chiede di riavviare, `RestartToUpdateRequested`) e **?**, i tasti da sapere («Keyboard shortcuts»).
- **I tasti da sapere** sono `MainViewModel.Shortcuts`, costruiti da `ShortcutGroup.For(Hotkey)` a gruppi (Items, Clipboard, Vault, Search, Mosaic, Window): tra loro c'è la scorciatoia in uso, che `App` passa al view model all'avvio e ogni volta che cambia nelle impostazioni.

## I menu

Sono risorse di `PanelView.xaml` con `x:Shared="False"`. `ItemMenu`, `TileMenu` e `TopicMenu` sono assegnati con `DynamicResource`: ogni elemento ha il suo menu, creato quando serve. `BoardMenu` è uno solo, assegnato alla bacheca con `StaticResource`. Nei menu che hanno `Opened="ContextMenu_Opened"` (riquadro, argomento, bacheca), le voci «Paste» (`Tag="paste"`) sono attive solo se negli appunti ci sono file. Il pulsante … di un riquadro apre il suo stesso menu.

| Menu | Voci |
|---|---|
| `ItemMenu`, elemento | Open, Open with…, Show in Explorer, Move to…, Cut, Copy, Copy path, Rename, Unfold as tile (solo per le cartelle di «General», `CanBecomeTile`), Properties, Move to Recycle Bin |
| `TileMenu`, riquadro | Paste, Paste shortcut, New folder, New shortcut…, Open in Explorer, Rename tile, Make it a space, Fold into General, Color, Sort by, Auto size, Move tile to Recycle Bin |
| `TopicMenu`, argomento | Open in Explorer, Paste here, Paste shortcut here, New tile, New space inside, Rename, Back to tile, Color (con Auto e None), Move to Recycle Bin |
| `BoardMenu`, bacheca | Undo (con l'etichetta di `UndoLabel`, come «Undo move»), New tile, New shortcut…, Paste, Paste shortcut, Open in Explorer |

Il menu completo di Windows (7-Zip e simili) non c'è ancora: è quello che resta della v0.2.

## I riquadri sulla bacheca

- **`TileTemplate`**: un `GroupBox` con lo stile `Brace.Panel`. Nel titolo la maniglia a puntini (visibile al passaggio del mouse, solo per i riquadri che sono una cartella), la striscia del colore, il nome (o il campo per rinominarlo), il numero di elementi e il pulsante …; sotto, la lista o il messaggio di riquadro vuoto; nell'angolo, la maniglia per la misura.
- **`TileList`**, la lista di un riquadro: con `Rows` è alta esattamente quelle righe, misurate sullo schermo (`RowPitch`: al 125 % Windows arrotonda una riga da 22 a 22,4 punti); senza, segue i suoi elementi fino a 268 punti (`TileViewModel.UsualListHeight`), poco meno di dodici righe. Prende gli elementi da `Source` invece che da `ItemsSource`, perché sulla tela nasce addormentata ([tela.md](tela.md#le-righe-create-quando-servono)).
- **L'angolo** (`ResizeGrip_*`): in larghezza scatta alle colonne della bacheca (al massimo 3, e non più di quelle che ci sono), in altezza a righe, ma solo se il trascinamento si è mosso di almeno mezza riga o il riquadro aveva già un'altezza. Alla fine `ResizeTile`; se il trascinamento viene interrotto, il riquadro torna com'era. Il doppio clic torna alla misura automatica.
- **`TileGridPanel`** dispone i riquadri in colonne di almeno 280 punti, con 16 punti tra l'una e l'altra.
- Sopra i riquadri, i sotto-argomenti come pulsanti; sopra ancora, il percorso, il titolo, l'interruttore «Board | Mosaic» e **New tile**, che c'è anche sul mosaico: il tooltip (`NewTileToolTip`) dice in quale argomento va, «New tile in “Lavoro” (Ctrl+Shift+N)».

### Le icone degli elementi

`ItemViewModel` sceglie tra due icone, sovrapposte nella riga:

- **`Glyph`**, l'icona di Brace: per le cartelle e i tipi comuni (`FileGlyphs.For(kind)`), e il documento di Brace per i file che nessun programma del PC apre (`FileAssociations`).
- **`Icon`**, l'icona di Windows: per programmi, collegamenti e gli altri file. Si chiede la prima volta che la riga si vede, su un thread a parte (`ShellIcons`); intanto la riga mostra il documento di Brace.

## La barra laterale

- Larga 264 punti; chiusa (`Ctrl+B` o il pulsante nella barra del titolo) non occupa spazio, e Inlay ricorda com'era.
- **Gli argomenti** sono un `TreeView` con lo stile `TopicItem`: righe da 32 punti e nomi da 14 (`NavRowHeight`, `NavTextSize`), quelli della radice un po' più marcati, l'icona del colore dell'argomento. Quello scelto ha la cartella piena e lo sfondo `accent-subtle`, che diventa `Brace.SelectionInactive` quando il focus non è nell'albero.
- In alto l'etichetta «Spaces» e il **+** per un argomento nuovo (tooltip «New space»).

## La barra del titolo

Disegnata da Inlay come `br-titlebar` di Brace, perché Brace per WPF non ne ha ancora una: logo e nome, il pulsante della barra laterale, la ricerca al centro (al massimo 280 punti, «Search the vault»), il badge dell'archivio in uso, il pin (legato a `Window.Topmost`), riduci, ingrandisci o ripristina, chiudi (che nasconde). Gli elementi che devono ricevere i clic dentro la zona della cornice hanno `WindowChrome.IsHitTestVisibleInChrome`.

Il badge (`Inlay.ProjectBadge`) è quello neutro di Brace, con «Vault» in `Brace.TextTertiary`, un filo e il nome dell'archivio, che compare solo qui: niente colori di stato, e l'ambra della barra resta al pin. Così si legge per quello che è, e il tooltip lo ripete («The vault in use: click to switch, create, rename or move vaults»). Un clic apre **Settings** › **Vaults**. Fino alla 0.3.5 c'era l'icona dell'archivio, `Brace.Icon.Database16`, che resta nella barra di stato e nella pagina **Vaults**.

## Colori

- **Gli otto colori** (Amber, Lapis, Amethyst, Rose, Jade, Citrine, Coral, Pearl: i nomi delle pietre di un intarsio) sono scritti nei due menu **Color** di `PanelView.xaml`, quello del riquadro e quello dell'argomento. `TileColors.All` li elenca con il loro nome, ma oggi nessuno lo usa: cambiarlo non cambia i menu.
- **`TileColors.BrushFor`** dà un pennello congelato per un colore «#RRGGBB»; un valore scritto a mano che non è un colore vale come nessuno.
- **`TopicViewModel.Color`**: `none` vuol dire nessun colore; un argomento della radice mostra il suo; uno interno il suo se scelto a mano, altrimenti quello del genitore. `ColorChoice` dice quale voce del menu **Color** ha la spunta (`auto`, `none` o il colore).
- Le zone della tela prendono il colore del loro argomento.

## Movimento

- **`Motion.Enter`**: dissolvenza più un piccolo scorrimento o ingrandimento, con la durata e la curva di Brace (`Brace.DurationNormal`, `Brace.EaseEnter.Function`). Niente si muove se Windows ha le animazioni spente. Si usa per il pannello che compare, le domande e gli avvisi.
- **Gli adorner**: `DragLabelAdorner` (l'etichetta accanto al puntatore), `InsertionAdorner` (la riga dove andrà un riquadro, una riga o un argomento), `SelectionBandAdorner` (il rettangolo).

## Le impostazioni (`SettingsViewModel`, `ArchivesViewModel`)

`SettingsView` ha le pagine a sinistra (`SettingsSection`: **Vaults**, **Hotkey**, **Startup & Explorer**, **About**) e la pagina scelta a destra. Ogni cambiamento vale subito. La prima volta (`IsWelcome`) c'è solo la pagina degli archivi, sotto il titolo «Welcome to Inlay».

- **Hotkey**: `HotkeyBox` registra la combinazione premuta (con `Ctrl`, `Alt` o `Win`; `Shift` da solo no; `Esc` lascia quella di prima). `UseHotkey` la prova subito tramite `ISettingsHost.TryUseHotkey` e la salva solo se Windows la dà a Inlay; se no, sotto il campo compare il motivo.
- **Startup & Explorer**: avvio con Windows (**Start Inlay with Windows**) e voce di «Invia a» (**“Inlay” in the Send to menu of Explorer**), tramite `IWindowsIntegration`. Se Windows rifiuta, l'interruttore torna com'era e un messaggio lo dice.
- **About** (`UpdatesViewModel`, lo stesso per tutta la vita di Inlay, che `App` tiene aggiornato a ogni controllo): il logo, «Version 0.3.5» (quella di Velopack, o per un Inlay lanciato dal codice quella del progetto, `VersionOfTheCode`), e sotto **Updates** una frase (`StatusText`) che dice com'è andato l'ultimo controllo: «Checking for updates…», «Inlay is up to date: checked today at 14:56.», un controllo senza risposta, o la versione pronta. **Check for updates** (`CheckCommand`, spento durante un controllo) chiama `App.CheckForUpdates`; **Restart to update** compare con una versione pronta. «What's new in each version» apre le release su GitHub (`UpdatesViewModel.ReleasesPage`). Un Inlay lanciato dal codice dice che non si aggiorna, e non ha i pulsanti.
- **Vaults** (`ArchivesViewModel`):
  - l'elenco viene dalle impostazioni, il più recente per primo, con «In use» o «Not found» e l'icona dell'archivio, come nella barra del titolo; sotto ogni archivio, contato in background, quanto occupa e quanto spazio ha il suo disco;
  - **Create the vault** (il nome, di partenza «Vault», e la cartella, che di partenza è quella dell'utente, sullo stesso disco del Desktop), **Choose the folder…** sotto «Open a folder as a vault» (`ArchiveFolders.Adopt`), **Open**, **Show in Explorer**, **Remove from the list** (non quello in uso; la cartella resta);
  - **Rename…** e **Move…** sullo stesso disco sono un `Directory.Move` della cartella, dopo aver lasciato libero l'archivio in uso (`IArchiveHost.Release`), che poi si riapre dal posto nuovo;
  - **Move…** su un altro disco (`MoveToOtherDriveAsync`): conta quanto occupa l'archivio e controlla lo spazio, lo copia con la shell, confronta byte per byte la copia con l'originale (`CopyCheck`) e passa alla copia solo se è identica; poi chiede se mandare la cartella vecchia nel Cestino. In ogni altro caso l'archivio resta dov'era.
  - Il conto e il confronto girano in background. La copia gira su un thread STA a parte (`CopyAsync`, con `ShellThread`), con la finestra di avanzamento di Windows: Inlay resta usabile. Mentre la shell copia (`IsCopying`) la finestra **Settings** non si chiude (`TryClose`) e **Quit** aspetta (`WhenCopied`), perché una copia interrotta lascerebbe mezzo archivio sull'altro disco. Durante il confronto (`IsChecking`) compaiono l'avanzamento e «Stop the check».
  - Restano sul thread della finestra la rinomina e lo spostamento sullo stesso disco (un `Directory.Move`) e il Cestino della cartella vecchia, che di solito sono rapidi.
  - Le domande della pagina sono anche qui dei `PromptViewModel`.

## Brace e il tema di Inlay

- `App.xaml` unisce `Brace/Brace.xaml` (che porta con sé `Brace.Controls.xaml`, e da lì i token e le icone) e poi `Themes/Inlay.xaml`.
- **I file XAML e C# di `Brace/` sono generati** dal design system (lo dice la loro prima riga) e non si modificano qui: le correzioni vanno fatte in Brace, in `strumenti/modelli/wpf/`, e poi ricopiate. Li accompagnano la licenza delle icone Fluent e `PROVENIENZA.md`, che dice da quale commit vengono.
- `Brace/Brace.Wpf.cs` (namespace `Brace`) ha gli aiuti in C# del design system: `Chrome.Dark` (i colori di Brace sulla cornice di Windows, messo dallo stile `Brace.Window`), `TreeIndentConverter`, `SwitchThumbMarginConverter`, `BezierEase` e altri.
- **`Themes/Inlay.xaml`** ha quello che Inlay aggiunge a Brace: le icone che Brace non ha ancora (`Inlay.Icon.Restore16`, `Inlay.Icon.Grip16`, `Inlay.Icon.ResizeCorner16`), gli stili della barra del titolo e dei suoi pulsanti, il pin, il badge dell'archivio, la maniglia della misura dei riquadri e il logo (`Inlay.Logo`, lo stesso disegno di `Assets/inlay.ico`). L'icona per ripristinare e la barra del titolo andrebbero portate in Brace.
- I caratteri Inter e Cascadia Mono sono dentro l'eseguibile (`Fonts/*.ttf`), con le licenze copiate accanto.
- L'icona dell'app si rigenera con `python scripts/genera-icona.py src/Inlay.App/Assets/inlay.ico`.
- Il testo del pannello è in modalità `Display`, nitido a scala 1; sulla tela è `Ideal`.
