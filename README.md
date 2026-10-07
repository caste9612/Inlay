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

`--archive` vale solo per quell'avvio: non cambia l'archivio salvato nelle impostazioni.

## Uso

- `Alt+E` apre e chiude il pannello (la scorciatoia si cambia in `%APPDATA%\Quiver\settings.json`).
- Per cercare basta scrivere. Con le frecce scegli un risultato, `Invio` lo apre, `Ctrl+Invio` lo mostra in Explorer e `Esc` chiude.
- Per creare:
  - un argomento: **+ Nuovo argomento**, in basso a sinistra;
  - un riquadro: `Ctrl+Maiusc+N`, oppure tasto destro sulla bacheca;
  - un argomento dentro un altro: tasto destro sull'argomento.
- Su un file o su un argomento, `F2` rinomina e `Canc` sposta nel Cestino. Per le cartelle Quiver chiede prima conferma. Le stesse azioni sono nel menu del tasto destro.
- Per spostare un file, trascinalo su un riquadro, su un argomento o su **Da smistare**. Come in Explorer, tra dischi diversi il file viene copiato; con `Ctrl` copi, con `Maiusc` sposti. Se trascini un file fuori dal pannello, il pannello si nasconde e puoi lasciarlo in un'altra app.
- Per prendere file da Explorer, comincia a trascinarli, premi `Alt+E` senza lasciare il tasto del mouse e rilasciali sul riquadro.
- Con il tasto destro su un riquadro puoi scegliere **Promuovi ad argomento**; su un argomento annidato, **Riporta a riquadro**.
- Dall'icona nella tray puoi aprire il pannello, cambiare archivio, attivare **Avvia con Windows** o uscire.

## Riga di comando

- `Quiver.exe --toggle | --show | --hide | --exit` passa il comando all'istanza già aperta, ad esempio da una scorciatoia di GlazeWM.
- `Quiver.exe --archive <cartella> --render-preview <file.png> [--topic Lavoro/Clienti] [--query testo]` disegna il pannello in un'immagine senza aprire finestre. Serve per i controlli automatici.

## Test e diagnostica

- `dotnet test` esegue i test delle regole dell'archivio, della ricerca e dei metadati, e quelli delle operazioni sui file (spostare, copiare, rinominare; il Cestino è escluso, per non riempirlo).
- Il log si trova in `%LOCALAPPDATA%\Quiver\quiver.log`. Registra il tempo di apertura del pannello, perché si chiude e quale finestra gli toglie il focus.
