# Quiver

Prototipo di una "scrivania" da richiamare con una scorciatoia, al posto del desktop di Windows.
Idea, regole dell'archivio e piano delle versioni sono in [PROGETTO.md](PROGETTO.md).

## Avvio

Serve Windows 11 con l'SDK di .NET 10.

```powershell
dotnet build Quiver.slnx
dotnet run --project src/Quiver.App
```

Al primo avvio Quiver chiede quale cartella usare come archivio. Per provarlo senza i tuoi file:

```powershell
.\scripts\New-DemoArchive.ps1 -Path D:\ArchivioDemo
dotnet run --project src/Quiver.App -- --archive D:\ArchivioDemo
```

`--archive` vale solo per quell'avvio: non cambia l'archivio salvato nelle impostazioni. Con un archivio di prova conviene aggiungere `--desktop <cartella>`: **Da smistare** mostra quella cartella al posto del Desktop vero, così trascinando non si spostano i propri file nell'archivio di prova.

Per usarlo tutti i giorni conviene installarne una copia a parte, così lo sviluppo può andare avanti mentre quella resta aperta:

```powershell
.\scripts\Install-Quiver.ps1
```

Lo script pubblica Quiver in `%LOCALAPPDATA%\Programs\Quiver` e lo avvia, dopo aver chiuso quello eventualmente aperto. Per aggiornarlo basta rilanciarlo. **Avvia con Windows** si attiva dall'icona nella tray della copia installata.

## Uso

`Alt+E` apre e chiude il pannello (la scorciatoia si cambia in `%APPDATA%\Quiver\settings.json`). Dentro, si usa come il desktop.

- **Selezionare**: un clic seleziona, `Ctrl`+clic e `Maiusc`+clic aggiungono altri elementi. Con `Ctrl` si sceglie anche da riquadri diversi. Un clic su uno spazio vuoto deseleziona.
- **Aprire**: doppio clic o `Invio`. `Ctrl+Invio` mostra l'elemento in Explorer.
- **Rinominare**: `F2`, oppure un secondo clic, lento, su un elemento già selezionato. Il nome si modifica sul posto e, come in Explorer, i collegamenti non mostrano `.lnk` o `.url`. `Invio` conferma, `Esc` annulla e un clic altrove conferma.
- **Eliminare**: `Canc` sposta nel Cestino. Per le cartelle Quiver chiede prima conferma.
- **Annullare**: `Ctrl+Z` annulla l'ultima operazione fatta in Quiver, anche più volte di seguito. Vale per spostamenti, copie, collegamenti, rinomine, nuove cartelle, riquadri e argomenti, ordine dei riquadri e Cestino: quello che ci hai mandato torna al suo posto. Si trova anche nel menu del tasto destro sulla bacheca.
  - Se nel frattempo un elemento è stato spostato o rinominato, resta com'è.
  - Se un file è aperto in un altro programma, `Ctrl+Z` si può ripetere.
  - Su un disco senza Cestino (di rete o rimovibile) l'annullamento non elimina niente: lì eliminare sarebbe definitivo.
- **Copiare e spostare con gli appunti**: `Ctrl+C`, `Ctrl+X` e `Ctrl+V` funzionano anche da e verso Explorer. Si incolla nel riquadro o nell'argomento su cui hai cliccato, altrimenti nella bacheca; anche appena aperto il pannello, con la ricerca vuota. **Incolla collegamento**, dal tasto destro, crea collegamenti ai file copiati, che restano dove sono.
- **Tasto destro**: Apri con…, Copia percorso, Proprietà (anche con `Alt+Invio`) e le altre azioni.
- **Trascinare**:
  - un file su un riquadro, su un argomento o su **Da smistare** lo sposta lì. Come in Explorer, tra dischi diversi viene copiato; con `Ctrl` copi, con `Maiusc` sposti, con `Alt` (o `Ctrl+Maiusc`) crei un collegamento;
  - se tieni fermo quello che trascini sopra un argomento, o su **Da smistare**, dopo un attimo la bacheca passa lì e lo lasci nel riquadro giusto, come con le cartelle di Explorer;
  - quello che ha già un suo posto entra come collegamento, come chiede il progetto: i programmi (`.exe`, come fa Explorer) e le cartelle e i file in rete;
  - quello che arriva da **Da smistare** viene spostato anche su un altro disco: smistarlo è proprio lo scopo;
  - gli allegati e i messaggi di Outlook, e i link e le immagini dal browser, diventano file nel riquadro;
  - un riquadro, preso per il titolo, cambia posto nella bacheca, e l'ordine resta. Lasciato su un argomento, ci entra dentro;
  - se porti qualcosa fuori dal pannello, il pannello si nasconde e puoi lasciarlo in un'altra app;
  - per prendere file da Explorer, comincia a trascinarli, premi `Alt+E` senza lasciare il tasto del mouse e rilasciali sul riquadro.
