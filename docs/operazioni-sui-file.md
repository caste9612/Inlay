# Le operazioni sui file

Spostare, copiare, collegare, rinominare, creare, eliminare e annullare: come sono fatte, e cosa le rende sicure. Le regole di base (cosa succede trascinando, il Cestino, il registro dei passi) stanno in `Quiver.Core` e sono descritte in [archivio.md](archivio.md). Qui c'è come le usa l'app, quasi tutto in `MainViewModel`.

I principi da rispettare sono quelli di [PROGETTO.md §3](../PROGETTO.md#3-principi): niente cancellazioni definitive, niente sovrascritture silenziose, ogni operazione si può annullare.

## Da dove partono

| Gesto | Metodo di `MainViewModel` |
|---|---|
| trascinare file su un riquadro o un argomento | `Transfer`, oppure `SaveDropped` per allegati e link ([più sotto](#trascinare)) |
| `Ctrl+V`, «Incolla collegamento» | `PasteInto`, `PasteLinksInto` |
| «Sposta in…», «Invia a Quiver» | `AskDestination` → `SendTo` → `Transfer` |
| `Canc` | `AskRecycle` → `Recycle` |
| `F2`, secondo clic lento | `BeginRename`, poi `CommitRename` o `CancelRename` |
| nuovo riquadro, argomento, cartella, collegamento | `NewTile`, `NewTopic`, `NewFolder`, `AskNewLink` |
| trascinare un riquadro, un argomento, delle righe | `MoveTile`, `MoveTopic`, `MoveItems` |
| angolo del riquadro, «Ordina per», colore, promuovi | `ResizeTile`, `SortTile`, `SetSortDirection`, `SetTileColor`, `SetTopicColor`, `PromoteTile`, `DemoteTopic`, `PromoteItem`, `DemoteTile` |
| `Ctrl+Z`, «Annulla» su un avviso | `Undo`, `UndoFrom` |

## Il turno e i thread

Spostamenti, copie, eliminazioni e annullamenti passano da `BeginFileWork(activity, touches)`, che restituisce il loro turno (`FileWork`):

- **Insieme, se toccano file diversi.** `touches` dice cosa cambia l'operazione (`FileTouches`, [archivio.md](archivio.md#operazioni-insieme-filetouches)). Un'operazione parte appena nessuna di quelle chieste prima di lei, in corso o in attesa, la intralcia (`StartWhatCan`): così parte dai file come li hanno lasciati, e non passa davanti a una che aspetta gli stessi file. Quelle su file diversi vanno avanti insieme.
- **`Ctrl+Z` le aspetta tutte**: `Undo` chiede il turno senza `touches`, cioè su tutto. Aspetta ogni operazione chiesta prima di lui e annulla l'ultima finita; quelle chieste dopo aspettano lui.
- **`IsBusy`** è vero da quando un'operazione chiede il turno a quando l'ultima finisce. La barra di stato mostra i LED e `Activity`: l'ultima operazione partita, e quante altre sono in corso o in attesa («Copio «Foto» in «Lavoro»… · un'altra in corso · 1 in attesa»); il suo tooltip, `ActivityDetails`, le elenca tutte. `FileWork.Say` cambia quello che dice un'operazione mentre lavora, come l'annullamento di uno spostamento («Rimetto 3 elementi dov'erano…»). `WhenIdle()` è il compito che **Esci** aspetta.
- **Il turno si restituisce** con il `Dispose` del `FileWork` (`using var turn = await BeginFileWork(…)`). `EndFileWork` avvisa la finestra (`FileOperationCompleted`, per riprendersi il focus), fa partire quelle che aspettavano e, quando non ne resta nessuna, se nel frattempo l'archivio è cambiato ricostruisce l'indice della ricerca una volta sola.
- **`RunShell(operation)`** fa girare la chiamata alla shell su un thread STA tutto suo (`ShellThread`), uno per operazione: la finestra resta usabile mentre Windows mostra le sue finestre di avanzamento e di conflitto, una per operazione, come in Explorer. Nelle anteprime e nei test (`live: false`) la chiamata avviene subito. Passa da `ShellRunner`, che i test sostituiscono per trattenere le operazioni e vedere quali vanno insieme.
- **Le rinomine no**: `RenameNow` chiama la shell sul thread dell'interfaccia, perché sono brevi e il campo del nome aspetta la risposta. Mentre la shell lavora, `_renaming` impedisce le ricariche.
- **`Start(task)`**: le operazioni lanciate da una domanda o da «Sposta in…», che si chiudono senza aspettarle. Un errore imprevisto torna comunque sul thread dell'interfaccia, al gestore degli errori dell'app.
- **La copia di un archivio su un altro disco**, in Impostazioni › Archivi, gira anche lei su un `ShellThread` ([interfaccia.md](interfaccia.md#le-impostazioni-settingsviewmodel-archivesviewmodel)).

## Spostare e copiare (`Transfer`)

`Transfer(paths, destinationFolder, kind, foldersAsRows)` aspetta il turno, con `FileTouches.Transfer(paths, destinationFolder)`, poi `TransferNow`:

1. **La cartella di arrivo** viene creata se manca (`EnsureFolder`), per esempio se nel frattempo è stata tolta da Explorer.
2. **I collegamenti** (`TransferKind.Link`) vanno a `CreateLinks` ([più sotto](#collegamenti)).
3. **Lo spazio**: per le copie, e per gli spostamenti da un altro disco (Windows copia e poi cancella), `NotEnoughRoom` conta quanto occupano i file (`FolderSize`, lontano dal thread dell'interfaccia) e lo confronta con lo spazio libero (`DiskOf`), tolto quello che serve alle copie già in corso sullo stesso disco (`FileWork.Room`), tutto, anche la parte già copiata: meglio un avviso di troppo che due copie a metà. Se non ci stanno, un avviso lo dice e non si comincia; se ci stanno, l'operazione tiene il suo spazio fino alla fine.
4. **Dove arriverà ogni elemento** (`landings`, per gli spostamenti): solo quelli il cui nome è libero nella destinazione. Se il nome è preso, l'utente sceglie nella finestra della shell cosa fare, quindi non si sa dove finirà l'elemento, e `Ctrl+Z` lo lascerà stare. Per le copie conta invece quello che compare di nuovo nella cartella (punto 9).
5. **I metadati delle cartelle che escono dall'archivio** vengono copiati prima (`SnapshotOf`), per ridarglieli se tornano con `Ctrl+Z`.
6. **La shell** copia (con i nomi «- Copia» se la copia è nella stessa cartella dell'originale) o sposta.
7. **I metadati seguono le cartelle spostate**: `FollowFolder` se sono ancora nell'archivio, `ForgetFolder` se ne sono uscite.
8. **Le cartelle arrivate in un argomento diventano righe del suo «Generale»** (`MakeRows`, che le segna `"kind": "item"`): una cartella lasciata su un argomento si vede come una riga, non come un riquadro con tutto il suo contenuto. Restano come sono quelle che arrivano già argomenti o righe, e tutte quando `foldersAsRows` è falso: un riquadro preso per il titolo resta un riquadro, e un argomento spostato sotto un altro (`MoveTopic`) resta un argomento. Nella cartella di un riquadro, o fuori da un argomento, nessun segno.
9. **Il passo per annullare**: `MoveStep` con gli elementi che si sono davvero spostati, ognuno con `BecameRow` se è diventato una riga; `CopyStep` con i nomi nuovi comparsi nella cartella che cominciano come un originale, compresa una copia tenuta con un nome come «Nome (2)». Se i nomi della cartella, prima o dopo, non si sono potuti leggere (`EntryNames` restituisce null), la copia non si annulla: le copie non si distinguerebbero da quello che c'era già.
10. **L'avviso** (`Report`) dice cosa è successo, con **Annulla** e **Mostra**. Quello che è arrivato viene selezionato dopo la ricarica (`SelectAfterReload`), come in Explorer.

Il tipo di operazione (spostare, copiare o collegare) lo decide chi chiama: per i trascinamenti `TransferRules.Choose`, per gli appunti il taglia o copia, per «Sposta in…» sempre spostare o collegare.

## Gli appunti

- **`CopyToClipboard(items, cut)`**: i file vanno negli appunti nel formato di Explorer (`FileClipboard`). Quelli tagliati sono ricordati in `_cutPaths` e mostrati sbiaditi (`IsCut`) sulla bacheca, sulla tela e tra i risultati.
- **`PasteInto(folder)`**: rifiuta di incollare una cartella dentro se stessa. Dopo un taglio svuota subito gli appunti, perché un secondo `Ctrl+V` durante lo spostamento non provi a spostare di nuovo gli stessi file, e sposta; dopo una copia, copia.
- **`PasteLinksInto(folder)`**: collegamenti ai file degli appunti, che restano dove sono.

## «Sposta in…» e «Invia a Quiver»

- **`AskDestination(paths)`** apre l'elenco dei posti (`DestinationPickerViewModel`) con le destinazioni di `ArchiveReader.ReadDestinations`, tolte quelle dove l'operazione non farebbe niente. Se tutti gli elementi hanno già un loro posto (programmi, cose in rete), l'elenco crea collegamenti invece di spostare.
- **`SendTo`** ricorda la destinazione tra le recenti (`RecentDestinations`) e fa partire `Transfer` senza aspettarlo: l'elenco si chiude subito.
- **«Invia a Quiver»** in Explorer lancia `Quiver.exe --sort <file>…`; l'istanza unica passa i file a quella aperta, che mostra la finestra e chiama `AskDestination` ([windows.md](windows.md#listanza-unica-singleinstance)).
- **`ShowNotice`** («Mostra» su un avviso): sulla bacheca apre l'argomento dove sono finiti gli elementi e li seleziona; sulla tela vola al riquadro (`ShowOnCanvasRequested`).

## Collegamenti

- **`CreateLinks(targets, folder)`**: un `.lnk` per ogni elemento (`ShellLinks.CreateIn`), con un `LinkStep`. Un errore su un elemento non ferma gli altri.
- **`AskNewLink(folder)`**, «Nuovo collegamento…»: la domanda accetta un percorso esistente (anche con variabili come `%USERPROFILE%`), che diventa un `.lnk`, oppure l'indirizzo di un sito, che diventa un `.url` (`WebShortcut`). Il collegamento nuovo nasce selezionato, con il nome pronto da scrivere.

## File da altri programmi (`SaveDropped`)

Allegati e messaggi di Outlook, link e immagini del browser non sono ancora file: `VirtualFiles.SaveTo` li scrive nella cartella mentre il rilascio è in corso, perché dopo il programma potrebbe non darli più. Quello che è stato salvato diventa un `CopyStep`, anche se un file successivo non riesce: annullare lo manda nel Cestino.

## Eliminare (`AskRecycle`, `Recycle`)

- I file vanno subito nel Cestino, come sul desktop. Se c'è almeno una cartella, prima una domanda di conferma con il pulsante rosso.
- `Recycle` copia i metadati delle cartelle, segna l'ora (due secondi prima, per sicurezza) e chiede alla shell di eliminare con il Cestino. Se un elemento non può andare nel Cestino, come su un disco di rete, è Windows stesso ad avvisare prima di eliminarlo per sempre (`FOF_WANTNUKEWARNING`).
- Poi trova nel Cestino dove è finito ogni elemento (`RecycleBin.FindDeleted`) e registra un `RecycleStep` con quelli trovati e il numero di quelli eliminati per sempre. L'avviso è un successo se sono tutti nel Cestino, un'attenzione se no.

## Creare

`NewTile`, `NewTopic` e `NewFolder` creano una cartella con un nome libero («Nuovo riquadro», «Nuovo argomento», «Nuova cartella»), registrano un `CreateStep` e cominciano subito la rinomina, come «Nuova cartella» sul desktop.

- Un argomento dentro un altro va segnato nei metadati (`SetTopic`); nella radice no, perché lo è già.
- Una cartella chiesta in «Generale» nasce nella cartella del suo argomento come una delle sue righe (`SetItem`), non come un riquadro. Sulla tela è l'argomento della zona, non per forza quello scelto nella barra laterale.

## Rinominare

- **`RenameableViewModel`** è la base di elementi, riquadri e argomenti. `BeginEdit` prepara il nome senza l'estensione nascosta (`.lnk`, `.url`); `EditSelectionLength` fa partire selezionato il nome senza l'estensione, come Explorer; `NewFileName` rimette l'estensione nascosta.
- **`CommitRename`** controlla il nome (`ArchiveNames.Validate`) e che non sia già preso (cambiare solo maiuscole e minuscole si può), rinomina con la shell e registra un `RenameStep`. Un elemento rinominato tiene il suo posto nell'ordine a mano (`KeepPlace`), una cartella si porta dietro i suoi metadati (`FollowFolder`), un elemento selezionato resta selezionato.
- Se c'è un problema, `CommitRename` lo restituisce e la vista lo dice in un avviso. Dopo `Invio` il nome resta in modifica, per correggerlo; se invece il campo perde il focus, il nome torna com'era.
- **`CommitAllEdits`**: un nome in modifica viene confermato quando la finestra si nasconde, quando si passa dalla bacheca alla tela o viceversa, e prima di un annullamento. Se non si può, il nome torna com'era e un avviso dice perché.
- Dai risultati della ricerca non si rinomina.

## I metadati che seguono

- **`UpdateMetadata(change)`** legge `archive.json` dal disco, applica il cambiamento e salva solo se è cambiato qualcosa. Non c'è una copia in memoria da tenere allineata: ogni cambiamento parte dal file. Se il file non si può scrivere, un avviso lo dice.
- **`ChangeMetadata(change, step)`** serve alle operazioni che toccano solo i metadati (ordine, misura, ordinamento, colore, promozione, righe di «Generale»): registra il passo solo se è cambiato qualcosa, poi ricarica.
- **Le regole**: una cartella rinominata o spostata dentro l'archivio porta con sé le sue voci (`FollowFolder` → `MoveFolder`); una cartella eliminata o uscita dall'archivio le perde (`ForgetFolder` → `RemoveFolder`), ma il passo per annullare ne tiene una copia. Anche l'argomento scelto segue la sua cartella (`FollowSelection`), o passa al genitore (`ForgetSelection`).
- **I colori degli argomenti nuovi** si danno a ogni ricarica (`TopicColors.AssignMissing`) e si scrivono nell'archivio, tranne nelle anteprime e nelle misure (`readOnly`), che li mostrano soltanto.

## Annullare

`Remember(step)` registra un passo in `UndoHistory` e aggiorna `CanUndo`, l'etichetta del menu (`UndoLabel`, «Annulla lo spostamento»…) e il pulsante **Annulla** degli avvisi, che resta solo su quello dell'ultima operazione: dopo un'altra, `Ctrl+Z` annullerebbe quella.

`Undo()` conferma i nomi in modifica, aspetta il turno (dopo tutte le operazioni chieste prima), prende l'ultimo passo, toglie gli avvisi che lo offrivano e lo esegue al contrario. L'esito è un avviso: fatto (successo), rifiutato perché nel frattempo qualcosa è cambiato (attenzione), non riuscito (errore). Se non è riuscito, di solito perché un file è aperto in un altro programma, il passo torna nel registro (`KeepForRetry`) e `Ctrl+Z` si può ripetere.

| Passo | Come si annulla |
|---|---|
| `MoveStep` | rimette con `MoveEach` solo gli elementi che sono ancora dove erano arrivati e il cui posto di prima è libero; i metadati tornano indietro (`FollowBack`), e una cartella diventata riga arrivando torna quello che era |
| `TopicMoveStep` | come lo spostamento, poi ridà all'argomento il posto che aveva tra gli altri |
| `CopyStep`, `LinkStep` | manda nel Cestino le copie o i collegamenti che ci sono ancora; si rifiuta se un disco non ha il Cestino, perché lì eliminare sarebbe per sempre |
| `RenameStep` | rinomina di nuovo, se il nome di prima è libero |
| `CreateStep` | toglie la cartella, solo se è ancora vuota |
| `RecycleStep` | rimette gli elementi dal Cestino (`RecycleBin.TryRestore`), prima le cartelle e poi il loro contenuto, avvisa Explorer e ridà i metadati |
| `TileOrderStep`, `TopicOrderStep` | ridà a ogni riquadro o argomento che c'è ancora il posto di prima |
| `TopicStep`, `ItemStep`, `ColorStep`, `ArrangeStep`, `SizeStep` | rimette nei metadati quello che c'era, se la cartella c'è ancora |

Le garanzie:

- **Un annullamento non sovrascrive mai niente**: rimette le cose solo in posti liberi.
- **Elimina per sempre solo una cartella vuota creata da Quiver** (`CreateStep`). Tutto il resto passa dal Cestino, e dove il Cestino non c'è l'annullamento non si fa.
- **Quello che è cambiato nel frattempo resta com'è**, e l'avviso dice quanti elementi sono stati lasciati stare.
- **Il registro si svuota cambiando archivio** (`LoadArchive`). Vive solo in memoria, fino a 50 passi.

## Trascinare

I trascinamenti sono gestiti in `Views/PanelView.xaml.cs`, che decide cosa fare e poi chiama `MainViewModel`.

### Cosa si prende in mano

| Cosa | Dati del trascinamento | Effetti permessi |
|---|---|---|
| righe di un riquadro o dei risultati della ricerca (la selezione, o la riga sola se non era selezionata) | `FileDrop` con i percorsi: li capiscono anche Explorer, la posta e gli altri programmi | spostare, copiare, collegare |
| un riquadro, preso per il titolo (solo se è una cartella sua) | `Quiver.Tile` con la cartella, più `FileDrop` con la cartella | spostare, copiare |
| un argomento della barra laterale | `Quiver.Topic` con la chiave dei metadati: lo capisce solo Quiver, così un argomento non esce dall'archivio trascinandolo in Explorer | spostare |

Mentre si trascina, `DragLabelAdorner` mostra accanto al puntatore il nome o «N elementi». Un trascinamento che finisce fuori da Quiver lo esegue il programma che lo riceve, per esempio Explorer: Quiver non registra niente, e il suo `Ctrl+Z` non lo annulla.

### Dove si può lasciare, e cosa succede (`PlanDrop`)

Le destinazioni sono i riquadri, lo sfondo della bacheca (la cartella dell'argomento), i pulsanti dei sotto-argomenti sopra la bacheca e gli argomenti della barra laterale. `PlanDrop` produce un `DropPlan`, in quest'ordine:

1. **Un argomento** (`PlanTopicDrop`): sul 30 % alto di una riga va prima di quell'argomento, sul 30 % basso dopo (e dopo tutto quello che c'è aperto sotto), nel 40 % centrale ci entra dentro. Sulla parte vuota della barra va in fondo agli argomenti della radice. Non può andare dentro se stesso né dentro un suo sotto-argomento (`MainViewModel.CanGoUnder`). Poi `MoveTopic`: tra gli stessi argomenti cambia solo l'ordine; sotto un altro argomento la cartella si sposta, resta un argomento, e un nome già preso lì viene rifiutato invece di unire le due cartelle.
2. **La parte vuota della barra laterale** accetta solo argomenti.
3. **Un riquadro preso per il titolo**, lasciato sullo sfondo o su un riquadro accanto, cambia posto (`MoveTile`): sullo sfondo va in fondo, su un altro riquadro va prima di lui. Su un riquadro di un'altra zona della tela non fa niente: smistare tra argomenti diversi sulla tela è la fase 2. Lasciato su un argomento della barra, invece, conta come cartella e ci viene spostato dentro (punto 5), e lì resta un riquadro.
4. **Righe lasciate nel loro stesso riquadro**, senza `Ctrl` né `Alt`, cambiano posto (`MoveItems`, ordine a mano): vanno prima della riga sotto il puntatore, o dopo l'ultima in vista.
5. **File** (`FileDrop`): la destinazione è la cartella del riquadro, dell'argomento o della bacheca. Il tipo di operazione lo decide `TransferRules.Choose` con i tasti tenuti premuti; senza tasti, quello che ha un suo posto diventa un collegamento (`preferLink`). Le cartelle che arrivano in un argomento diventano righe di «Generale», tranne un riquadro preso per il titolo (`FoldersAsRows`). Se il programma da cui arrivano i file non permette quell'effetto, si ripiega sulla copia, o su niente.
6. **File che non sono ancora file** (allegati, link del browser, senza `FileDrop` ma con `FileGroupDescriptorW`): `SaveDropped`, come una copia.

### Durante e alla fine

- **`DragOver`** mostra l'effetto (spostare, copiare, collegare) e illumina la destinazione: il bordo del riquadro in `accent`, o la riga dell'argomento in `accent-subtle`. Per un riquadro, una riga o un argomento che cambia posto, `InsertionAdorner` disegna la linea dove andrà.
- **Aprire restando sopra**: con un trascinamento di file sopra un argomento o un pulsante di sotto-argomento per 700 ms, la bacheca passa lì, come le cartelle di Explorer. Il conto parte quando si entra, e muoversi lì sopra non lo fa ripartire. Non succede trascinando un riquadro o un argomento.
- **`Drop`** risponde sempre «nessun effetto» al programma da cui arrivano i file: lo spostamento o la copia li fa Quiver, e l'altro programma non deve cancellare niente. Il lavoro parte con `Dispatcher.BeginInvoke`, a trascinamento finito, così una copia lunga non tiene bloccato l'altro programma. L'unica eccezione è `SaveDropped`, che deve leggere il contenuto mentre il rilascio è ancora in corso.
