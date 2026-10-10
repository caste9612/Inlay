# Convenzioni

Le regole, scritte e non, che il codice di Inlay segue. Chi ci lavora le rispetta, perché il codice nuovo si legga come quello che c'è.

## Le lingue

- **Inglese**: codice, nomi, commenti, nomi dei test e, dalla 0.3.4, tutto quello che legge chi usa Inlay dentro il programma (testi dell'interfaccia, avvisi, errori). Fino alla 0.3.3 l'interfaccia era in italiano.
- **Italiano**: i documenti, i messaggi dei commit e le note di versione.
- I testi dell'interfaccia sono scritti direttamente nel XAML e nel codice: se servirà più di una lingua, andranno spostati in file di risorse (PROGETTO.md §8).

### Come sono scritti i commenti

Quasi ogni tipo e membro ha un `<summary>`. I commenti spiegano il perché, spesso dal punto di vista di chi usa Inlay, in frasi semplici:

```csharp
// Off the clipboard at once: a second Ctrl+V while the move runs would try to move the same files again.
FileClipboard.Clear();
```

```csharp
/// <summary>Files go straight to the Recycle Bin, as on the desktop; folders, with everything in them, after a confirmation.</summary>
public void AskRecycle(IReadOnlyList<string> paths)
```

Ricorrono i confronti con Explorer e con il desktop («as in Explorer», «as on the desktop»), che sono il modello di comportamento.

### Come sono scritti i testi dell'interfaccia

- Frasi brevi, parole di tutti i giorni, senza gergo: «Move 3 items to the Recycle Bin?», «The files are already there.».
- Le parole dell'utente, sempre le stesse: vault, space, tile, General, item, Board, Mosaic, e fold e unfold per le cartelle tra le righe di General ([il glossario](README.md#glossario)). I colori hanno i nomi delle pietre di un intarsio: Amber, Lapis, Amethyst, Rose, Jade, Citrine, Coral, Pearl.
- I nomi tra virgolette curve: “Documents”. I percorsi con «›»: “Lavoro › Clienti”. I puntini di sospensione come carattere unico: “Move to…”. Voci di menu e pulsanti con la sola iniziale maiuscola, come in Windows 11: “Copy path”, “Move to Recycle Bin”.
- I tasti con i nomi inglesi: `Enter`, `Del`, `Shift`, `Ctrl+Shift+N`.
- Un errore dice cosa è successo e cosa fare: «Couldn't rename “a.txt”: it may be open in another program. Try again with Ctrl+Z.».
- Numeri, dimensioni e date in inglese, qualunque sia la lingua di Windows: «18,481 items», «12.4 MB», «5 Oct». La cultura "en-US" è scritta dove servono (`ByteSize`, `DateLabel`, i conteggi di `MainViewModel` e `ZoneViewModel`).
- I documenti, in italiano, chiamano le parti di Inlay con le loro parole italiane (archivio, argomento, riquadro, bacheca, tela) e scrivono in grassetto le voci dell'interfaccia come le mostra Inlay (**Move to…**); i tasti tra apici inversi (`Ctrl+Z`).

## Il codice C#

- **.NET 10**, la versione del linguaggio predefinita, `Nullable` e `ImplicitUsings` attivi. Nei progetti WPF `System.IO` è aggiunto a mano agli using impliciti.
- **Costrutti moderni**: espressioni di collezione (`[]`, `[.. a, .. b]`), costruttori primari, `record` per i dati, pattern matching, `Lock`.
- **Visibilità stretta**: classi `sealed`, `internal` dove si può (i test le vedono con `InternalsVisibleTo`), classi `static` per le regole senza stato.
- **Chiamate a Windows** scritte a mano, in `NativeMethods` o vicino a chi le usa, di solito con `[LibraryImport]`. `[DllImport]` dove `[LibraryImport]` non arriva (strutture con stringhe, `StringBuilder`, interfacce COM) e, in `VirtualFiles`, anche per qualche funzione semplice ([windows.md](windows.md#la-shell-di-windows-interop)). CsWin32 resta un'idea per quando le chiamate cresceranno.
- **Gli errori**: si prendono solo quelli attesi (`catch (Exception e) when (e is IOException or UnauthorizedAccessException)`), si scrivono nel log e si dicono all'utente, in inglese. Inlay non deve mai chiudersi per un errore; quelli imprevisti finiscono nel gestore di `App`.
- **I file di Inlay** (metadati, impostazioni, recenti) si salvano scrivendo prima un `.tmp` e poi spostandolo sopra il file vero.
- **Fuori dal thread dell'interfaccia** quello che può durare: spostamenti, copie ed eliminazioni della shell, anche la copia di un archivio (`ShellThread`), le icone di Windows (`ShellIcons`), l'indice della ricerca, i conteggi e i confronti (`Task.Run`). I risultati tornano attraverso il dispatcher. Sul thread dell'interfaccia restano le cose brevi: la lettura delle cartelle a ogni ricarica, le rinomine, lo spostamento di un archivio sullo stesso disco.
- **Nomi che dicono l'intenzione**: `AskRecycle`, `ReloadUnlessEditing`, `KeepForRetry`, `CommitAfterInput`.
- **I test** hanno nomi che sono frasi: `Undo_LeavesAloneWhatChangedSince` ([test.md](test.md)).

## I principi da non rompere

Vengono da [PROGETTO.md §3](../PROGETTO.md#3-principi), e il codice li tiene in piedi così:

- **Il file system è la verità.** Argomenti e riquadri sono cartelle vere; quello che le cartelle non sanno dire va in `.inlay/archive.json`, con chiavi relative alla radice. Niente database, niente cache da tenere allineate: si rilegge il disco.
- **Mai perdere dati.**
  - Si elimina passando dal Cestino. Dove il Cestino non c'è (dischi di rete o rimovibili) la shell avvisa prima di eliminare per sempre, e Inlay dice cosa è successo; un annullamento, lì, non elimina niente. Inlay stesso elimina per sempre solo una cartella vuota creata da lui, quando se ne annulla la creazione.
  - La shell chiede cosa fare quando un nome è preso: niente sovrascritture silenziose.
  - Ogni operazione registra il suo passo per annullarla (`Remember`), e un annullamento rimette le cose solo in posti liberi.
  - I metadati seguono le cartelle che Inlay sposta e rinomina, e tornano con loro quando si annulla.
- **Come Explorer e come il desktop**: le regole per spostare, copiare o collegare, i tasti, i nomi «(2)» e «- Copia», l'ordine naturale, i collegamenti senza estensione, il tasto destro che agisce sulla selezione.
- **Ciò che ha già un suo posto resta lì**: programmi e cose in rete entrano come collegamenti.
- **Veloce**: la finestra si crea una volta sola e poi si mostra; quello che può durare non tiene fermo il thread dell'interfaccia (vedi sopra). Gli obiettivi sono in PROGETTO.md §8: finestra visibile in meno di 100 ms, ricerca su 50.000 file in meno di 50 ms, meno di 100 MB a riposo. Con la tela in vista, ogni apertura rilegge tutte le bacheche: è il primo punto da misurare se l'apertura rallenta (il tempo è nel log).
- **Pensata per GlazeWM**: nascondendosi, Inlay ridà il focus alla finestra di prima, e non lo prende a una finestra di un altro programma dopo un'operazione.

## Brace

- I file di `src/Inlay.App/Brace` e `src/Inlay.App/Fonts` vengono dal design system e non si modificano qui ([interfaccia.md](interfaccia.md#brace-e-il-tema-di-inlay)).
- Quello che Inlay aggiunge sta in `Themes/Inlay.xaml`, con il prefisso `Inlay.`; se manca qualcosa che serve anche ad altri software (un'icona, un controllo), il posto giusto è Brace.
- I colori si prendono dai token con `DynamicResource` (`Brace.SurfaceBase`, `Brace.Accent`…), gli stili da quelli di Brace (`Brace.Button.Primary`, `Brace.Panel`…).
- Le regole di Brace che il codice rispetta: un solo ambra (`accent`) per barra, icone monocromatiche, icone piene solo per la selezione della navigazione principale, durate e curve dei movimenti dai token, niente movimento se Windows ha le animazioni spente.

## I documenti

| Documento | Per chi | Cosa contiene |
|---|---|---|
| [README.md](../README.md) | chi usa Inlay | installazione, uso, riga di comando, test e diagnostica |
| [PROGETTO.md](../PROGETTO.md) | chi decide dove va Inlay | il problema, l'idea, i principi, le regole dell'archivio, l'interfaccia, le versioni, GlazeWM, l'architettura, i rischi, i prossimi passi, i piani |
| [novita/](../novita) | chi aggiorna Inlay | le note di ogni versione |
| [docs/](README.md) | chi lavora sul codice | com'è fatto il codice e come si lavora |

Si aggiornano nello stesso commit del codice che cambiano.

## Git

- Si lavora su `main`. I messaggi dei commit sono in italiano e dicono cosa cambia per chi usa Inlay; quelli che preparano un rilascio sono «Versione X.Y.Z» ([rilascio.md](rilascio.md)).
- Restano fuori dal repository (`.gitignore`): `bin`, `obj`, `artifacts`, `.orbit/` (l'indice dei simboli di un editor) e anche i file `*.lnk`, quindi nessun collegamento di prova può stare nel repository: i test e lo script dell'archivio di prova li creano al momento.
