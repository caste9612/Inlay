# Il modello dell'archivio (`Quiver.Core`)

`Quiver.Core` contiene tutto quello che riguarda l'archivio e non ha bisogno di un'interfaccia: come si legge, cosa c'è nei metadati, le regole per spostare, copiare o collegare, il registro per annullare, il Cestino, la ricerca, il posto delle zone sulla tela. È una libreria `net10.0` senza pacchetti esterni, con i suoi test in `tests/Quiver.Tests`.

Le regole dell'archivio, viste da chi lo usa, sono in [PROGETTO.md §4](../PROGETTO.md#4-come-è-organizzato-larchivio). Qui c'è come sono scritte nel codice.

## Il modello

Sono tutti `record` immutabili, letti dal disco a ogni ricarica. L'app li avvolge nei suoi view model, che invece restano vivi tra una ricarica e l'altra ([interfaccia.md](interfaccia.md)).

| Tipo | Cos'è | File |
|---|---|---|
| `Topic` | un argomento: cartella, chiave dei metadati, sotto-argomenti, colore | `Board.cs` |
| `Tile` | un riquadro: titolo, cartella, se è «Generale», elementi già in ordine, colore, misura, ordinamento | `Board.cs` |
| `Board` | la bacheca di un argomento: l'argomento e i suoi riquadri | `Board.cs` |
| `ArchiveItem` | un elemento: percorso, nome, se è una cartella, data di modifica, dimensione | `ArchiveItem.cs` |
| `Destination` | un posto dove mandare file, per «Sposta in…» | `ArchiveReader.cs` |

- `ArchiveItem.DisplayName` toglie `.lnk` e `.url`, come Explorer. `Size` è null per le cartelle; le date sono in ora locale.
- `Topic.Color` è il colore tenuto nei metadati, e `ColorIsAutomatic` dice se l'ha dato Quiver. Cosa si vede lo decide l'app (`TopicViewModel.Color`): un sotto-argomento senza un colore scelto per lui, nessuno o solo quello automatico che aveva quando stava nella radice, mostra quello del suo argomento.

## Nomi e chiavi (`ArchiveLayout`)

- `.quiver` (la cartella dei metadati) e `archive.json` sono costanti di `ArchiveLayout`.
- I nomi che iniziano con un punto (`.quiver`, `.git`) non si mostrano mai, e nemmeno i file con l'attributo Nascosto o di Sistema (`TopLevelOnly`).
- **La chiave dei metadati** di una cartella è il suo percorso relativo alla radice, con le barre `/`: `Lavoro/Clienti/Rossi`. Le chiavi si confrontano senza badare a maiuscole e minuscole, come i percorsi di Windows. `IsKeyAtOrBelow` dice se una chiave è una cartella o qualcosa che sta dentro.
- `IsLinkToElsewhere`: junction e collegamenti simbolici non si seguono, né nella ricerca né nel calcolo delle dimensioni, perché potrebbero girare in tondo o uscire dall'archivio. I segnaposto di OneDrive invece sono cartelle normali.

## Leggere l'archivio (`ArchiveReader`)

`new ArchiveReader(root, metadata)`. Ogni metodo rilegge il disco: non c'è cache.

