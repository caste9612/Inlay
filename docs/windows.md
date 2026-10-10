# L'app e Windows

Come Inlay parte, vive nell'area di notifica, gestisce la sua finestra e parla con la shell di Windows. Il codice sta in `src/Inlay.App`: `Program.cs`, `App.xaml.cs`, `PanelWindow.xaml.cs`, `SettingsWindow.xaml.cs`, e le cartelle `Services` e `Interop`.

## L'avvio (`Program`, `App`)

`Program.Main` è il punto di partenza al posto di quello generato da WPF (`<StartupObject>` nel csproj), perché Velopack deve passare prima di tutto: quando Windows lancia Inlay per installarlo, aggiornarlo o toglierlo, Velopack fa la sua parte e chiude il processo prima di qualunque finestra.

- Dopo l'installazione: avvio con Windows e voce in «Invia a».
- Dopo un aggiornamento: se la voce di «Invia a» c'è, viene riscritta, perché punti al `Inlay.exe` nuovo.
- Prima della disinstallazione: toglie l'avvio con Windows e la voce di «Invia a».

Poi `App.OnStartup`, in quest'ordine:

1. Legge la riga di comando (`CommandLineOptions.Parse`, [più sotto](#la-riga-di-comando-commandlineoptions)).
2. Con `--render-preview` disegna l'anteprima ed esce; con `--benchmark-canvas` misura la tela ([test.md](test.md#verificare-linterfaccia-senza-aprirla)). Nessuno dei due tocca l'istanza già aperta.
3. Aggancia `DispatcherUnhandledException`: un errore imprevisto finisce nel log e in un messaggio, e l'app resta aperta.
4. Istanza unica: se Inlay è già aperto gli passa il comando (di solito `toggle`, `sort` se ci sono file) ed esce. Se non è aperto e il comando è `--exit`, esce e basta.
5. La prima volta prende le impostazioni e i posti recenti di Quiver, il nome di Inlay fino alla 0.3.2 (`FromQuiver.TakeOverSettings`: li copia da `%APPDATA%\Quiver` se `%APPDATA%\Inlay` non c'è ancora). Poi carica le impostazioni. L'archivio è quello di `--archive`, che vale solo per quell'avvio, o quello delle impostazioni. Se non c'è, o non esiste più, apre la finestra **Settings** come benvenuto («Welcome to Inlay»); se l'utente non sceglie niente, esce.
6. Crea `MainViewModel` e `PanelWindow`, e chiama `Prepare()`: la finestra nasce e si dispone senza comparire, così la prima apertura è veloce come le altre.
7. Crea l'icona nella tray e registra la scorciatoia; se la scorciatoia è occupata, lo dice con un avviso della tray.
8. Comincia ad ascoltare i comandi delle altre istanze.
9. Con `--sort` chiede subito dove mettere i file; con `--show` o `--toggle` si mostra. Altrimenti resta nell'area di notifica.

`App` fa anche da `ISettingsHost` per la finestra **Settings**: dà l'archivio in uso, lo cambia (`MainViewModel.LoadArchive`), lo lascia libero mentre viene spostato (`ReleaseArchive`) e prova una scorciatoia nuova.

### L'uscita

`ShutdownMode` è `OnExplicitShutdown`: chiudere una finestra non chiude Inlay. Si esce solo con **Quit** nel menu della tray o con `--exit` (`App.Quit`), oppure con «Restart to update», che esce per applicare una versione nuova ([rilascio.md](rilascio.md#velopack-nellapp)): tutti e due passano da `BeginLeaving`. Se un'operazione sui file è in corso, Inlay lo dice («Inlay will quit as soon as the file operation under way is done.») e aspetta che finisca (`MainViewModel.WhenIdle`), per non lasciare un file copiato a metà. Lo stesso per la copia di un archivio su un altro disco (`ArchivesViewModel.WhenCopied`), che lascerebbe mezzo archivio. Quando Windows si spegne (`OnSessionEnding`) la finestra si chiude subito, ricordando dov'era.

## La finestra (`PanelWindow`)

Una sola finestra, creata all'avvio e poi solo mostrata e nascosta. `OnClosing` annulla la chiusura e la nasconde (Alt+F4, il menu della barra delle applicazioni), tranne quando Inlay sta uscendo (`AllowClose`). La X della barra del titolo è un pulsante di Inlay, e chiama direttamente `HidePanel`.

- **Cornice**: `WindowChrome` con `CaptionHeight` 32, bordi di 6 px per ridimensionare e niente vetro. La barra del titolo la disegna `PanelView`. Da ingrandita, Windows lascia la cornice invisibile fuori dallo schermo: il contenuto rientra di altrettanto (`HiddenFrame`, calcolato per i DPI del monitor) e la zona che trascina la finestra si allunga. `WindowCorners.MakeSmall` chiede gli angoli piccoli di Windows 11.
- **La scorciatoia** (`Toggle`) segue `WindowToggle.Decide`: nascosta la mostra, ridotta la ripristina, dietro altre finestre la porta davanti, già davanti la rimette nella tray.
- **Mostrare** (`ShowPanel`): ricorda la finestra che era davanti; la prima volta la mette dov'era l'ultima volta (`SetWindowPlacement`, che la sposta su uno schermo che c'è) o al centro del monitor in uso; poi `OnPanelShowing` rilegge l'archivio, la finestra compare, prende il focus, mette il cursore nella ricerca e anima il contenuto. Il tempo di apertura va nel log.
- **Prendere il focus** (`BringToFront`): se Windows lo rifiuta, perché un altro programma lo tiene, Inlay condivide per un attimo l'input con il thread di quella finestra (`AttachThreadInput`) e riprova. Se non ci riesce, il log dice quale finestra era davanti.
- **Nascondere** (`HidePanel`): salva il posto, nasconde, avvisa il view model (`OnPanelHidden`) e ridà il focus alla finestra di prima, se c'è ancora, è visibile, non è ridotta a icona e non è nascosta da GlazeWM su un altro workspace (`DWMWA_CLOAKED`). Così Windows non sceglie una finestra a caso, che con GlazeWM potrebbe portare in primo piano un altro workspace.
- **Tornando alla finestra** (dalla barra delle applicazioni, Alt+Tab o un clic) più di un secondo dopo averla mostrata, `OnWindowActivated` rilegge quello che è cambiato.
- **Dopo un'operazione sui file**, le finestre della shell hanno preso il focus: la finestra se lo riprende, ma solo se davanti c'è ancora una finestra di Inlay o nessuna.
- **Cosa ricorda**, nelle impostazioni: posizione e misura (in pixel, come le tiene Windows, e se era ingrandita), il pin «Always on top» (`Topmost`), la vista (bacheca o tela) e la barra degli argomenti aperta o chiusa.

## La finestra Impostazioni (`SettingsWindow`)

La prima volta si apre come finestra di benvenuto, con il titolo «Welcome to Inlay», e si chiude appena l'utente apre un archivio. Dopo, una alla volta, sulla pagina richiesta; aperta da un Inlay visibile gli appartiene e gli resta sopra. Chiudendola si fermano il controllo di una copia e il calcolo delle dimensioni degli archivi. Mentre la shell copia un archivio su un altro disco, invece, non si chiude (`ArchivesViewModel.TryClose`). Le pagine sono descritte in [interfaccia.md](interfaccia.md#le-impostazioni-settingsviewmodel-archivesviewmodel).

## I file sul PC

| Cosa | Dove | Chi |
|---|---|---|
| impostazioni | `%APPDATA%\Inlay\settings.json` | `AppSettings` |
| destinazioni recenti di «Move to…» | `%APPDATA%\Inlay\recenti.json` | `RecentDestinations` |
| log | `%LOCALAPPDATA%\Inlay\inlay.log` | `Log` |
| avvio con Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, valore `Inlay` | `Autostart` |
| voce di «Invia a» | `Inlay.lnk` nella cartella SendTo dell'utente, con l'argomento `--sort` | `SendToShortcut` |
| programma installato | `%LOCALAPPDATA%\Inlay` | Velopack ([rilascio.md](rilascio.md)) |

- **`AppSettings`**: l'archivio in uso, l'elenco degli archivi (il più recente per primo), la scorciatoia (`Alt+E`, `DefaultHotkey`), il posto della finestra, il pin, la vista e la barra degli argomenti. Si salva scrivendo prima un `.tmp`, come i metadati dell'archivio. Un file che non è JSON valido fa partire con i valori predefiniti; com'era, viene copiato in `settings.json.corrupt`, come succede ad `archive.json`, e lo dice il log.
- **`RecentDestinations`**: per ogni archivio, fino a 8 chiavi dei metadati, la più recente per prima. Sta fuori dall'archivio apposta: smistare un file non deve cambiare l'archivio.
- **`Log`**: una riga per evento, con data e ora. Oltre 1 MB il file ricomincia da capo. I test lo mandano in un file loro.
- **`Autostart`** e **`SendToShortcut`** dicono di essere attivi solo quando puntano proprio a questo `Inlay.exe`, non a una copia altrove. `WindowsIntegration` li offre alla finestra **Settings** attraverso `IWindowsIntegration`, che nelle anteprime e nei test è finta.

## L'istanza unica (`SingleInstance`)

- Un mutex `Local\Inlay.<utente>` dice se Inlay è già aperto.
- Le altre istanze gli scrivono su una named pipe `Inlay.<utente>`, solo per lo stesso utente (`CurrentUserOnly`), aspettando al massimo 2 secondi.
- Il protocollo (`SingleInstance.Protocol`): una riga con il comando, poi una riga per ogni argomento. Un percorso può contenere spazi, mai un a capo.
- I comandi sono `toggle`, `show`, `hide`, `exit` e `sort` (`Commands`). L'evento `CommandReceived` arriva su un thread in background e `App` lo passa al thread dell'interfaccia.

## La scorciatoia (`GlobalHotkey`)

Una finestra solo per i messaggi (`HWND_MESSAGE`) riceve `WM_HOTKEY`. `TryRegister` legge la scorciatoia con `KeyGestureConverter` («Alt+E», «Ctrl+Shift+Space»), toglie quella di prima e registra la nuova con `RegisterHotKey`, senza ripetizione. Se la nuova è occupata, `App` registra di nuovo quella di prima.

## L'icona nella tray (`TrayIcon`)

Con H.NotifyIcon. Un clic apre Inlay; il menu ha **Open Inlay** (con la scorciatoia accanto), **Open the vault in Explorer**, **Always on top** (aggiornato a ogni apertura del menu, perché il pin si può cambiare anche dalla barra del titolo), **Vaults…**, **Settings…**, **Restart to update to Inlay X** quando una versione nuova è pronta (`ShowUpdateReady`), **Check for updates** (solo per un Inlay installato: apre **Settings** › **About** e controlla) e **Quit**. Il tooltip dice la versione e la scorciatoia, «Inlay 0.3.5 (Alt+E)». `ForceCreate(false)` evita che H.NotifyIcon metta il processo in «modalità efficienza», che rallenterebbe il pannello.

## Cartelle di Windows (`WindowsFolders`)

- `ReservedForArchives()`: le cartelle che non possono fare da archivio, ognuna con il suo motivo in inglese: la cartella dell'utente (dentro la quale invece si può), il Desktop, Windows, i programmi (`Program Files`, `ProgramData`, `AppData`).
- `RecycleBinOf(path)`: la cartella del Cestino dell'utente su quel disco, `<disco>\$Recycle.Bin\<SID>`.

## La shell di Windows (`Interop`)

Le chiamate alle API di Windows sono scritte a mano: quelle che servono in più punti in `NativeMethods`, le altre accanto a chi le usa (`ShellFileOperations`, `VirtualFiles`, e in `Services` `ShellLauncher`, `FileAssociations`, `WindowsFolders`). Di solito con `[LibraryImport]`; con `[DllImport]` dove servono strutture con stringhe, un `StringBuilder` o interfacce COM, e in `VirtualFiles` anche per qualche funzione semplice.

- **`ShellFileOperations`**: spostare, copiare, rinominare ed eliminare passano da `SHFileOperation`, come in Explorer: finestra di avanzamento, domanda quando un nome è già preso, eliminazioni nel Cestino (`FOF_ALLOWUNDO`) con l'avviso quando non è possibile (`FOF_WANTNUKEWARNING`).
  - `MoveEach` sposta ogni elemento al suo percorso completo (`FOF_MULTIDESTFILES`): è così che gli annullamenti rimettono le cose dov'erano.
  - `Copy` con `renameOnCollision` dà i nomi «- Copia», come Explorer quando si copia nella stessa cartella.
  - Restituisce `false` se l'operazione non è riuscita o l'utente l'ha annullata. Vuole un thread STA. Con `interactive: false`, solo nei test, non mostra nessuna finestra e sostituisce i file che ci sono già.
- **`ShellLinks`**: i collegamenti `.lnk`, con `IShellLinkW`. `CreateIn(folder, target)` copia un collegamento invece di farne uno che punta a un altro. Il nome viene da `NameFor`: per un programma la descrizione del file («Google Chrome» per `chrome.exe`), per un disco l'etichetta («Dati (D)», o «Drive (D)» senza etichetta), per una condivisione «commesse on server» («Network» se il percorso non la dice), per il resto il nome del file con l'estensione; un nome che Windows non accetta diventa «Shortcut». La cartella di lavoro è quella del programma.
- **`VirtualFiles`**: i file che esistono solo dentro un altro programma finché non vengono lasciati, come gli allegati e i messaggi di Outlook o i link e le immagini del browser. Arrivano come elenco di nomi (`FileGroupDescriptorW`) più il contenuto di ognuno (`FileContents`), da leggere durante il rilascio. `SaveTo` li scrive nella cartella: nomi resi sicuri (niente `..`, caratteri non validi sostituiti), nomi liberi come Explorer, data di modifica conservata. Un messaggio di Outlook arriva come `IStorage` e diventa un file `.msg`.
- **`ShellNotify`**: avvisa Explorer dei cambiamenti fatti senza la shell, come un elemento rimesso a posto dal Cestino, e aggiorna l'icona del Cestino.
- **`WindowCorners.MakeSmall`**: gli angoli piccoli di Windows 11 per le finestre disegnate da Inlay.
- **`WindowInfo.Describe`**: processo, titolo e classe di una finestra, per il log.

## Gli altri servizi di Windows (`Services`)

- **`ShellLauncher`**: aprire come un doppio clic in Explorer, mostrare in Explorer con l'elemento selezionato, «Apri con» e «Proprietà».
- **`ShellIcons`**: le icone di Windows per programmi, collegamenti e file senza un'icona di Brace, con una cache. Si leggono su un thread STA tutto loro, perché un collegamento a una cartella di rete che non risponde può bloccare per secondi; intanto il pannello mostra il documento di Brace. I file normali condividono l'icona della loro estensione; programmi, collegamenti, icone e cartelle si leggono uno per uno. Nelle anteprime (`Synchronous`) le icone si leggono subito, sullo stesso thread.
- **`FileAssociations.IsKnown(extension)`**: se un programma del PC apre quel tipo di file. Se nessuno lo apre, l'icona è il documento di Brace.
- **`FileClipboard`**: i file negli appunti nello stesso formato di Explorer, con «Preferred DropEffect» per dire se sono copiati o tagliati. Se un altro programma tiene occupati gli appunti, le chiamate falliscono senza errori.
- **`DiskSpace`**: lo spazio libero per questo utente, quote comprese (`GetDiskFreeSpaceEx`). È poco (`IsLow`) sotto i 5 GB o sotto il 10 %. Lo dice come «120 GB free» e, per il tooltip, «Drive E: 120 GB free of 465 GB».
- **`DateLabel`**: «today», «yesterday», «5 Oct», «12 Mar 2025», all'inglese qualunque sia la lingua di Windows; la data completa per i tooltip («5 Oct 2026, 14:32»).

## La riga di comando (`CommandLineOptions`)

Quelle per l'utente sono descritte nel [README](../README.md#riga-di-comando). Tutte quelle che `Parse` riconosce:

| Opzione | Cosa fa |
|---|---|
| `--toggle`, `--show`, `--hide`, `--exit` | il comando per l'istanza aperta |
| `--sort <file>…` | i file da mandare nell'archivio: tutto quello che segue è un percorso, perché Explorer aggiunge i file in fondo |
| `--archive <cartella>` | l'archivio, solo per questo avvio |
| `--render-preview <file.png>` | disegna il pannello in un'immagine ed esce |
| `--topic`, `--query`, `--canvas`, `--zoom` | cosa mostrare nell'anteprima |
| `--prompt`, `--prompt-link`, `--prompt-move`, `--notice` | una domanda, «Move to…» o gli avvisi sopra la bacheca, nell'anteprima |
| `--settings` (o `--archives`), `--section`, `--welcome` | l'anteprima della finestra **Settings** |
| `--benchmark-canvas <file.txt>` | misura quanto è fluida la tela |
