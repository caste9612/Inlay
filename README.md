# Inlay

Una "scrivania" da richiamare con una scorciatoia, al posto del desktop di Windows. Fino alla 0.3.2 si chiamava **Quiver**.
Idea, regole dell'archivio e piano delle versioni sono in [PROGETTO.md](PROGETTO.md); com'è fatto il codice, per chi ci lavora, è in [docs/](docs/README.md).

## Installazione

Scarica **Inlay-win-Setup.exe** dalle [release](https://github.com/caste9612/Inlay/releases) e lancialo.
- Inlay si installa per il tuo utente, senza permessi di amministratore, in `%LOCALAPPDATA%\Inlay`, con il collegamento nel menu Start.
- Parte con Windows; si disattiva da **Settings**, nel menu dell'icona nella tray.
- Mette **Inlay** nel menu **Invia a** del tasto destro di Explorer: i file scelti vengono mandati nell'archivio. Anche questo si toglie da **Settings**.
- Serve il runtime .NET 10 Desktop: se manca, l'installer lo scarica.
- L'installer non è firmato, quindi Windows può avvisare: **Ulteriori informazioni**, poi **Esegui comunque**.
- Si disinstalla da **Impostazioni > App**; l'archivio, con i tuoi file, resta dov'è.
- **Aggiornamenti**: Inlay si aggiorna da solo dalle release di GitHub. Un minuto dopo l'avvio, e poi ogni sei ore, controlla se c'è una versione nuova e la scarica in sottofondo. Quando è pronta lo dicono un avviso della tray e la barra di stato, «Inlay X is ready»: un clic riavvia Inlay aggiornato, dopo le operazioni sui file in corso; altrimenti si aggiorna quando esci. La versione in uso, e se è l'ultima, si vedono in **Settings** › **About**: lì **Check for updates** controlla subito, e **Restart to update** compare quando una versione nuova è pronta. La stessa voce del menu dell'icona apre quella pagina e controlla. Le versioni segnate come da provare (pre-release) non arrivano da sole.
- **Se avevi Quiver**: esci da Quiver (tasto destro sull'icona nella tray, **Esci**) e disinstallalo da **Impostazioni > App**, poi installa Inlay. Disinstallare Quiver toglie solo il programma, il suo avvio con Windows e **Invia a Quiver**: l'archivio e le impostazioni restano. Prima Quiver, perché l'installer avvia subito Inlay, e insieme si contenderebbero la scorciatoia e la cartella nascosta dell'archivio. Al primo avvio Inlay prende le impostazioni di Quiver (archivi, scorciatoia, finestra) e i posti recenti; ogni archivio, la prima volta che Inlay lo apre, passa la sua cartella nascosta da `.quiver` a `.inlay`, con tutto quello che contiene. Se in GlazeWM hai regole o tasti con `Quiver.exe`, scrivi `Inlay.exe`. Questo passaggio si fa a mano una volta sola: Quiver non sapeva aggiornarsi, Inlay sì.

Al primo avvio Inlay dà il benvenuto e chiede l'archivio: creane uno nuovo o usa una cartella che hai già.

Per preparare l'installer dal codice: `.\scripts\New-Installer.ps1`; con `-Publish` lo pubblica anche come release su GitHub, con le note di `novita\<versione>.md`, e con `-Prerelease` la segna come da provare. La versione è quella di `src\Inlay.App\Inlay.App.csproj`. Prima di pubblicare lo script controlla che tutto sia nel commit e che il commit sia già su GitHub, e lega il tag della release proprio a quel commit.

## Sviluppo

Serve Windows 11 con l'SDK di .NET 10.

```powershell
dotnet build Inlay.slnx
dotnet run --project src/Inlay.App
```

Per provarlo senza i tuoi file:

```powershell
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Inlay.App -- --archive D:\ArchivioDemo
```

- `--archive` vale solo per quell'avvio: non cambia l'archivio in uso nelle impostazioni.
- Se l'Inlay installato è aperto, prima esci da quello (**Quit** nel menu della tray): un secondo Inlay passa il comando a quello aperto e si chiude.

## Uso

`Alt+E` porta Inlay davanti; se è già la finestra in uso, lo rimette nell'area di notifica (la scorciatoia si cambia in **Settings**). Dentro, si usa come il desktop.

L'interfaccia è in inglese, con parole sue: l'archivio è il **vault**, gli argomenti sono gli **spaces**, i riquadri i **tiles**, il riquadro con i file sciolti di un argomento è **General**; le due viste sono **Board**, la bacheca, e **Mosaic**, il mosaico, che fino alla 0.3.3 si chiamava tela. Qui le parti di Inlay hanno i loro nomi italiani, le voci da cliccare quelli inglesi.

- **La finestra**: è una finestra normale, con in alto la barra del titolo di Inlay, disegnata come quella di Brace. Si sposta trascinando la barra, si ridimensiona dai bordi e un doppio clic sulla barra la ingrandisce. Il pin, accanto ai pulsanti della finestra, la tiene sopra le altre: comodo per trascinarci dentro i file da Explorer. Lo stesso fa **Always on top** nel menu della tray. La X la rimette nell'area di notifica, come `Esc` e `Alt+E`; per uscire da Inlay c'è **Quit** nel menu della tray. Posizione, dimensione e pin restano per la volta dopo.
- **La barra degli argomenti** (**Spaces**): il pulsante subito dopo «Inlay», nella barra del titolo, o `Ctrl+B` la chiudono e la riaprono. Chiusa non occupa spazio, così bacheca e mosaico prendono tutta la larghezza; Inlay ricorda come l'hai lasciata.
- **Selezionare**: un clic seleziona, `Ctrl`+clic e `Shift`+clic aggiungono altri elementi. Con `Ctrl` si sceglie anche da riquadri diversi. Un clic su uno spazio vuoto deseleziona. Trascinando sul vuoto della bacheca si disegna un rettangolo che seleziona quello che tocca, anche in più riquadri; con `Ctrl` si aggiunge alla selezione.
- **Aprire**: doppio clic o `Enter`. `Ctrl+Enter` mostra l'elemento in Explorer.
- **Rinominare**: `F2`, oppure un secondo clic, lento, su un elemento già selezionato. Il nome si modifica sul posto e, come in Explorer, i collegamenti non mostrano `.lnk` o `.url`. `Enter` conferma, `Esc` annulla e un clic altrove conferma.
- **Eliminare**: `Del` sposta nel Cestino. Per le cartelle Inlay chiede prima conferma.
- **Move to…**: dal tasto destro su uno o più elementi, anche tra i risultati della ricerca. Si apre l'elenco dei riquadri e degli argomenti, con quelli usati di recente in cima: basta scrivere una parte del nome e premere `Enter`, o fare doppio clic. Un argomento riceve i file tra i suoi file sciolti. I file vengono spostati anche da un altro disco; i programmi e quello che sta in rete entrano come collegamenti.
- **Invia a Inlay**: in Explorer, tasto destro su uno o più file, **Invia a** › **Inlay**: il pannello si apre chiedendo dove metterli, come con **Move to…**.
- **Annullare**: `Ctrl+Z` annulla l'ultima operazione fatta in Inlay, anche più volte di seguito. Vale per spostamenti, copie, collegamenti, rinomine, nuove cartelle, riquadri e argomenti, ordine dei riquadri e Cestino: quello che ci hai mandato torna al suo posto. Si trova anche nel menu del tasto destro sulla bacheca. Dopo uno spostamento, una copia, un collegamento o un'eliminazione, in basso a destra compare un avviso con **Undo** e, quando c'è un posto dove andare, **Show**, che porta sulla destinazione con gli elementi selezionati. **Undo** resta finché quell'operazione è l'ultima fatta.
- **Avvisi**: in basso a destra, al massimo tre, il più recente in fondo. Gli esiti e le notizie si chiudono da soli dopo sei secondi, ma non mentre ci tieni sopra il puntatore; i problemi restano finché non li chiudi con la X, e finiscono anche nel log.
  - Se nel frattempo un elemento è stato spostato o rinominato, resta com'è.
  - Se un file è aperto in un altro programma, `Ctrl+Z` si può ripetere.
  - Su un disco senza Cestino (di rete o rimovibile) l'annullamento non elimina niente: lì eliminare sarebbe definitivo.
- **Copiare e spostare con gli appunti**: `Ctrl+C`, `Ctrl+X` e `Ctrl+V` funzionano anche da e verso Explorer. Si incolla nel riquadro o nell'argomento su cui hai cliccato, altrimenti nella bacheca; anche appena aperto il pannello, con la ricerca vuota. **Paste shortcut**, dal tasto destro, crea collegamenti ai file copiati, che restano dove sono.
- **Tasto destro**: **Open with…**, **Move to…**, **Copy path**, **Properties** (anche con `Alt+Enter`) e le altre azioni.
- **Trascinare**:
  - un file su un riquadro o su un argomento lo sposta lì. Come in Explorer, tra dischi diversi viene copiato; con `Ctrl` copi, con `Shift` sposti, con `Alt` (o `Ctrl+Shift`) crei un collegamento;
  - una cartella lasciata su un argomento diventa una riga del suo **General**, invece di un riquadro con tutto il suo contenuto; vale anche incollandola o con **Move to…**. Lasciata su un riquadro, è un elemento di quel riquadro, come sempre;
  - se tieni fermo quello che trascini sopra un argomento, dopo un attimo la bacheca passa lì e lo lasci nel riquadro giusto, come con le cartelle di Explorer;
  - quello che ha già un suo posto entra come collegamento, come chiede il progetto: i programmi (`.exe`, come fa Explorer) e le cartelle e i file in rete;
  - gli allegati e i messaggi di Outlook, e i link e le immagini dal browser, diventano file nel riquadro;
  - un riquadro, preso per il titolo (al passaggio del mouse compare la maniglia a puntini), cambia posto nella bacheca, e l'ordine resta: una riga mostra dove andrà. Lasciato su un argomento, ci entra dentro e resta un riquadro;
  - le righe di un riquadro, trascinate dentro il riquadro stesso, prendono l'ordine che vuoi; i file che arrivano dopo si mettono in fondo;
  - un argomento, nella barra a sinistra, si sposta sopra o sotto un altro; lasciato al centro di un altro argomento ci entra dentro, con la sua cartella;
  - mentre trascini, un'etichetta accanto al puntatore dice cosa hai in mano: il nome, o quanti elementi;
  - file e cartelle si portano anche fuori da Inlay, in Explorer, in una mail o in un'altra app;
  - per prendere file da Explorer, tieni le due finestre affiancate, oppure fissa Inlay con il pin: resta sopra Explorer mentre trascini. Funziona anche cominciare a trascinare e premere `Alt+E` senza lasciare il tasto del mouse.
- **File nella radice**: i file lasciati direttamente nella cartella dell'archivio, fuori da ogni argomento, non compaiono sulla bacheca né sul mosaico. La barra di stato dice quanti sono («3 loose files in the vault»), e un clic apre la radice in Explorer; la ricerca li trova. Una cartella `_Da smistare`, negli archivi fatti con le versioni che avevano «Da smistare», è ora un argomento come gli altri.
- **Il Desktop** non compare in Inlay: i suoi file si mandano nell'archivio trascinandoli da Explorer o con **Invia a Inlay**.
- **Cercare**: basta scrivere, in qualunque punto del pannello. Inlay cerca in tutto l'archivio, nei nomi di file e cartelle e nei nomi delle cartelle che li contengono, senza badare a maiuscole e accenti: ogni parola scritta deve esserci, e vengono prima i nomi che cominciano così. Con le frecce scegli un risultato, `Enter` lo apre ed `Esc` torna alla bacheca.
  - Sopra i risultati ci sono i **filtri**: dove cercare (**Everywhere**, o un argomento) e i tipi (**Folders**, **Documents**, **PDF**, **Spreadsheets**, **Images**, **Shortcuts**…). Scrivono nella casella `in:/Lavoro` e `type:pdf`, e si possono anche scrivere a mano: `type:photos`, `type:dwg` per un'estensione, `in:Rossi` per le cartelle con quel nome a qualunque profondità (con la `/` davanti, solo dalla radice), `in:"Foto vacanze"` tra virgolette se ci sono spazi. Più tipi, o più posti, valgono uno o l'altro.
  - Con i soli filtri, senza parole, l'elenco è dei più recenti: `type:pdf in:Rossi` dà gli ultimi PDF di Rossi. Il pulsante a imbuto nella casella offre gli stessi filtri prima di scrivere.
- **Mosaico**: con l'interruttore **Board | Mosaic** in alto a destra, tutto l'archivio insieme, con una zona per argomento; i sotto-argomenti stanno dentro la zona del loro argomento. Inlay ricorda l'ultima vista usata.
  - La rotella zooma attorno al puntatore, dal 10 % al 200 %; sopra un riquadro che scorre lo fa scorrere, e con `Ctrl` zooma sempre. Anche `Ctrl++` e `Ctrl+-`, o i pulsanti accanto alla percentuale.
  - Trascinando sul vuoto, o con la rotella premuta dovunque, si sposta la vista. Il doppio clic sul vuoto, **Show all** o `Ctrl+0` mettono in vista tutto l'archivio.
  - Da lontano le zone mostrano il nome e i riquadri sono blocchi, con la striscia del loro colore se ne hanno uno; a metà strada i riquadri hanno titolo, numero di elementi e i nomi dei primi, quanti ne entrano; da vicino, dal 70 % circa, sono i riquadri della bacheca, con gli stessi gesti. Le righe dei riquadri arrivano appena la vista si ferma, così spostarsi e zoomare restano fluidi anche con archivi grandi.
  - Il tasto destro sul nome di una zona apre il menu del suo argomento, come nella barra laterale: **New tile**, **New space inside**, **Rename**, **Color**…
  - Un clic su un argomento nella barra laterale vola alla sua zona; il doppio clic sul nome di una zona la inquadra, quello su un riquadro visto da lontano ci vola sopra. Un clic su un risultato della ricerca vola al suo riquadro, con l'elemento selezionato.
- **Creare**:
  - un argomento: il pulsante + accanto a **Spaces**; dentro un altro argomento, tasto destro su quello, o sul nome della sua zona (**New space inside**). Il nome si scrive nella barra degli argomenti, che si apre se era chiusa, e il mosaico vola sulla zona nuova;
  - un riquadro: **New tile**, in alto a destra sulla bacheca e sul mosaico, `Ctrl+Shift+N`, o tasto destro sulla bacheca o sul nome di una zona. Va nell'argomento scelto nella barra a sinistra, che il suggerimento del pulsante nomina («New tile in “Lavoro”»); sul mosaico la vista vola sul riquadro nuovo, abbastanza vicino da scriverne il nome;
  - una cartella in un riquadro: **New folder**, dal tasto destro sul riquadro o dal pulsante … accanto al titolo; in **General** è una delle sue righe;
  - un collegamento: **New shortcut…** dal tasto destro sul riquadro o sulla bacheca. Si scrive l'indirizzo di un sito, anche solo `rossi.it`, oppure il percorso di un programma, di un file o di una cartella.

  Come «Nuova cartella» sul desktop, il nuovo elemento nasce con il nome pronto da scrivere.
- **Riquadri e argomenti**: dal menu di un riquadro puoi scegliere **Make it a space**; da quello di un argomento annidato, **Back to tile**.
  Una cartella tra le righe di **General** diventa un riquadro con **Unfold as tile**, dal tasto destro; un riquadro torna una riga con **Fold into General**, dal suo menu. Le cartelle restano dove sono: cambia solo come le mostra Inlay, e `Ctrl+Z` torna indietro.
- **Color**: dal menu … di un riquadro, uno degli otto colori di Brace, con i nomi delle pietre di un intarsio (Amber, Lapis, Amethyst, Rose, Jade, Citrine, Coral, Pearl), o **None**. Compare come una striscia accanto al titolo, resta nei metadati dell'archivio e `Ctrl+Z` lo toglie.
- **Colore degli argomenti**: ogni argomento della radice, la prima volta che Inlay lo vede, prende il colore di Brace meno usato dagli altri e lo tiene nell'archivio: spostarlo o rinominarlo non lo cambia. I sotto-argomenti usano quello del loro argomento. Il colore si vede nell'icona della barra laterale e, sul mosaico, nel bordo e nel nome della zona, che da lontano è anche leggermente tinta. Dal tasto destro su un argomento, **Color** › **Auto**, **None** o uno degli otto; `Ctrl+Z` torna a quello di prima.
- **Misura dei riquadri**: trascina l'angolo in basso a destra di un riquadro. In larghezza scatta a una, due o tre colonne, in altezza decide quante righe si vedono prima di scorrere. Il doppio clic sull'angolo, o **Auto size** nel menu …, torna alla misura che segue gli elementi.
- **Sort by**: dal menu … di un riquadro, nome, data di modifica, tipo o dimensione (**Name**, **Date modified**, **Type**, **Size**), crescente o decrescente; le cartelle vengono prima, come in Explorer. Sostituisce l'ordine dato a mano, e viceversa. Anche questo resta nei metadati, e `Ctrl+Z` lo annulla.
- **Copie lunghe**: mentre Windows sposta o copia, Inlay resta usabile e la barra di stato dice cosa sta facendo. Più operazioni su file diversi vanno avanti insieme, ognuna con la sua finestra di avanzamento; quelle sugli stessi file, o nella stessa cartella, aspettano il loro turno. La barra di stato dice quante sono in corso e quante in attesa, e passandoci sopra le elenca. `Ctrl+Z` aspetta che finiscano tutte e annulla l'ultima che hai chiesto, anche se una più vecchia finisce dopo; **Undo** su un avviso annulla proprio quella. **Quit** aspetta che finiscano tutte.
- **Spazio su disco**: prima di copiare nell'archivio, o di spostarci file da un altro disco, Inlay conta quanto occupano; se non ci stanno lo dice e non comincia, invece di lasciare la copia a metà. Lo stesso vale per **Move…** di un archivio su un altro disco.
- **Barra di stato**: a sinistra, con l'icona dell'archivio, quanti elementi contiene e quanto occupano (un clic lo apre in Explorer) e, se ce ne sono, i file sciolti nella radice; oppure l'operazione in corso; a destra, come in Explorer, quanti elementi sono selezionati e quanto pesano, lo spazio libero sul disco dell'archivio, in giallo quando resta meno del 10 % o meno di 5 GB, e **?**, con i tasti da sapere.
- **Vaults**, gli archivi, come i vault di Obsidian: si aprono dal badge **Vault** in alto a destra, accanto al nome dell'archivio in uso, o da **Vaults…** nel menu della tray.
  - Ogni archivio è una cartella normale; la cartella nascosta `.inlay` lo segna come archivio e ne tiene la disposizione.
  - Puoi crearne uno nuovo, usare come archivio una cartella che hai già, passare dall'uno all'altro, rinominarli e toglierli dall'elenco. Togliere dall'elenco non tocca i file.
  - Sotto il nome di ogni archivio, quanto occupa, tutto compreso, e lo spazio libero sul suo disco. Lo conta in sottofondo, aprendo la pagina.
  - **Move…** porta l'archivio in un'altra cartella. Sullo stesso disco è un attimo. Su un altro disco Inlay lo copia, confronta la copia con l'originale byte per byte, passa alla copia solo se sono identiche e poi chiede se mandare la cartella vecchia nel Cestino. Mentre copia, Inlay resta usabile; la finestra **Settings** si chiude a copia finita, e **Quit** aspetta che finisca.
  - Non possono fare da archivio un disco intero, il Desktop, la cartella del tuo utente o quelle di Windows e dei programmi. Un archivio non può stare dentro un altro.
- **Settings**: dal menu della tray. Le pagine sono **Vaults**, **Hotkey**, **Startup & Explorer** e **About**, e ogni cambiamento vale subito.
  - **Hotkey**: clicca nel campo e premi la combinazione, con Ctrl, Alt o Win e un tasto. Inlay la prova all'istante: se un altro programma la tiene, resta quella di prima e lo dice.
  - **Startup & Explorer**: gli interruttori per **Start Inlay with Windows** e per **“Inlay” in the Send to menu of Explorer**.
  - **About**: la versione di Inlay e gli aggiornamenti: se è l'ultima e quando l'ha controllato, oppure quale versione nuova è pronta; **Check for updates**, **Restart to update** e il link alle novità di ogni versione, su GitHub.
- **Tray**: dall'icona puoi aprire Inlay (**Open Inlay**) o l'archivio in Explorer, tenere Inlay sempre in primo piano (**Always on top**), aprire gli archivi (**Vaults…**) e le impostazioni (**Settings…**), cercare un aggiornamento (**Check for updates**, che apre **About**) o uscire (**Quit**). Passandoci sopra, l'icona dice la versione e la scorciatoia: «Inlay 0.3.5 (Alt+E)».

## Aspetto

L'interfaccia usa Brace, il design system di riferimento: colori, caratteri (Inter e Cascadia Mono), icone Fluent e componenti. I file sono copiati in `src/Inlay.App/Brace` e `src/Inlay.App/Fonts`; da dove vengono e come si aggiornano è scritto in `src/Inlay.App/Brace/PROVENIENZA.md`.

Cartelle e file comuni hanno l'icona di Brace del loro tipo: PDF, Word, fogli di calcolo, presentazioni, immagini, video, audio, archivi compressi, codice, posta, pagine web e modelli 3D. Come vuole il design system sono monocromatiche: il tipo si riconosce dal disegno. Lo stesso vale per i file che nessun programma del PC apre. I programmi, i collegamenti e i file dei programmi installati tengono l'icona di Windows, che per loro fa da logo.

L'icona di Inlay è una bacheca: tre riquadri, uno dei quali ambra, come un riquadro selezionato. Si rigenera con `python scripts/genera-icona.py`.

## Riga di comando

- `Inlay.exe --toggle | --show | --hide | --exit` passa il comando all'istanza già aperta, ad esempio da una scorciatoia di GlazeWM.
- `Inlay.exe --sort <file> [<file>…]` apre il pannello chiedendo dove mandare quei file: è quello che fa **Invia a Inlay**. Se Inlay è già aperto, i file passano all'istanza in esecuzione.
- `Inlay.exe --archive <cartella> --render-preview <file.png> [--topic Lavoro/Clienti] [--query testo] [--prompt | --prompt-link | --prompt-move | --notice]` disegna il pannello in un'immagine senza aprire finestre. Serve per i controlli automatici:
  - `--prompt` aggiunge una richiesta di conferma, `--prompt-link` la domanda di **New shortcut…** con un errore, `--prompt-move` l'elenco di **Move to…**, `--notice` gli avvisi di uno spostamento, con **Undo** e **Show**, e di un errore;
  - `--settings` disegna la finestra **Settings**, con `--section archives|hotkey|windows|about` la pagina, e con `--welcome` com'è al primo avvio;
  - `--canvas` disegna il mosaico, tutto in vista; `--zoom 0.5` la disegna al 50 %, attorno al centro o, con `--topic`, attorno alla zona di quell'argomento.
- `Inlay.exe --archive <cartella> --benchmark-canvas <file.txt>` misura quanto è fluido il mosaico su questo PC. Apre il mosaico dell'archivio in una finestra grande come lo schermo, senza prendere il focus, la muove per otto secondi sempre sullo stesso percorso (avvicinamento, spostamento da vicino, allontanamento, zoom avanti e indietro) e scrive nel file il tempo di apertura, i fotogrammi al secondo e quelli lenti; per ognuno dei più lenti, oltre 100 ms, cosa è successo prima (un cambio di livello, righe create, raccolte della memoria). L'archivio viene solo letto, e funziona anche con Inlay già aperto.

## Test e diagnostica

- `dotnet test` esegue tre gruppi di test:
  - le regole dell'archivio, la ricerca, i metadati, i file sciolti nella radice, i tipi di file, il ripristino dal Cestino (su un Cestino finto, costruito dal test) e il confronto byte per byte di una copia;
  - le operazioni sui file: spostare, copiare, rinominare, i collegamenti e i file trascinati da Outlook e dal browser. Il Cestino vero è escluso, per non riempirlo;
  - il pannello: rinomina sul posto, selezione dopo spostamenti e ricaricamenti, nuove cartelle e nuovi riquadri, ordine dei riquadri e degli argomenti, misura dei riquadri, ordinamento e ordine a mano degli elementi, `Ctrl+Z`, **Move to…** con le destinazioni recenti, gli avvisi con **Undo** e **Show**, la barra di stato con la selezione e lo spazio libero, il controllo dello spazio prima di una copia, i colori degli argomenti dati una volta e tenuti;
  - il mosaico: le zone con i sotto-argomenti dentro, la selezione e gli spostamenti tra zone, la camera (zoom attorno a un punto, voli, livelli), la disposizione delle zone senza sovrapposizioni, e le liste dei riquadri che restano senza righe finché non servono, alte come con le righe;
  - la coda delle operazioni sui file: un thread STA a parte, un'operazione dopo l'altra;
  - gli archivi: creare, aprire una cartella, rinominare e spostare l'archivio in uso, anche su un altro disco;
  - le impostazioni: la scorciatoia provata subito e tenuta solo se libera, gli interruttori salvati e applicati, su una Windows finta;
  - il collegamento di **Invia a**, fatto in una cartella del test, e il passaggio dei file tra due istanze, senza toccare la pipe vera.
- Se il file delle impostazioni, `%APPDATA%\Inlay\settings.json`, si rovina, Inlay riparte da quelle predefinite e tiene il file com'era in `settings.json.corrupt`, con l'elenco degli archivi.
- Il log si trova in `%LOCALAPPDATA%\Inlay\inlay.log`. Registra il tempo di apertura del pannello, perché si chiude e quale finestra gli toglie il focus.
