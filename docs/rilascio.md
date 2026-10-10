# Versioni e rilascio

Come si numera una versione, come si scrive la nota e come si pubblica l'installer su GitHub.

## Le versioni

- **Dove sta**: `<Version>` in `src/Inlay.App/Inlay.App.csproj`. Lo script dell'installer la legge da lì.
- **Come si numera**: la seconda cifra segue le versioni del piano ([PROGETTO.md §6](../PROGETTO.md#6-funzioni-e-versioni)): 0.1 il prototipo, 0.2 la gestione completa, 0.3 la tela, 0.4 trasloco e GlazeWM. La terza cresce a ogni rilascio dentro quel piano, novità o correzioni.
- **Le release su GitHub** (`caste9612/Inlay`, repository pubblico dal 10 ottobre 2026; fino alla 0.3.2 era `caste9612/Quiver`, e GitHub manda i vecchi indirizzi al nuovo): dalla 0.2.0 alla 0.2.2 pre-release, dalla 0.2.3 release normali («da qui niente più pre-release», PROGETTO.md §10). L'ultima è la 0.3.6, con il badge **Vault** e la creazione dal mosaico; la 0.3.5 ha portato la pagina **About**, la 0.3.4 è la prima con l'interfaccia in inglese, la 0.3.3 la prima con il nome Inlay. Ogni release ha il suo tag `vX.Y.Z` sul commit da cui è stata costruita.

## La nota di versione (`novita/<versione>.md`)

È il testo della release su GitHub e delle note che Velopack mette nel pacchetto. È scritta per chi usa Inlay, in italiano semplice, sempre con la stessa forma:

1. `# Inlay X.Y.Z`, poi una frase su cosa porta la versione e come installarla: «Scarica **Inlay-win-Setup.exe** e lancialo: se hai già Inlay, prima esci…».
2. Il paragrafo sull'avviso di Windows per l'installer non firmato.
3. `## Novità` e/o `## Corretto`: una voce per cosa, con il nome in grassetto come lo vede l'utente.
4. `## Da sapere`: per la prima versione con il nome Inlay, come si passa da Quiver (uscire da Quiver e disinstallarlo prima di installare Inlay, che l'installer avvia subito; cosa viene preso al primo avvio); poi cosa cambia nell'archivio (se la versione lo legge come la precedente, cosa scrive in `.inlay\archive.json`, cosa succede tornando alla versione prima) e quanti test ci sono, e che passano tutti.

## La prassi dei commit

Dalla storia del repository:

- Un commit con le modifiche, con un messaggio in italiano che dice cosa cambia per chi usa Inlay: «Colori degli argomenti, barra di stato come in Explorer, avvisi, spazio su disco». README e PROGETTO si aggiornano nello stesso commit.
- Poi un commit «Versione X.Y.Z» che alza `<Version>` e aggiunge `novita/X.Y.Z.md`.
- Poi la pubblicazione, che crea il tag.

## Prima di pubblicare

1. `dotnet test`: passano tutti, e il numero va nella nota ([test.md](test.md)).
2. Le anteprime delle parti dell'interfaccia toccate (`--render-preview`).
3. L'archivio vero si legge come con la versione prima, in sola lettura ([test.md](test.md#prima-di-pubblicare)).
4. README e PROGETTO dicono com'è adesso: i gesti nuovi nel README, lo stato e i prossimi passi in PROGETTO (§10).
5. Versione alzata, nota scritta, commit «Versione X.Y.Z», `git push`.
6. `.\scripts\New-Installer.ps1 -Publish`.

## Lo script (`scripts/New-Installer.ps1`)

```powershell
.\scripts\New-Installer.ps1                       # solo l'installer, in artifacts\installer
.\scripts\New-Installer.ps1 -Publish              # anche la release su GitHub
.\scripts\New-Installer.ps1 -Publish -Prerelease  # segnata come da provare
.\scripts\New-Installer.ps1 -Version 0.3.3        # un'altra versione da quella del csproj
```

Cosa fa:

1. Prende la versione dal csproj, se non è indicata, e controlla che esista `novita\<versione>.md`.
2. Con `-Publish`, prima di compilare: niente modifiche fuori dal commit (i file non tracciati non contano), il commit deve essere già su GitHub e la release `v<versione>` non deve esistere. Così l'installer e il codice della release coincidono.
3. Svuota `artifacts\publish` e `artifacts\installer` (la cartella `artifacts` è ignorata da git).
4. `dotnet publish` in Release per `win-x64`, non autonomo (usa il runtime .NET 10 Desktop installato) e ReadyToRun, per partire più in fretta con Windows.
5. `dotnet tool restore` e `dotnet vpk pack` (Velopack 1.2.161, nel manifesto `dotnet-tools.json`): id e titolo `Inlay`, `Inlay.exe` come eseguibile, l'icona, il runtime `net10-x64-desktop` (l'installer lo scarica se manca), il collegamento solo nel menu Start (sul Desktop sarebbe strano, per un programma che ne prende il posto) e la nota come testo della versione.
6. Con `-Publish`, `gh release create v<versione>` con tutti i file di `artifacts\installer`, il tag sul commit appena compilato, il titolo «Inlay <versione>» e la nota. Serve `gh` collegato all'account.

In `artifacts\installer`, `Inlay-win-Setup.exe` è l'installer da lanciare; gli altri file servono agli aggiornamenti.

## Velopack nell'app

- **Dove si installa**: per l'utente, senza amministratore, in `%LOCALAPPDATA%\Inlay`, con il collegamento nel menu Start. Si disinstalla da Impostazioni › App; l'archivio resta dov'è. `Inlay-win-Setup.exe --silent` installa senza finestre e non avvia Inlay; `Update.exe --uninstall --silent`, nella cartella di installazione, disinstalla senza finestre.
- **`Program.Main`** passa prima da Velopack: dopo l'installazione accende l'avvio con Windows e «Invia a», dopo un aggiornamento riscrive la voce di «Invia a» per il nuovo `Inlay.exe`, se c'è ancora, prima della disinstallazione li toglie ([windows.md](windows.md#lavvio-program-app)).
- **Aggiornare** (`Services/Updater.cs`, `GitHubUpdater`): Inlay legge le release del repository pubblico con `GithubSource`, senza token, e solo quelle normali: le pre-release no. `App.StartUpdates` controlla un minuto dopo l'avvio e poi ogni sei ore; `CheckForUpdates` scarica la versione nuova (`PrepareAsync`) e la mette nella barra di stato (`MainViewModel.UpdateReady`), nel menu della tray (`TrayIcon.ShowUpdateReady`) e nella pagina **About** (`UpdatesViewModel`), con un avviso della tray una volta per versione. Com'è andato ogni controllo, anche quando non c'è niente di nuovo o GitHub non risponde, lo dice la pagina **About**, non la tray; **Check for updates**, nella pagina o nel menu della tray, controlla subito. «Restart to update» aspetta le operazioni sui file, come **Quit**, chiude la finestra e chiama `ApplyUpdatesAndRestart`; **Quit** con una versione pronta chiama `WaitExitThenApplyUpdates`; una versione scaricata e mai applicata, per esempio se Windows si spegne prima, la ritrova il controllo dopo il prossimo avvio, e la offre di nuovo. Velopack non la applica all'avvio (`SetAutoApplyOnStartup(false)` in `Program.Main`): un secondo `Inlay.exe`, come `--toggle` da GlazeWM o «Invia a», la applicherebbe sotto l'Inlay in esecuzione. Dopo il riavvio `Program.UpdatedTo` fa dire alla tray a che versione è arrivato. Lanciato dal codice (`dotnet run`) Inlay non è installato, e non cerca niente.
- **Ogni release pubblicata è un aggiornamento**: arriva a tutti gli Inlay installati entro qualche ora. Per questo vanno pubblicati tutti i file di `artifacts\installer`, `releases.win.json` compreso, e una release non si cancella. La prima versione di Inlay è la 0.3.3, dopo la 0.3.2, l'ultima di Quiver; chi aveva Quiver la installa a mano, perché Quiver non sapeva aggiornarsi.
- **Firma**: l'installer non è firmato, quindi Windows (SmartScreen) avvisa la prima volta: **Ulteriori informazioni**, poi **Esegui comunque**.

## Provare un aggiornamento prima di pubblicarlo

Con la variabile d'ambiente `INLAY_UPDATES` Inlay cerca le versioni nuove in una cartella, o a un indirizzo, invece che su GitHub (`GitHubUpdater.Create`). Così un aggiornamento si prova sul PC senza pubblicare niente e senza installare: in una copia portatile.

1. Due versioni nella stessa cartella: per ognuna `dotnet publish` e `dotnet vpk pack` come fa lo script, con `--packVersion` diversi e lo stesso `--outputDir`. Dalla seconda in poi vpk fa anche il pacchetto delta.
2. `Inlay-win-Portable.zip` della prima, estratto in una cartella temporanea: dentro c'è `Inlay.exe`, che avvia `current\Inlay.exe`, e `Update.exe`. Una copia portatile non accende l'avvio con Windows né «Invia a».
3. Prima di avviarla, `%APPDATA%\Inlay\settings.json` con un archivio di prova e una scorciatoia libera: senza, Inlay prenderebbe le impostazioni di Quiver, e con loro l'archivio vero.
4. `INLAY_UPDATES` sulla cartella dei pacchetti, poi `Inlay.exe`: un minuto dopo il log (`%LOCALAPPDATA%\Inlay\inlay.log`) dice «Update ready».
5. **Quit** applica la versione nuova (`current\sq.version`); «Inlay X is ready» nella barra di stato riavvia Inlay aggiornato, e il log dice «Updated to».
6. Alla fine si tolgono `%APPDATA%\Inlay`, `%LOCALAPPDATA%\Inlay` e la cartella temporanea.

Il 10 ottobre 2026 la prova, con le versioni da 0.9.0 a 0.9.3 e un archivio fatto da Quiver, ha dato: `.quiver` diventa `.inlay` alla prima apertura; ogni versione nuova arriva un minuto dopo l'avvio, anche come delta; si applica all'uscita e con il pulsante della barra di stato (riavvio in 3 secondi); un secondo `Inlay.exe --toggle` con una versione scaricata la lascia stare; archivio, metadati, impostazioni e posti recenti restano identici da una versione all'altra.

Dal vero, da GitHub, la prima volta con la 0.3.4: l'Inlay 0.3.3 installato sul PC, riavviato, l'ha trovata 58 secondi dopo l'avvio e l'ha scaricata (`Inlay-0.3.4-full.nupkg`, 5,4 MB, in `%LOCALAPPDATA%\Inlay\packages`); all'uscita (`Inlay.exe --exit`, come **Esci** nel menu della 0.3.3) si è applicata in un secondo, e Inlay è ripartito come 0.3.4. Nessuno dei 21.063 file dell'archivio è cambiato, e nemmeno `archive.json` e le impostazioni; l'avvio con Windows punta a `current\Inlay.exe`, e la voce di «Invia a» è stata riscritta per la versione nuova.

## Compatibilità dei dati

- `archive.json` non ha migrazioni: i campi nuovi si aggiungono solo quando servono, e una versione vecchia che non li conosce li perde al primo salvataggio, tenendo tutto il resto ([archivio.md](archivio.md#i-metadati-archivemetadata-metadatastore)). I file non si toccano mai.
- `settings.json` e `recenti.json` stanno sul PC, fuori dall'archivio ([windows.md](windows.md#i-file-sul-pc)).
- Ogni nota di versione dice cosa succede ai dati tornando alla versione prima.