- **Da smistare**: raccoglie in riquadri separati i file lasciati nella radice dell'archivio o nella cartella `_Da smistare` e quelli del Desktop, compreso quello comune a tutti gli utenti dove gli installer mettono i collegamenti. In `settings.json` si può togliere il Desktop (`"inboxDesktop": false`) o aggiungere Download (`"inboxDownloads": true`). La ricerca trova anche questi file.
- **Cercare**: basta scrivere. Con le frecce scegli un risultato, `Invio` lo apre ed `Esc` torna alla bacheca.
- **Creare**:
  - un argomento: il pulsante + accanto ad **Argomenti**; dentro un altro argomento, tasto destro su quello;
  - un riquadro: **Nuovo riquadro**, `Ctrl+Maiusc+N` o tasto destro sulla bacheca;
  - una cartella in un riquadro: tasto destro sul riquadro, o il pulsante … accanto al titolo;
  - un collegamento: **Nuovo collegamento…** dal tasto destro sul riquadro o sulla bacheca. Si scrive l'indirizzo di un sito, anche solo `rossi.it`, oppure il percorso di un programma, di un file o di una cartella.

  Come «Nuova cartella» sul desktop, il nuovo elemento nasce con il nome pronto da scrivere.
- **Riquadri e argomenti**: dal menu di un riquadro puoi scegliere **Promuovi ad argomento**; da quello di un argomento annidato, **Riporta a riquadro**.
- **Tray**: dall'icona puoi aprire il pannello, cambiare archivio, attivare **Avvia con Windows** o uscire.

## Aspetto

L'interfaccia usa Brace, il design system di riferimento: colori, caratteri (Inter e Cascadia Mono), icone Fluent e componenti. I file sono copiati in `src/Quiver.App/Brace` e `src/Quiver.App/Fonts`; da dove vengono e come si aggiornano è scritto in `src/Quiver.App/Brace/PROVENIENZA.md`.

L'icona è una bacheca: tre riquadri, uno dei quali ambra, come un riquadro selezionato. Si rigenera con `python scripts/genera-icona.py`.

## Riga di comando

- `Quiver.exe --toggle | --show | --hide | --exit` passa il comando all'istanza già aperta, ad esempio da una scorciatoia di GlazeWM.
- `Quiver.exe --archive <cartella> --render-preview <file.png> [--topic Lavoro/Clienti] [--query testo] [--prompt | --prompt-link] [--inbox [--desktop <cartella>]]` disegna il pannello in un'immagine senza aprire finestre. Serve per i controlli automatici:
  - `--prompt` aggiunge una richiesta di conferma, `--prompt-link` la domanda di **Nuovo collegamento…** con un errore;
  - `--inbox` mostra **Da smistare**, e `--desktop` usa una cartella al posto del Desktop vero, come nelle prove.

## Test e diagnostica

- `dotnet test` esegue tre gruppi di test:
  - le regole dell'archivio, la ricerca, i metadati, Da smistare e il ripristino dal Cestino (su un Cestino finto, costruito dal test);
  - le operazioni sui file: spostare, copiare, rinominare, i collegamenti e i file trascinati da Outlook e dal browser. Il Cestino vero è escluso, per non riempirlo;
  - il pannello: rinomina sul posto, selezione dopo spostamenti e ricaricamenti, nuove cartelle e nuovi riquadri, ordine dei riquadri, `Ctrl+Z`.
- Il log si trova in `%LOCALAPPDATA%\Quiver\quiver.log`. Registra il tempo di apertura del pannello, perché si chiude e quale finestra gli toglie il focus.