- `ReadTopics()`: le cartelle della radice sono argomenti, anche una `_Da smistare` lasciata dalle versioni che l'avevano; più in basso lo sono solo quelle con `"kind": "topic"`. Vengono prima quelle con un `"order"`, poi le altre per nome (l'ordinamento è stabile).
- `ReadBoard(topic)`: prima «Generale», se l'argomento ha file sciolti, poi un riquadro per ogni sottocartella che non è un argomento, nello stesso ordine.
  - «Generale» usa la chiave del suo argomento per misura, ordinamento e ordine a mano, ma non ne mostra il colore.
  - Il colore di un riquadro è `ChosenColorOf`: quello dato da Quiver quando la cartella era un argomento della radice non conta.
  - Gli elementi passano da `ItemArrangement.Arrange` ([più sotto](#ordine-degli-elementi-itemarrangement-naturalstringcomparer)).
- `ReadRootFiles()`: i file lasciati sciolti nella radice, che nessun argomento mostra: la barra di stato dice quanti sono.
- `ReadDestinations()`: per «Sposta in…», nell'ordine della barra laterale: ogni argomento (per i suoi file sciolti), i suoi riquadri, poi i sotto-argomenti. `Location` è il percorso da mostrare, come «Lavoro › Clienti».
- Gli elementi di una cartella: prima le cartelle, poi i file, in ordine naturale («Verbale 2» prima di «Verbale 10»). Una cartella che non si può leggere si mostra vuota.

Le costanti che l'utente vede: `GeneralTileTitle` («Generale») e `PathSeparator` (« › »).

## I metadati (`ArchiveMetadata`, `MetadataStore`)

Il formato del file, con un esempio, è in [PROGETTO.md §4, Metadati](../PROGETTO.md#metadati). Nel codice:

- `ArchiveMetadata.Folders` è un dizionario da chiave a `FolderMetadata`, senza distinzione di maiuscole.
- `FolderMetadata` ha `Kind`, `Workspace`, `Color`, `AutoColor`, `Size`, `Order`, `Sort` e `Items`. Una voce che resta vuota (`IsEmpty`) viene tolta: il file tiene solo quello che serve.
- Ogni metodo che cambia qualcosa (`SetTopic`, `SetColor`, `SetOrder`, `SetSize`, `SetSort`, `SetItemOrder`, `SetArrangement`, `RenameItem`, `MoveFolder`, `RemoveFolder`, `Restore`) restituisce `true` solo se è cambiato davvero: chi lo chiama salva solo allora.
- `MoveFolder(oldKey, newKey)` sposta la voce e tutte quelle sotto: così i metadati seguono una cartella rinominata o spostata da Quiver. Se succede da Explorer nessuno lo sa, e le voci restano sotto il nome vecchio.
- `Snapshot(keys)` e `Restore(snapshot)` copiano e rimettono le voci di alcune cartelle e di tutto quello che c'è sotto. È così che una cartella mandata nel Cestino, o portata fuori dall'archivio, ritrova colore, misura e ordine quando torna con `Ctrl+Z`. `MetadataSnapshot.Restrict` ne tiene solo una parte.
- `TileSize`: da 1 a 3 colonne e da 2 a 40 righe, o righe automatiche (null). La misura di sempre (una colonna, righe automatiche) non si scrive.
- `ItemSort` con `SortKey` (`Name`, `Modified`, `Type`, `Size`): per nome dalla A è l'ordine di sempre e non si scrive. `ItemSort.For(key)` dà il verso di un ordinamento appena scelto: data e dimensione partono dal più grande.
- L'ordine a mano (`Items`) e l'ordinamento (`Sort`) si escludono: `SetSort` cancella il primo, `SetItemOrder` il secondo. `RenameItem` fa tenere il posto a un elemento rinominato.
- `Workspace` è già nel formato, per l'argomento legato a un workspace di GlazeWM (v0.4), ma il codice non lo usa ancora.
- Il posto delle zone sulla tela (`"canvas"`, [PROGETTO.md §11](../PROGETTO.md#11-piano-la-tela)) non c'è ancora: arriva con la fase 3.

`MetadataStore` legge e scrive `<radice>\.quiver\archive.json`:

- JSON indentato, proprietà in camelCase, valori null omessi, enum come stringhe (`"modified"`, non `1`).
- Salva scrivendo prima `archive.json.tmp` e poi spostandolo sopra il file vero, così un crash non lascia mai un file a metà. La cartella `.quiver` viene segnata come nascosta.
- Un file che non è JSON valido viene copiato in `archive.json.corrupt` e l'archivio parte con metadati vuoti: il file rovinato non va perso al salvataggio successivo. Un errore del disco invece arriva a chi ha chiamato: `MainViewModel` lo scrive nel log e, in lettura, parte da metadati vuoti, in scrittura lo dice in un avviso.
- Un campo sconosciuto viene ignorato in lettura, e quindi perso al primo salvataggio. Per questo una versione vecchia di Quiver perde i campi nuovi, come dicono le note di versione. `Version` vale 1 e non c'è ancora una migrazione.

## Gli archivi (`ArchiveFolders`)

- Un archivio è una cartella che contiene `.quiver` (`IsArchive`).
- `Check(folder, reserved, otherArchives)` dà il motivo, in italiano, per cui una cartella non può fare da archivio, o null. Non può essere un disco intero, né una cartella riservata (`ReservedFolder`) o una che ne contiene una, né stare dentro un altro archivio o contenerne uno. Le cartelle riservate le elenca l'app ([windows.md](windows.md#cartelle-di-windows-windowsfolders)); dentro la cartella dell'utente, l'unica con `InsideAllowed`, un archivio può stare.
- `Adopt` rende archivio una cartella esistente, `Create` ne fa una nuova (errore se il nome è preso), `IsSameDrive` dice se uno spostamento è una semplice rinomina.

## Nomi validi (`ArchiveNames`)

- `Validate(name)` dice in italiano perché un nome non va bene (vuoto, caratteri vietati, punto o spazio in fondo, punto all'inizio, nomi riservati come `CON` o `LPT1`, oltre 255 caratteri), o null se va bene.
- `Unique(folder, name, extension)` trova un nome libero, come Explorer: «Nome», poi «Nome (2)», «Nome (3)»…

## Le modifiche da fuori (`ArchiveWatcher`)

- Un `FileSystemWatcher` su tutta la radice, sottocartelle comprese.
- Guarda solo i nomi (creazione, eliminazione, rinomina), non il contenuto né le date: le date si aggiornano perché la bacheca si rilegge a ogni apertura del pannello.
- Gli eventi vicini si fondono in uno solo, `Changed`, dopo un ritardo che sceglie chi lo crea; arriva su un thread del pool. Un errore del watcher, come il buffer pieno con eventi persi, fa partire comunque una ricarica.
- Se la radice non si può osservare, come può succedere su un disco di rete, `MainViewModel` lo scrive nel log, e la bacheca si rilegge comunque a ogni apertura del pannello.

## Spostare, copiare o collegare (`TransferRules`)

`Choose(sources, destination, …)` decide cosa fa un trascinamento, come Explorer. La usa anche «Sposta in…», per scartare i posti dove non farebbe niente; gli appunti no: quello che è stato tagliato si sposta, quello che è stato copiato si copia ([operazioni-sui-file.md](operazioni-sui-file.md#gli-appunti)).

| Condizione | Risultato |
|---|---|
| la destinazione è dentro una delle sorgenti, o le sorgenti sono già tutte lì | niente (`None`) |
| `Alt`, oppure `Ctrl+Maiusc` | collegamento (`Link`) |
| `Ctrl` | copia (`Copy`) |
| `Maiusc` | spostamento (`Move`) |
| `preferLink`: gli elementi hanno già un loro posto | collegamento |
| nessuna delle precedenti | spostamento sullo stesso disco, copia tra dischi diversi |

- `HasItsOwnPlace(path, root)`: fuori dall'archivio, ed è un `.exe` o sta in rete (percorso `\\…` o disco di rete). È il principio 5 di PROGETTO.md: entra come collegamento.
- `IsSameOrInside(path, folder)` è il confronto di percorsi usato un po' dappertutto, anche dall'app.

## Annullare (`UndoHistory`)

- Una pila di al massimo 50 passi (`Capacity`), solo in memoria: chiudendo Quiver si perde.
- Ogni passo è un `record` che deriva da `UndoStep` e ricorda quello che serve per tornare indietro:

| Passo | Cosa ricorda |
|---|---|
| `MoveStep` | per ogni elemento da dove è partito e dove è arrivato (`MovedItem`), e i metadati delle cartelle uscite dall'archivio (`Forgotten`) |
| `CopyStep` | le copie fatte, anche di quello che arriva da altri programmi: annullare le manda nel Cestino |
| `LinkStep` | i collegamenti fatti: annullare li manda nel Cestino, e quello a cui puntano resta dov'è |
| `RenameStep` | il nome di prima e quello di dopo |
| `CreateStep` | la cartella creata (riquadro, argomento o cartella): si toglie solo se è ancora vuota |
| `RecycleStep` | gli elementi andati nel Cestino (`RecycledItem`), quanti sono stati eliminati per sempre (`NotInBin`) e i loro metadati |
| `TileOrderStep`, `TopicOrderStep` | il posto di prima di ogni riquadro o argomento |
| `TopicMoveStep` | un argomento trascinato dentro un altro: lo spostamento e il posto che aveva |
| `ArrangeStep` | l'ordinamento e l'ordine a mano di prima |
| `SizeStep` | la misura di prima |
| `TopicStep` | se la cartella era un argomento («Promuovi ad argomento», «Riporta a riquadro») |
| `ColorStep` | il colore di prima, e se era automatico |

Come i passi vengono registrati ed eseguiti al contrario è in [operazioni-sui-file.md](operazioni-sui-file.md#annullare).

## Il Cestino (`RecycleBin`)

Il Cestino di ogni disco tiene ogni elemento come due file: `$R…` è l'elemento, `$I…` dice dove stava e quando è stato eliminato.

- `IsAvailableFor(path)`: solo i dischi fissi hanno il Cestino. Sui dischi di rete e rimovibili Windows elimina per sempre, e quindi lì un annullamento non elimina niente.
- `FindDeleted(paths, sinceUtc)`: subito dopo un'eliminazione trova, per ogni percorso, il `$I…` più recente scritto da allora. Legge solo i file recenti, perché il Cestino può averne migliaia. I percorsi senza un `$I…` sono stati eliminati per sempre.
- `TryRestore(item)` rimette `$R…` al suo posto e cancella `$I…`, come «Ripristina». Non fa niente se al posto originale c'è già qualcosa.
- `ReadIndex` legge i `$I…` della versione 2 (Windows 10 e 11) e della versione 1 (da Vista a 8.1).
- Il costruttore riceve la funzione che dà, per un percorso, la cartella del Cestino dell'utente su quel disco: nell'app è `WindowsFolders.RecycleBinOf`, nei test un Cestino finto.

## La ricerca (`SearchIndex`, `SearchText`)

- `SearchIndex.Build(root, ct)` mette in memoria tutti i file e le cartelle dell'archivio, senza quelli nascosti, di sistema o con il punto davanti, e senza seguire junction e collegamenti simbolici. I file sciolti nella radice ci sono, con «Archivio» come posto.
- `ArchiveCount` e `ArchiveBytes` sono i numeri della barra di stato («55.781 elementi · 2,3 GB»).
- `Search(query, max)`: ogni parola deve comparire nel nome o nel percorso. `SearchText.Score` dà 100 punti se il nome comincia con la parola, 70 se ci comincia una sua parola, 40 se la parola è in mezzo al nome e 10 se è solo nel percorso, poi toglie un quarto della lunghezza del nome: a parità vincono i nomi corti. A parità di punteggio, l'ordine naturale.
- `SearchText.Normalize` toglie maiuscole e accenti: «perche» trova «Perché».

## Ordine degli elementi (`ItemArrangement`, `NaturalStringComparer`)

- `Arrange(items, sort, byHand)`: con un ordine a mano, gli elementi elencati prendono il loro posto e gli altri seguono nell'ordine della cartella. Con un ordinamento, prima le cartelle e poi i file, ognuno per la chiave scelta e a parità per nome. Il tipo è l'estensione.
- `NaturalStringComparer.Instance`: i numeri per valore, le lettere senza badare alle maiuscole, come Explorer.

## Le altre utilità

- `FileKinds.Of(item)`: il tipo di un file (`FileKind`) dalla sua estensione, per scegliere l'icona di Brace. `Other` vuol dire l'icona di Windows.
- `FolderSize.Of(paths, ct)`: quanti byte servono per copiare file e cartelle, nascosti compresi. Serve al controllo dello spazio prima di una copia.
- `CopyCheck.Differences(original, copy, progress, ct)`: confronta byte per byte la copia di una cartella con l'originale, quando un archivio si sposta su un altro disco.
- `WebShortcut.Parse(text)` riconosce un indirizzo scritto a mano, anche senza `https://`; `WebShortcut.Create(folder, uri)` scrive il `.url` con il nome del sito.
- `TopicColors`: la tavolozza automatica (sette colori di Brace; il grigio, l'ottavo, solo a mano), `LeastUsed` e `AssignMissing`, che dà un colore agli argomenti della radice che non ce l'hanno.
- `CanvasPlacement`: dove vanno le zone della tela ([tela.md](tela.md)).
- `ByteSize.Format`: le dimensioni come le scrive Explorer in italiano, con unità da 1024 e tre cifre («12,4 MB»).
