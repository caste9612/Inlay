# Versioni e rilascio

Come si numera una versione, come si scrive la nota e come si pubblica l'installer su GitHub.

## Le versioni

- **Dove sta**: `<Version>` in `src/Inlay.App/Inlay.App.csproj`. Lo script dell'installer la legge da lì.
- **Come si numera**: la seconda cifra segue le versioni del piano ([PROGETTO.md §6](../PROGETTO.md#6-funzioni-e-versioni)): 0.1 il prototipo, 0.2 la gestione completa, 0.3 la tela, 0.4 trasloco e GlazeWM. La terza cresce a ogni rilascio dentro quel piano, novità o correzioni.
- **Le release su GitHub** (`caste9612/Inlay`, repository privato; fino alla 0.3.2 era `caste9612/Quiver`, e GitHub manda i vecchi indirizzi al nuovo): dalla 0.2.0 alla 0.2.2 pre-release, dalla 0.2.3 release normali («da qui niente più pre-release», PROGETTO.md §10). L'ultima è la 0.3.2. Ogni release ha il suo tag `vX.Y.Z` sul commit da cui è stata costruita.

## La nota di versione (`novita/<versione>.md`)

È il testo della release su GitHub e delle note che Velopack mette nel pacchetto. È scritta per chi usa Inlay, in italiano semplice, sempre con la stessa forma:

1. `# Inlay X.Y.Z`, poi una frase su cosa porta la versione e come installarla: «Scarica **Inlay-win-Setup.exe** e lancialo: se hai già Inlay, prima esci…».
2. Il paragrafo sull'avviso di Windows per l'installer non firmato.
3. `## Novità` e/o `## Corretto`: una voce per cosa, con il nome in grassetto come lo vede l'utente.
4. `## Da sapere`: per la prima versione con il nome Inlay, come si passa da Quiver (installare Inlay, disinstallare Quiver, cosa viene preso al primo avvio); poi cosa cambia nell'archivio (se la versione lo legge come la precedente, cosa scrive in `.inlay\archive.json`, cosa succede tornando alla versione prima) e quanti test ci sono, e che passano tutti.

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

- **Dove si installa**: per l'utente, senza amministratore, in `%LOCALAPPDATA%\Inlay`, con il collegamento nel menu Start. Si disinstalla da Impostazioni › App; l'archivio resta dov'è.
- **`Program.Main`** passa prima da Velopack: dopo l'installazione accende l'avvio con Windows e «Invia a», dopo un aggiornamento riscrive la voce di «Invia a» per il nuovo `Inlay.exe`, se c'è ancora, prima della disinstallazione li toglie ([windows.md](windows.md#lavvio-program-app)).
- **Aggiornare**: oggi si scarica e si lancia l'installer nuovo, dopo essere usciti da Inlay; l'installer chiede se aggiornare, e archivio e impostazioni restano. Gli aggiornamenti automatici da GitHub sono rimandati, perché con il repository privato servirebbe un token.
- **Firma**: l'installer non è firmato, quindi Windows (SmartScreen) avvisa la prima volta: **Ulteriori informazioni**, poi **Esegui comunque**.

## Compatibilità dei dati

- `archive.json` non ha migrazioni: i campi nuovi si aggiungono solo quando servono, e una versione vecchia che non li conosce li perde al primo salvataggio, tenendo tutto il resto ([archivio.md](archivio.md#i-metadati-archivemetadata-metadatastore)). I file non si toccano mai.
- `settings.json` e `recenti.json` stanno sul PC, fuori dall'archivio ([windows.md](windows.md#i-file-sul-pc)).
- Ogni nota di versione dice cosa succede ai dati tornando alla versione prima.
