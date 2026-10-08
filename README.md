# Quiver

Una "scrivania" da richiamare con una scorciatoia, al posto del desktop di Windows.
Idea, regole dell'archivio e piano delle versioni sono in [PROGETTO.md](PROGETTO.md).

## Installazione

Scarica **Quiver-win-Setup.exe** dalle [release](https://github.com/caste9612/Quiver/releases) e lancialo.
- Quiver si installa per il tuo utente, senza permessi di amministratore, in `%LOCALAPPDATA%\Quiver`, con il collegamento nel menu Start.
- Parte con Windows; si disattiva da **Impostazioni**, nel menu dell'icona nella tray.
- Mette **Quiver** nel menu **Invia a** del tasto destro di Explorer: i file scelti vengono mandati nell'archivio. Anche questo si toglie da Impostazioni.
- Serve il runtime .NET 10 Desktop: se manca, l'installer lo scarica.
- L'installer non è firmato, quindi Windows può avvisare: **Ulteriori informazioni**, poi **Esegui comunque**.
- Si disinstalla da **Impostazioni > App**; l'archivio, con i tuoi file, resta dov'è.

Al primo avvio Quiver dà il benvenuto e chiede l'archivio: creane uno nuovo o usa una cartella che hai già.

Per preparare l'installer dal codice: `.\scripts\New-Installer.ps1`; con `-Publish` lo pubblica anche come release su GitHub, con le note di `novita\<versione>.md`, e con `-Prerelease` la segna come da provare. La versione è quella di `src\Quiver.App\Quiver.App.csproj`. Prima di pubblicare lo script controlla che tutto sia nel commit e che il commit sia già su GitHub, e lega il tag della release proprio a quel commit.

## Sviluppo

Serve Windows 11 con l'SDK di .NET 10.

```powershell
dotnet build Quiver.slnx
dotnet run --project src/Quiver.App
```

Per provarlo senza i tuoi file:

```powershell
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo --desktop D:\DesktopDiProva
```

- `--archive` vale solo per quell'avvio: non cambia l'archivio in uso nelle impostazioni.
- `--desktop <cartella>` fa mostrare a **Da smistare** quella cartella al posto del Desktop vero. Così trascinando non si spostano i propri file nell'archivio di prova.

## Uso

`Alt+E` porta Quiver davanti; se è già la finestra in uso, lo rimette nell'area di notifica (la scorciatoia si cambia in **Impostazioni**). Dentro, si usa come il desktop.

- **La finestra**: è una finestra normale, con in alto la barra del titolo di Quiver, disegnata come quella di Brace. Si sposta trascinando la barra, si ridimensiona dai bordi e un doppio clic sulla barra la ingrandisce. Il pin, accanto ai pulsanti della finestra, la tiene sopra le altre: comodo per trascinarci dentro i file da Explorer. Lo stesso fa **Sempre in primo piano** nel menu della tray. La X la rimette nell'area di notifica, come `Esc` e `Alt+E`; per uscire da Quiver c'è **Esci** nel menu della tray. Posizione, dimensione e pin restano per la volta dopo.
- **Selezionare**: un clic seleziona, `Ctrl`+clic e `Maiusc`+clic aggiungono altri elementi. Con `Ctrl` si sceglie anche da riquadri diversi. Un clic su uno spazio vuoto deseleziona. Trascinando sul vuoto della bacheca si disegna un rettangolo che seleziona quello che tocca, anche in più riquadri; con `Ctrl` si aggiunge alla selezione.
- **Aprire**: doppio clic o `Invio`. `Ctrl+Invio` mostra l'elemento in Explorer.
- **Rinominare**: `F2`, oppure un secondo clic, lento, su un elemento già selezionato. Il nome si modifica sul posto e, come in Explorer, i collegamenti non mostrano `.lnk` o `.url`. `Invio` conferma, `Esc` annulla e un clic altrove conferma.
- **Eliminare**: `Canc` sposta nel Cestino. Per le cartelle Quiver chiede prima conferma.
- **Sposta in…**: dal tasto destro su uno o più elementi, anche in **Da smistare** e tra i risultati della ricerca. Si apre l'elenco dei riquadri e degli argomenti, con quelli usati di recente in cima: basta scrivere una parte del nome e premere `Invio`, o fare doppio clic. Un argomento riceve i file tra i suoi file sciolti. I file vengono spostati anche da un altro disco; i programmi e quello che sta in rete entrano come collegamenti.
- **Invia a Quiver**: in Explorer, tasto destro su uno o più file, **Invia a** › **Quiver**: il pannello si apre chiedendo dove metterli, come con **Sposta in…**.
- **Annullare**: `Ctrl+Z` annulla l'ultima operazione fatta in Quiver, anche più volte di seguito. Vale per spostamenti, copie, collegamenti, rinomine, nuove cartelle, riquadri e argomenti, ordine dei riquadri e Cestino: quello che ci hai mandato torna al suo posto. Si trova anche nel menu del tasto destro sulla bacheca. Dopo uno spostamento, una copia, un collegamento o un'eliminazione, accanto al messaggio nella barra di stato compaiono **Annulla** e, quando c'è un posto dove andare, **Mostra**, che porta sulla destinazione con gli elementi selezionati.
  - Se nel frattempo un elemento è stato spostato o rinominato, resta com'è.
  - Se un file è aperto in un altro programma, `Ctrl+Z` si può ripetere.
  - Su un disco senza Cestino (di rete o rimovibile) l'annullamento non elimina niente: lì eliminare sarebbe definitivo.
- **Copiare e spostare con gli appunti**: `Ctrl+C`, `Ctrl+X` e `Ctrl+V` funzionano anche da e verso Explorer. Si incolla nel riquadro o nell'argomento su cui hai cliccato, altrimenti nella bacheca; anche appena aperto il pannello, con la ricerca vuota. **Incolla collegamento**, dal tasto destro, crea collegamenti ai file copiati, che restano dove sono.
- **Tasto destro**: Apri con…, Sposta in…, Copia percorso, Proprietà (anche con `Alt+Invio`) e le altre azioni.
- **Trascinare**:
  - un file su un riquadro, su un argomento o su **Da smistare** lo sposta lì. Come in Explorer, tra dischi diversi viene copiato; con `Ctrl` copi, con `Maiusc` sposti, con `Alt` (o `Ctrl+Maiusc`) crei un collegamento;
  - se tieni fermo quello che trascini sopra un argomento, o su **Da smistare**, dopo un attimo la bacheca passa lì e lo lasci nel riquadro giusto, come con le cartelle di Explorer;
  - quello che ha già un suo posto entra come collegamento, come chiede il progetto: i programmi (`.exe`, come fa Explorer) e le cartelle e i file in rete;
  - quello che arriva da **Da smistare** viene spostato anche su un altro disco: smistarlo è proprio lo scopo;
  - gli allegati e i messaggi di Outlook, e i link e le immagini dal browser, diventano file nel riquadro;
  - un riquadro, preso per il titolo, cambia posto nella bacheca, e l'ordine resta: una riga mostra dove andrà. Lasciato su un argomento, ci entra dentro;
  - mentre trascini, un'etichetta accanto al puntatore dice cosa hai in mano: il nome, o quanti elementi;
  - file e cartelle si portano anche fuori da Quiver, in Explorer, in una mail o in un'altra app;
  - per prendere file da Explorer, tieni le due finestre affiancate, oppure fissa Quiver con il pin: resta sopra Explorer mentre trascini. Funziona anche cominciare a trascinare e premere `Alt+E` senza lasciare il tasto del mouse.
- **Da smistare**: raccoglie in riquadri separati i file lasciati nella radice dell'archivio o nella cartella `_Da smistare` e quelli del Desktop, compreso quello comune a tutti gli utenti dove gli installer mettono i collegamenti. In **Impostazioni › Da smistare** si può togliere il Desktop o aggiungere Download. La ricerca trova anche questi file.
- **Cercare**: basta scrivere. Con le frecce scegli un risultato, `Invio` lo apre ed `Esc` torna alla bacheca.
- **Creare**:
  - un argomento: il pulsante + accanto ad **Argomenti**; dentro un altro argomento, tasto destro su quello;
  - un riquadro: **Nuovo riquadro**, `Ctrl+Maiusc+N` o tasto destro sulla bacheca;
  - una cartella in un riquadro: tasto destro sul riquadro, o il pulsante … accanto al titolo;
  - un collegamento: **Nuovo collegamento…** dal tasto destro sul riquadro o sulla bacheca. Si scrive l'indirizzo di un sito, anche solo `rossi.it`, oppure il percorso di un programma, di un file o di una cartella.

  Come «Nuova cartella» sul desktop, il nuovo elemento nasce con il nome pronto da scrivere.
- **Riquadri e argomenti**: dal menu di un riquadro puoi scegliere **Promuovi ad argomento**; da quello di un argomento annidato, **Riporta a riquadro**.
- **Colore**: dal menu … di un riquadro, uno degli otto colori di Brace, o nessuno. Compare come una striscia accanto al titolo, resta nei metadati dell'archivio e `Ctrl+Z` lo toglie.
- **Archivi**, come i vault di Obsidian: si aprono dal nome dell'archivio in alto nel pannello, o da **Archivi…** nel menu della tray.
  - Ogni archivio è una cartella normale; la cartella nascosta `.quiver` lo segna come archivio e ne tiene la disposizione.
  - Puoi crearne uno nuovo, usare come archivio una cartella che hai già, passare dall'uno all'altro, rinominarli e toglierli dall'elenco. Togliere dall'elenco non tocca i file.
  - **Sposta…** porta l'archivio in un'altra cartella. Sullo stesso disco è un attimo. Su un altro disco Quiver lo copia, confronta la copia con l'originale byte per byte, passa alla copia solo se sono identiche e poi chiede se mandare la cartella vecchia nel Cestino.
  - Non possono fare da archivio un disco intero, il Desktop, la cartella del tuo utente o quelle di Windows e dei programmi. Un archivio non può stare dentro un altro.
- **Impostazioni**: dal menu della tray. Le pagine sono Archivi, Scorciatoia, Da smistare e Avvio ed Explorer, e ogni cambiamento vale subito.
  - **Scorciatoia**: clicca nel campo e premi la combinazione, con Ctrl, Alt o Win e un tasto. Quiver la prova all'istante: se un altro programma la tiene, resta quella di prima e lo dice.
  - **Avvio ed Explorer**: gli interruttori per **Avvia Quiver con Windows** e per **Quiver nel menu Invia a**.
- **Tray**: dall'icona puoi aprire Quiver o l'archivio in Explorer, tenere Quiver sempre in primo piano, aprire gli archivi e le impostazioni, o uscire.

## Aspetto

L'interfaccia usa Brace, il design system di riferimento: colori, caratteri (Inter e Cascadia Mono), icone Fluent e componenti. I file sono copiati in `src/Quiver.App/Brace` e `src/Quiver.App/Fonts`; da dove vengono e come si aggiornano è scritto in `src/Quiver.App/Brace/PROVENIENZA.md`.

Cartelle e file comuni hanno l'icona di Brace del loro tipo: PDF, Word, fogli di calcolo, presentazioni, immagini, video, audio, archivi compressi, codice, posta, pagine web e modelli 3D. Come vuole il design system sono monocromatiche: il tipo si riconosce dal disegno. Lo stesso vale per i file che nessun programma del PC apre. I programmi, i collegamenti e i file dei programmi installati tengono l'icona di Windows, che per loro fa da logo.

L'icona di Quiver è una bacheca: tre riquadri, uno dei quali ambra, come un riquadro selezionato. Si rigenera con `python scripts/genera-icona.py`.

## Riga di comando

- `Quiver.exe --toggle | --show | --hide | --exit` passa il comando all'istanza già aperta, ad esempio da una scorciatoia di GlazeWM.
- `Quiver.exe --sort <file> [<file>…]` apre il pannello chiedendo dove mandare quei file: è quello che fa **Invia a Quiver**. Se Quiver è già aperto, i file passano all'istanza in esecuzione.
- `Quiver.exe --archive <cartella> --render-preview <file.png> [--topic Lavoro/Clienti] [--query testo] [--prompt | --prompt-link | --prompt-move | --notice] [--inbox [--desktop <cartella>]]` disegna il pannello in un'immagine senza aprire finestre. Serve per i controlli automatici:
  - `--prompt` aggiunge una richiesta di conferma, `--prompt-link` la domanda di **Nuovo collegamento…** con un errore, `--prompt-move` l'elenco di **Sposta in…**, `--notice` la barra di stato con **Annulla** e **Mostra**;
  - `--inbox` mostra **Da smistare**, e `--desktop` usa una cartella al posto del Desktop vero, come nelle prove;
  - `--settings` disegna la finestra **Impostazioni**, con `--section archives|hotkey|inbox|windows` la pagina, e con `--welcome` com'è al primo avvio.

## Test e diagnostica

- `dotnet test` esegue tre gruppi di test:
  - le regole dell'archivio, la ricerca, i metadati, Da smistare, i tipi di file, il ripristino dal Cestino (su un Cestino finto, costruito dal test) e il confronto byte per byte di una copia;
  - le operazioni sui file: spostare, copiare, rinominare, i collegamenti e i file trascinati da Outlook e dal browser. Il Cestino vero è escluso, per non riempirlo;
  - il pannello: rinomina sul posto, selezione dopo spostamenti e ricaricamenti, nuove cartelle e nuovi riquadri, ordine dei riquadri, `Ctrl+Z`, **Sposta in…** con le destinazioni recenti e i pulsanti della barra di stato;
  - gli archivi: creare, aprire una cartella, rinominare e spostare l'archivio in uso, anche su un altro disco;
  - le impostazioni: la scorciatoia provata subito e tenuta solo se libera, gli interruttori salvati e applicati, su una Windows finta;
  - il collegamento di **Invia a**, fatto in una cartella del test, e il passaggio dei file tra due istanze, senza toccare la pipe vera.
- Il log si trova in `%LOCALAPPDATA%\Quiver\quiver.log`. Registra il tempo di apertura del pannello, perché si chiude e quale finestra gli toglie il focus.
