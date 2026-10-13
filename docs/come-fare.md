# Come fare per…

Ricette per i cambiamenti più comuni: quali file toccare, in che ordine, e cosa non dimenticare. Per ognuna valgono le [convenzioni](convenzioni.md) e, alla fine, i test, le anteprime e i documenti ([rilascio.md](rilascio.md#prima-di-pubblicare)).

## Un'operazione nuova che si annulla

1. **Il passo**, in `src/Inlay.Core/UndoHistory.cs`: un `sealed record XxxStep(…) : UndoStep` con tutto quello che serve per tornare indietro: i percorsi di prima e di dopo, i valori di prima, un `MetadataSnapshot` se delle cartelle escono dall'archivio.
2. **L'operazione**, in `MainViewModel`:
   - se tocca i file: `using var turn = await BeginFileWork("Doing this…", touches)`, con quello che l'operazione cambia (`FileTouches`, per sapere quali altre possono andare insieme), la shell con `RunShell(…)`, i metadati che seguono (`FollowFolder`, `ForgetFolder`), poi `Remember(step, turn)`, che mette il passo al posto dell'operazione nel registro, `Report(…)` per l'avviso con **Undo** e **Show**, `SelectAfterReload(…)` e `ReloadUnlessEditing()`;
   - se tocca solo i metadati: `ChangeMetadata(m => m.SetXxx(…), new XxxStep(…))`.
3. **L'annullamento**, sempre in `MainViewModel`:
   - un caso nello `switch` di `Undo()`: un passo che non è lì fa partire un'eccezione («Unknown undo step»);
   - un metodo `UndoXxx` che restituisce un `Outcome`: `Refused` se nel frattempo qualcosa è cambiato, `Failed` (con `KeepForRetry(step)`) se non è riuscito, `Done` se è fatto. Mai sovrascrivere, mai eliminare per sempre: si passa dal Cestino;
   - l'etichetta in `UndoLabel`, per il menu della bacheca.
4. **I test** in `MainViewModelTests`: fare, controllare, annullare, controllare; e il caso in cui qualcosa è cambiato prima dell'annullamento.
5. **I documenti**: la voce **Annullare** di [uso.md](uso.md), e PROGETTO se cambia cosa vede l'utente.

## Un campo nuovo nei metadati

1. **`FolderMetadata`** (`ArchiveMetadata.cs`): una proprietà che può essere null, dove null vuol dire «non c'è», così i file vecchi si leggono come prima. Aggiornare `IsEmpty` e `SameAs`. I valori devono essere immutabili, perché `Clone` fa una copia superficiale: una lista si sostituisce, non si modifica.
2. **`ArchiveMetadata`**: `XxxOf(key)` e `SetXxx(key, value)`, che restituisce `true` solo se è cambiato qualcosa, usando `Change`. Il valore di sempre non si scrive (come la misura usuale o l'ordine per nome).
3. Spostare, eliminare e annullare funzionano già: `MoveFolder`, `RemoveFolder`, `Snapshot` e `Restore` lavorano su voci intere.
4. Se cambia come si legge l'archivio, `ArchiveReader` e i `record` del modello; se si vede, il view model e il suo `Update`.
5. Il nome nel JSON viene da solo, in camelCase; un enum si scrive come stringa.
6. **I test** in `MetadataStoreTests`: lettura e scrittura, e la forma del file che i documenti descrivono.
7. **I documenti**: PROGETTO.md §4, Metadati; nella nota di versione, cosa succede tornando alla versione prima (il campo si perde al primo salvataggio).

## Una voce di menu

1. **In `Views/PanelView.xaml`**, nel menu giusto (`ItemMenu`, `TileMenu`, `TopicMenu`, `BoardMenu`): `<MenuItem Header="…" Click="Xxx_Click" />`. `InputGestureText` mostra solo il tasto, non lo gestisce. `Tag="paste"` se la voce serve solo con dei file negli appunti: funziona nei menu con `Opened="ContextMenu_Opened"`, e `ItemMenu` non ce l'ha.
2. **In `Views/PanelView.xaml.cs`**, il gestore: `DataContextOf<T>(sender)` dà l'elemento, il riquadro o l'argomento; per gli elementi si agisce su `TargetsOf(item)`, cioè sulla selezione se l'elemento ne fa parte. Il gestore chiama un metodo di `MainViewModel`; se restituisce un `Task`, con `Observe(…)`.
3. **La logica** va in `MainViewModel`, dove si può provare con i test, non nel gestore.
4. `PanelMenusTests` costruisce già tutti i menu con i loro dati e ne prepara le voci: un binding che non si può attaccare fa fallire il test, invece di bloccare il tasto destro com'è successo con **Colore** nella 0.3.0.
5. **I documenti**: la voce **Tasto destro** di [uso.md](uso.md).

## Un tasto

1. **In `PanelView.OnPreviewKeyDown`**, rispettando l'ordine che c'è: prima le domande aperte, poi il campo di una rinomina (che gestisce i suoi tasti), poi la ricerca.
2. **In `Views/ShortcutHints.cs`** (`ShortcutGroup.For`), se è un tasto da sapere: compare sotto **?** nella barra di stato, attraverso `MainViewModel.Shortcuts`. E nel tooltip del pulsante che fa la stessa cosa, se c'è.
3. **I documenti**: la parte **Uso** di [uso.md](uso.md) e la tabella dei tasti di PROGETTO.md §5.
4. Attenzione ai tasti di GlazeWM: nel config attuale `Alt+Enter` è suo (PROGETTO.md §7).

## Un'impostazione

1. **`Services/AppSettings.cs`**: la proprietà con il suo valore predefinito. Un file vecchio, che non ce l'ha, prende quello.
2. **`ViewModels/SettingsViewModel.cs`**: un `[ObservableProperty]` e il suo `partial void OnXxxChanged`, che scrive nelle impostazioni e chiama `Save()`. Se l'app deve reagire, un metodo nuovo in `ISettingsHost`, da fare in `App`, nel `PreviewHost` di `PreviewRenderer` e nei finti dei test. Se deve saperlo il pannello, `App` lo passa a `MainViewModel` all'avvio e quando cambia, come fa con la scorciatoia (`Hotkey`).
3. **`Views/SettingsView.xaml`**: nella pagina giusta. Per una pagina nuova: un valore in `SettingsSection`, la sua riga nella barra a sinistra, la pagina con `SectionVisibilityConverter` e il nome della sezione, e il nome in `PreviewRenderer` per `--section`.
4. **I test** in `SettingsViewModelTests`, e l'anteprima con `--settings --section …`.
5. **I documenti**: **Settings** in [uso.md](uso.md), e le impostazioni del PC in PROGETTO.md §4.

## Un'opzione della riga di comando

1. **`Services/CommandLine.cs`**: il campo in `CommandLineOptions` e il caso in `Parse`. Un comando per l'Inlay già aperto va anche in `Commands` e in `App.Execute`.
2. Per un'anteprima: in `PreviewRenderer.Render`, lo stato da preparare prima di disegnare.
3. **I test** in `CommandLineTests`; **i documenti**: **Riga di comando** in [uso.md](uso.md) e la tabella in [windows.md](windows.md#la-riga-di-comando-commandlineoptions).

## Un'icona, o un tipo di file

1. Cercarla prima tra le icone di Brace (`Brace/Brace.Icons.xaml`).
2. Se manca, il posto giusto è Brace; intanto si può disegnare in `Themes/Inlay.xaml` come `Inlay.Icon.Xxx16`, monocromatica, da portare in Brace più avanti.
3. Per un tipo di file nuovo: un valore in `FileKind`, le sue estensioni in `FileKinds`, la sua icona in `Views/FileGlyphs.cs`, e un caso in `FileKindTests`.

## Un cambiamento alla tela

- Il livello arriva per eredità (`CanvasSurface.Level`): niente binding che cercano la superficie risalendo l'albero.
- Quello che si vede solo da vicino si nasconde con `Hidden`, non `Collapsed` (lo stile `NearOnly`), così il riquadro tiene la sua misura e zoomando non si muove niente.
- Le liste dei riquadri nascono addormentate: chi ha bisogno delle righe di una lista la sveglia (`TileList.Wake`), come fa `FirstSelectedRow`.
- Prima e dopo, `--benchmark-canvas` su un archivio pesante, e le anteprime con `--canvas --zoom` ([test.md](test.md#verificare-linterfaccia-senza-aprirla)).
- **Per la fase 3** (le zone dove le mette l'utente) `CanvasPlacement` è già pronto: le zone con `X` e `Y` restano dove sono e le altre ci girano intorno, ed è provato da `CanvasPlacementTests`. Mancano il campo `"canvas"` nei metadati (vedi sopra), il passaggio dei posti salvati a `ZoneCanvasPanel`, il trascinamento del titolo e dell'angolo delle zone, e i passi per annullarli.
