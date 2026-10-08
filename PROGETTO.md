# Quiver — documento di progetto

*Stato: versione 0.2.1, pre-release su GitHub. Interfaccia su Brace, gesti del desktop provati dal vivo su un archivio di prova, `Ctrl+Z`, il Desktop in «Da smistare», i collegamenti, gli archivi gestiti come i vault di Obsidian. Dalla 0.2.1 anche «Sposta in…», «Invia a Quiver», le impostazioni nell'app, il colore dei riquadri e la selezione a rettangolo, non ancora provati dal vivo. Da provare nell'uso quotidiano e con GlazeWM. Ultimo aggiornamento: 8 ottobre 2026.*

## 1. Il problema

Il desktop di Windows viene usato come piano di lavoro: file, programmi, documenti e cartelle di argomenti diversi, sistemati a occhio in zone dello schermo. Funziona finché il desktop si vede. Con molte finestre aperte, e ancora di più con un tiling window manager come GlazeWM, è sempre coperto: per raggiungere qualcosa bisogna spostare o ridurre le finestre ogni volta.

In più, come sistema di organizzazione il desktop è fragile: un solo livello, nessuna ricerca, e la disposizione salta se cambiano la risoluzione o i monitor.

## 2. L'idea

Un'app dedicata che **sostituisce il desktop** come posto dove tenere e ritrovare le proprie cose, organizzate per argomento.

- I file vivono in un **archivio dedicato**: una normale cartella con sottocartelle vere, che l'app legge e gestisce.
- L'app si richiama all'istante con una scorciatoia (**pannello**) oppure resta aperta in un **workspace dedicato** di GlazeWM.
- Il desktop di Windows non serve più: le sue icone si possono nascondere e l'app lo usa solo come "inbox" per i file che i programmi continuano a salvarci.

### Perché non uno strumento esistente

- **Organizzatori del desktop con "Peek"** (Stardock Fences, PecoFence): portano il desktop sopra le finestre, ma il contenitore resta il desktop. Qui invece il desktop deve sparire.
- **Launcher** (PowerToys Command Palette, Flow Launcher, Raycast): ottimi per aprire qualcosa per nome, ma non hanno un'organizzazione visiva per argomento e non gestiscono i file.
- **File manager evoluti** (Directory Opus, Files): potenti, ma restano finestre di Explorer potenziate, non una scrivania da richiamare al volo.

## 3. Principi

1. **Il file system è la verità.** Argomenti e riquadri sono cartelle vere. Anche senza l'app i file restano ordinati e raggiungibili da Explorer; backup, OneDrive e ricerca di Windows funzionano come sempre.
2. **Mai perdere dati.** Niente cancellazioni definitive (si passa sempre dal Cestino), niente sovrascritture silenziose, ogni operazione si può annullare.
3. **Prima la tastiera**, ma con il mouse si può fare tutto.
4. **Veloce e leggera**: il pannello compare senza attese e l'app consuma poco mentre aspetta in background.
5. **Ciò che ha già un suo posto resta lì** (cartelle sul server, progetti, programmi installati): entra nell'archivio come collegamento.
6. **Pensata per GlazeWM**, ma funziona anche senza.

## 4. Come è organizzato l'archivio

### Archivi, come i vault di Obsidian

- Un archivio è una cartella normale. La cartella nascosta `.quiver` dentro la segna come archivio e ne tiene i metadati, così l'archivio si porta dietro la sua disposizione dovunque vada.
- Se ne possono avere più di uno. Le impostazioni del PC tengono l'elenco e quello in uso.
- La pagina **Archivi** della finestra Impostazioni li gestisce:
  - al primo avvio dà il benvenuto e chiede di crearne uno o di usare una cartella che si ha già;
  - poi si apre dal nome dell'archivio in alto nel pannello o dal menu della tray;
  - permette di passare da un archivio all'altro, rinominarli, spostarli e toglierli dall'elenco, senza toccare i file.
- Spostare un archivio sullo stesso disco è un attimo: cambia solo il percorso della cartella. Su un altro disco Quiver lo copia, confronta la copia con l'originale byte per byte e passa alla copia solo se sono identiche. La cartella vecchia va nel Cestino solo se lo dice l'utente.
- Non possono essere archivi un disco intero, il Desktop (è tra le cose da smistare), la cartella dell'utente, quelle di Windows e dei programmi. Un archivio non sta dentro un altro né ne contiene uno.
- Il posto consigliato è sullo stesso disco del Desktop, per esempio `C:\Users\<utente>\Archivio`. Così spostare nell'archivio i file del Desktop è istantaneo, senza copie.

### Struttura

Ogni archivio è organizzato così (es. `D:\Archivio`):

```
D:\Archivio\
├─ _Da smistare\                ← inbox interna
├─ Lavoro\                      ← argomento (menu laterale)
│  ├─ Clienti\                  ← sotto-argomento
│  │  ├─ Rossi\                 ← sotto-argomento
│  │  │  ├─ Documenti\          ← riquadro (bacheca di Rossi)
│  │  │  │  ├─ Offerta_v3.docx  ← elemento
│  │  │  │  └─ Commessa 2026\   ← elemento cartella
│  │  │  ├─ Programmi\          ← riquadro
│  │  │  │  └─ SolidWorks.lnk   ← programma (collegamento)
│  │  │  └─ Note.md             ← file sciolto: riquadro "Generale"
│  │  └─ Bianchi\
│  └─ Interni\
├─ Personale\
├─ Strumenti\
└─ .quiver\                     ← nascosta: metadati dell'archivio
```

### Regole (albero libero)

- **Argomento**: una cartella che compare nel menu laterale. Le cartelle direttamente nella radice sono argomenti. Più in basso una cartella diventa argomento solo se la si "promuove". Gli argomenti si annidano su quanti livelli si vuole (Lavoro → Clienti → Rossi).
- **Riquadro**: ogni altra sottocartella di un argomento. Compare come riquadro sulla bacheca dell'argomento. Una cartella creata da Explorer è un riquadro finché non la si promuove ad argomento.
- **Generale**: riquadro automatico con i file sciolti che stanno direttamente nella cartella dell'argomento.
- **Elemento**: tutto ciò che sta dentro un riquadro: file, cartelle (si aprono in Explorer, in seguito anche dentro l'app) e collegamenti.
- Un argomento che contiene solo sotto-argomenti mostra sulla bacheca le loro schede, per navigarci dentro.
- I file lasciati direttamente nella radice finiscono nell'inbox.
- Cartelle speciali: `_Da smistare` (inbox) e `.quiver` (nascosta).

### Tipi di elemento

| Tipo | Come è salvato | Esempio |
|---|---|---|
| File o cartella | così com'è | `Offerta_v3.docx` |
| Programma | collegamento `.lnk` (vale anche per le app dello Store) | `SolidWorks.lnk` |
| File o cartella esterni | collegamento `.lnk` | cartella della commessa sul server |
| Sito web | collegamento `.url` | portale del cliente |
| Nota | file `.md` o `.txt` | `Note.md` |

Ogni elemento è un file vero: Explorer vede esattamente quello che vede l'app.

### Metadati

Quello che le cartelle non possono memorizzare sta in `<radice>\.quiver\archive.json`. I percorsi sono relativi alla radice, così l'archivio si può spostare o sincronizzare portandosi dietro la disposizione:

```json
{
  "version": 1,
  "folders": {
    "Lavoro/Clienti":                 { "kind": "topic" },
    "Lavoro/Clienti/Rossi":           { "kind": "topic", "workspace": "3" },
    "Lavoro/Clienti/Bianchi":         { "kind": "topic" },
    "Lavoro/Interni":                 { "kind": "topic" },
    "Lavoro/Clienti/Rossi/Documenti": { "tile": { "x": 0, "y": 0, "w": 2, "h": 3 } },
    "Lavoro/Clienti/Rossi/Programmi": { "tile": { "x": 2, "y": 0, "w": 1, "h": 2 }, "color": "#2D7FF9" }
  }
}
```

- `"kind": "topic"` serve solo sotto il primo livello, perché le cartelle nella radice sono già argomenti.
- Se una cartella viene rinominata o spostata dall'app, i metadati si aggiornano subito. Se succede da Explorer con l'app aperta, l'app se ne accorge. Se succede ad app chiusa, i metadati rimasti senza cartella vengono riallineati all'avvio. Nel caso peggiore si perdono la posizione e il colore di un riquadro, mai i file.

Le impostazioni del singolo PC (archivio in uso ed elenco degli archivi, scorciatoia, sorgenti dell'inbox) stanno in `%APPDATA%\Quiver\settings.json`, fuori dall'archivio, e si cambiano dalla finestra **Impostazioni**: le pagine sono Archivi, Scorciatoia, Da smistare e Avvio ed Explorer, e ogni cambiamento vale subito. La scorciatoia si registra premendola in un campo: Quiver la prova all'istante e la tiene solo se Windows gliela dà.

## 5. Interfaccia

```
┌─────────────────────────────────────────────────────────────────────┐
│ ▣ Quiver   [ Cerca in tutto l'archivio          ]                 ✕ │
├────────────────┬────────────────────────────────────────────────────┤
│ Argomenti    + │ Lavoro › Clienti                                   │
│ ▾ Lavoro       │ Rossi                           [+ Nuovo riquadro] │
│   ▾ Clienti    │                                                    │
│       Bianchi  │ ┌ Generale  1 …┐ ┌ Documenti 2 …┐ ┌ Programmi 2 …┐ │
│     ▌ Rossi    │ │ Note.md      │ │ Commessa 2026│ │ SolidWorks   │ │
│     Interni    │ └──────────────┘ │ Offerta_v3   │ │ TeamViewer   │ │
│   Personale    │                  └──────────────┘ └──────────────┘ │
│   Strumenti    │                                                    │
│                │                                                    │
│                │                                                    │
│ ↓ Da smistare 4│                                                    │
├────────────────┴────────────────────────────────────────────────────┤
│ D:\Archivio · 1.240 elementi             Invio apri  F2 rinomina  … │
└─────────────────────────────────────────────────────────────────────┘
```

- **In alto**: il logo, la ricerca su tutto l'archivio, il nome dell'archivio in uso (apre le Impostazioni sulla pagina **Archivi**) e il pulsante per chiudere.
- **Barra laterale**: l'albero degli argomenti, con + per crearne uno, e in fondo l'inbox con il numero di file da smistare.
- **Bacheca**: il percorso e il nome dell'argomento, il pulsante **Nuovo riquadro**, i sotto-argomenti come pulsanti e i riquadri in colonne, larghe almeno 280 px. Ogni riquadro ha il titolo, il numero di elementi e il menu …; ogni elemento ha la sua icona e la data di modifica.
- **Barra di stato**: l'archivio e quanti elementi contiene, oppure l'esito dell'ultima operazione, e i tasti principali.

### Aspetto

- **Brace**, il design system personale, dà colori (tema scuro), caratteri (Inter, Cascadia Mono), icone (Fluent) e componenti. La sua versione WPF è copiata in `src/Quiver.App/Brace` e non si modifica lì: le correzioni vanno fatte in Brace, così valgono per tutti i software che lo usano.
- **Icone dei file**: cartelle e tipi comuni hanno l'icona di Brace del loro concetto, dal suo dizionario «un concetto, un'icona». Ci sono PDF, Word, fogli di calcolo, presentazioni, immagini, video, audio, archivi compressi, codice, posta, pagine web e modelli 3D. Le icone sono monocromatiche come tutte quelle di Brace; quelle che mancavano sono state aggiunte a Brace stesso. I file che nessun programma del PC apre hanno il documento di Brace. I programmi, i collegamenti e i file dei programmi installati tengono l'icona di Windows, che per loro è un logo.
- **Icona**: una bacheca con tre riquadri, uno dei quali ambra come un riquadro selezionato. È disegnata in `scripts/genera-icona.py`; il logo nel pannello è lo stesso disegno, in `Themes/Quiver.xaml`.

### Due modalità, stessa interfaccia

- **Pannello** (prima versione): con la scorciatoia compare sopra le finestre, sul monitor attivo, con il cursore già nella casella di ricerca. Quando apri un elemento il pannello sparisce e il file si apre nel workspace in cui stavi lavorando. Si chiude con `Esc` o con un clic fuori.
- **Workspace Home** (versione successiva): l'app resta aperta a schermo intero in un workspace dedicato di GlazeWM. Quando apri un elemento da qui, l'app torna prima al workspace in cui stavi lavorando (o a quello collegato all'argomento) e lo apre lì.

### Tastiera

| Tasto | Azione |
|---|---|
| `Alt+E` (scorciatoia globale) | apre e chiude il pannello |
| scrivere | cerca in tutto l'archivio |
| frecce, `Tab` | spostarsi tra argomenti, riquadri ed elementi |
| `Invio` | apre (anche più elementi insieme) |
| `Ctrl+Invio` | mostra l'elemento in Explorer |
| `F2` | rinomina sul posto |
| `Canc` | sposta nel Cestino |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | copia, taglia, incolla, anche con Explorer |
| `Ctrl+A` | seleziona tutto il riquadro |
| `Ctrl+Maiusc+N` | nuovo riquadro |
| `Ctrl+Maiusc+C` | copia il percorso |
| `Alt+Invio` | proprietà |
| `Ctrl+Z` | annulla l'ultima operazione, anche più volte di seguito |
| `Esc` | cancella la ricerca, poi chiude il pannello |

### Mouse: come sul desktop

- Un clic seleziona; con `Ctrl` e `Maiusc` se ne selezionano altri, con `Ctrl` anche da riquadri diversi. Un clic sul vuoto deseleziona; trascinando sul vuoto si disegna un rettangolo che seleziona quello che tocca, anche in più riquadri.
- Doppio clic per aprire.
- Un secondo clic lento su un elemento già selezionato lo rinomina. Mentre si scrive il nome, un clic altrove lo conferma.
- Il tasto destro agisce sulla selezione, se l'elemento ne fa parte, altrimenti solo su quell'elemento. Il menu offre Apri, Apri con…, Mostra in Explorer, Sposta in…, Taglia, Copia, Copia percorso, Rinomina, Proprietà e Sposta nel Cestino. Il menu completo di Windows (7-Zip e simili) è per la v0.2.
- **Sposta in…** apre, sopra la bacheca, l'elenco dei riquadri e degli argomenti con i recenti in cima: si scrive una parte del nome e si conferma con un clic o con Invio. È lo stesso elenco che compare con **Invia a Quiver** dal menu «Invia a» di Explorer.
- Dopo un'operazione, accanto al messaggio nella barra di stato ci sono **Annulla** e **Mostra**: la via per tornare indietro e quella per andare a vedere, senza tastiera.
- Mentre si trascina, un'etichetta accanto al puntatore dice cosa si ha in mano; un riquadro trascinato mostra con una riga dove andrà.
- Drag & drop:
  - in entrata, da Explorer, dalla posta (allegati e messaggi di Outlook) o dal browser (link e immagini);
  - tra riquadri e argomenti: tenendo fermo l'elemento sopra un argomento, l'argomento si apre e lo si lascia nel riquadro giusto;
  - in uscita, per esempio per allegare un file a una mail;
  - i riquadri si riordinano trascinandoli per il titolo.
- Spostare, copiare o collegare segue le regole di Explorer: sullo stesso disco si sposta, tra dischi diversi si copia; `Ctrl` copia, `Maiusc` sposta, `Alt` o `Ctrl+Maiusc` crea un collegamento. Con due eccezioni, coerenti con i principi:
  - quello che ha già un suo posto (programmi `.exe`, cartelle e file in rete) entra come collegamento;
  - quello che sta in «Da smistare» viene spostato anche su un altro disco.

## 6. Funzioni e versioni

### v0.1 — MVP: "il desktop non mi serve più"

- Prima configurazione: scelta o creazione della radice dell'archivio.
- Pannello con scorciatoia globale, icona nella tray, avvio con Windows.
- Albero degli argomenti e bacheca con riquadri (disposti in colonne, nell'ordine scelto trascinandoli) e "Generale".
- Icone vere di file, cartelle e programmi, con la data di modifica.
- Apri, Apri con…, mostra in Explorer, Proprietà, ricerca istantanea per nome.
- Selezione come sul desktop, anche di più elementi.
- Crea argomento, riquadro o cartella con il nome pronto da scrivere; rinomina sul posto; sposta nel Cestino.
- Copia, taglia e incolla compatibili con Explorer.
- Drag & drop in entrata (anche da Outlook e dal browser), tra riquadri e argomenti, e in uscita.
- Aggiornamento immediato se l'archivio cambia da Explorer.

### v0.2 — gestione completa

- Inbox: `_Da smistare`, il Desktop e, se si vuole, Download. Ci finiscono anche i collegamenti che gli installer mettono sul desktop, così un programma appena installato si aggiunge con un gesto.
  - **Fatto**: «Da smistare» mostra un riquadro per ogni provenienza, il Desktop comprende quello comune a tutti gli utenti, Download si attiva dalle impostazioni e la ricerca trova anche questi file.
  - **Fatto**: «Sposta in…» dal tasto destro manda la selezione nel riquadro o nell'argomento scelto per nome, con le destinazioni recenti in cima (tenute per PC in `%APPDATA%\Quiver\recenti.json`); «Invia a Quiver» nel menu «Invia a» di Explorer fa lo stesso con i file scelti lì, passandoli all'istanza aperta con `--sort`.
- **Fatto**: annulla (`Ctrl+Z`) per spostamenti, copie, collegamenti, rinomine, nuove cartelle, ordine dei riquadri, promozioni ed eliminazioni. Quello che è andato nel Cestino torna al suo posto.
- Menu contestuale completo di Windows (7-Zip e simili).
- Riquadri con dimensione personalizzata.
  - **Fatto**: il colore, scelto tra gli otto di Brace dal menu del riquadro, come striscia accanto al titolo; sta nei metadati e si annulla.
- **Fatto**: selezione con il rettangolo, trascinando sul vuoto della bacheca, anche attraverso più riquadri; con `Ctrl` si aggiunge.
- **Fatto**: aggiunta di collegamenti a programmi, cartelle esterne e siti:
  - trascinando: i programmi e quello che è in rete diventano collegamenti da soli, con `Alt` qualunque cosa;
  - con «Incolla collegamento» e con «Nuovo collegamento…», scrivendo un sito o un percorso;
  - da Outlook e dal browser arrivano allegati, messaggi e link.

### v0.3 — trasloco e GlazeWM

- **Trasloco dal desktop**: l'app legge la posizione delle icone, raggruppa quelle vicine e propone argomenti e riquadri. Si vede un'anteprima, si conferma e i file vengono spostati; il trasloco si può annullare.
- **Workspace Home**.
- **Argomento ↔ workspace**: se apri il pannello in un workspace collegato, si apre subito il suo argomento. "Apri argomento" porta sul workspace collegato e apre gli elementi segnati come "di avvio".

### Idee per dopo

- Anteprima rapida con `Spazio` e miniature.
- Ricerca dentro i file (con l'indice di Windows o con Everything).
- Recenti e preferiti per argomento.
- Regole automatiche per l'inbox (es. `*.dwg` → Rossi/Disegni).
- Pulsante in Zebar.
- Archivio condiviso tra più PC.

## 7. Integrazione con GlazeWM

- **Il pannello non va affiancato, e GlazeWM lo lascia già stare: non serve nessuna regola.** Verificato il 7 ottobre 2026 su GlazeWM 3.9.1, quello installato, in due modi:
  - nel codice di GlazeWM (`is_manageable` in `packages/wm-platform/src/native_window.rs`): non gestisce le finestre con lo stile `WS_EX_TOOLWINDOW`, né quelle che hanno un proprietario e nessuna barra del titolo;
  - sulla finestra del pannello di Quiver in esecuzione: ha entrambe le caratteristiche.

  La regola `ignore` servirà solo se un giorno il pannello cambierà tipo di finestra. La finestra Home invece dovrà essere gestita da GlazeWM.
- **Focus**: quando chiudi il pannello con `Esc` o con la scorciatoia, Quiver ridà il focus alla finestra in cui stavi lavorando, invece di lasciarlo scegliere a Windows: con GlazeWM potrebbe finire su una finestra di un altro workspace, che verrebbe portato in primo piano. Le finestre nascoste da GlazeWM ("cloaked") sono escluse.
- **Tasti già presi da GlazeWM** nel config attuale: `Alt+Invio` apre cmd, quindi in Quiver le Proprietà si aprono dal tasto destro, finché quel tasto resta a GlazeWM.
- **Scorciatoia**: la registra l'app; `Alt+E` è libera nel config attuale. In alternativa può gestirla GlazeWM con `shell-exec`, lanciando `Quiver.exe --toggle`.
- **Interfaccia di comando (IPC)**: GlazeWM espone un WebSocket su `127.0.0.1:6123` con messaggi JSON. Si possono inviare comandi (es. `focus --workspace 3`), fare interrogazioni (workspace attivo, finestre) e ricevere eventi (cambio di focus, workspace attivato). L'app lo usa per sapere qual è il workspace attivo, per passare al workspace di un argomento prima di aprirlo e, in modalità Home, per tornare al workspace precedente.
- **Workspace Home** (indicativo, da provare): un workspace dedicato con una sua scorciatoia e una regola che ci manda la finestra principale dell'app.

  ```yaml
  workspaces:
    - name: 'home'
  keybindings:
    - commands: ['focus --workspace home']
      bindings: ['alt+0']
  window_rules:
    - commands: ['move --workspace home']
      match:
        - window_process: { equals: 'Quiver' }
          window_title: { equals: 'Quiver Home' }
  ```

## 8. Architettura tecnica

- **Piattaforma**: C# / .NET 10, WPF, Windows 11 x64.
- **Struttura della soluzione**:
  - `Quiver.Core`: la logica, senza interfaccia grafica. Contiene il modello dell'archivio e di «Da smistare», i metadati, le regole per spostare, copiare o collegare, il registro per annullare, il ripristino dal Cestino, l'indice per la ricerca e il controllo delle modifiche fatte da fuori. Si può testare in automatico.
  - `Quiver.App`: l'interfaccia WPF. Contiene finestre (pannello e Home), viste e view model, tray, impostazioni, il collegamento con le funzioni di Windows (icone, operazioni sui file della shell, collegamenti `.lnk`, file trascinati da Outlook e dal browser, menu contestuale, posizione delle icone sul desktop, scorciatoia globale) e il client IPC di GlazeWM.
  - `Quiver.Tests`: i test di `Quiver.Core`.
  - `Quiver.App.Tests`: i test delle operazioni sui file fatte dalla shell e dei view model del pannello e degli archivi, su archivi temporanei.
- **Librerie**: CommunityToolkit.Mvvm (MVVM), H.NotifyIcon (icona nella tray), Velopack (installer), System.Text.Json. L'aspetto viene da Brace (vedi "Aspetto"). Le poche chiamate alle API di Windows per ora sono scritte a mano; CsWin32 resta candidata per quando l'integrazione crescerà.
- **Istanza unica**: se l'app viene lanciata una seconda volta (`Quiver.exe --toggle`, o `--sort` con i file di «Invia a Quiver»), passa il comando e i percorsi all'istanza già aperta tramite una named pipe, una riga per ciascuno, e si chiude.
- **Movimento**: il contenuto del pannello e le domande sopra la bacheca entrano con una dissolvenza e uno scorrimento di pochi pixel, con le durate e le curve di Brace (`Views/Motion.cs`); niente si muove se Windows ha le animazioni disattivate. Durante un trascinamento due adorner disegnano l'etichetta che segue il puntatore e la riga dove andrà un riquadro.
- **Operazioni sui file**: passano tutte dalla shell di Windows, con le sue finestre di avanzamento e di conflitto, e usano il Cestino. Ognuna lascia un passo nel registro per annullarla, con quello che serve a rifarla al contrario, metadati compresi: dove sono finiti gli elementi, come si chiamavano, quando sono andati nel Cestino.
- **Ripristino dal Cestino**: il Cestino di ogni disco tiene ogni elemento come due file, `$R…` con il contenuto e `$I…` con il percorso originale e la data.
  - Subito dopo un'eliminazione, Quiver annota quali `$I…` sono i suoi.
  - `Ctrl+Z` rimette al loro posto esattamente quegli elementi, anche se nel frattempo ne sono stati eliminati altri con lo stesso percorso, e toglie i `$I…`, come fa «Ripristina».
  - Provato due volte sul Cestino vero, con un file e una cartella di prova; i test automatici usano un Cestino finto.
- **Annullare senza perdere dati**: un annullamento non sovrascrive mai niente. Elimina definitivamente solo una cartella vuota creata da Quiver; il resto passa dal Cestino e, sui dischi che non ce l'hanno, l'annullamento non si fa. Se un file è aperto in un altro programma, il passo resta e `Ctrl+Z` si può ripetere.
- **Installazione**: l'installer è fatto con Velopack (`scripts\New-Installer.ps1`, strumento `vpk` nel manifesto `dotnet-tools.json`).
  - Installa per l'utente, senza amministratore, in `%LOCALAPPDATA%\Quiver`, con il collegamento nel menu Start. Sul Desktop non mette niente.
  - Dopo l'installazione Quiver parte con Windows; alla disinstallazione l'avvio automatico si toglie.
  - Il runtime .NET 10 Desktop, se manca, lo scarica l'installer.
  - `Program.Main` fa passare prima Velopack, che durante installazione e disinstallazione chiude il processo prima di qualunque finestra.
  - Le release stanno su GitHub, con le note in `novita\<versione>.md`. Velopack potrà anche aggiornare Quiver da lì; con il repository privato serve un token, quindi gli aggiornamenti automatici sono rimandati.
  - Lo script pubblica solo da un commit già su GitHub, senza modifiche fuori dal commit, e lega il tag della release a quel commit: l'installer e il codice della release coincidono.
- **Ricarica**: a ogni apertura del pannello e a ogni modifica vista dal watcher, la bacheca viene riletta. I view model di argomenti, riquadri ed elementi ancora presenti restano gli stessi, aggiornati sul posto (`Reconciler`): selezione, nome in modifica, rami aperti e focus sopravvivono, e la vista riceve solo gli inserimenti, gli spostamenti e le rimozioni.
- **Icone di Windows**: programmi, collegamenti e file senza un'icona di Brace chiedono la loro icona a Windows su un thread dedicato, perché un collegamento a una risorsa di rete che non risponde può bloccare per secondi; nel frattempo mostrano il documento di Brace. Nelle anteprime da riga di comando le icone si leggono subito, sullo stesso thread.
- **Obiettivi di prestazioni**: pannello visibile in meno di 100 ms dalla scorciatoia (la finestra è già creata e solo nascosta); ricerca su 50.000 file in meno di 50 ms; meno di 100 MB di memoria a riposo.
- **Convenzioni**: codice e commenti in inglese; interfaccia in italiano, con i testi in file di risorse per eventuali traduzioni.

## 9. Rischi e punti aperti

- **File spostati ad app chiusa**: all'avvio i metadati vanno riallineati.
- **Cartelle di rete e OneDrive**: il controllo delle modifiche è meno affidabile, e sulle cartelle di rete il Cestino non esiste. Prima di eliminare lì serve una conferma esplicita.
- **Focus**: Windows limita le app che si portano in primo piano da sole. Una scorciatoia registrata dall'app lo consente, ma va provato bene con GlazeWM e con più monitor.
- **Archivio su più PC**: possibili conflitti su `archive.json` e programmi installati in percorsi diversi. Per ora è rimandato.
- **Installer non firmato**: Windows avvisa al primo avvio (SmartScreen). Una firma del codice costa e per ora non serve.
- **Da decidere**: se rinominare la cartella `aaa` in `Quiver`; se includere Download nell'inbox (per ora è un'impostazione, spenta). Dove mettere l'archivio lo sceglie l'utente nella finestra Archivi; il consiglio è lo stesso disco del Desktop.

## 10. Prossimi passi

Fatto:
- la soluzione .NET 10 con il repository git, il prototipo del pannello, la v0.1 e l'interfaccia rifatta con Brace;
- della v0.2: `Ctrl+Z`, il Desktop in «Da smistare», i collegamenti (anche «Nuovo collegamento…»), i file trascinati da Outlook e dal browser;
- la prova dal vivo dei gesti del pannello, su un archivio di prova: ha trovato e fatto correggere il pannello che si nascondeva durante un trascinamento;
- le icone di Brace per i tipi di file, gli archivi come i vault di Obsidian, l'installer e la release 0.2.0 su GitHub;
- con GlazeWM: verificato che il pannello non ha bisogno della regola `ignore`, e che la finestra Archivi resta libera perché non si ridimensiona;
- dopo una lettura completa del codice (7 ottobre 2026): le icone di Windows su un thread dedicato, la ricarica a ogni apertura del pannello, il salvataggio atomico delle impostazioni e la ricarica che riconcilia i view model invece di sostituirli;
- l'8 ottobre 2026, con il mouse al primo posto: «Sposta in…» dal tasto destro, «Invia a Quiver» in Explorer, i pulsanti Annulla e Mostra nella barra di stato, la comparsa animata del pannello e delle domande, la riga e l'etichetta durante i trascinamenti. Tutto verificato con i test e con le anteprime, non ancora dal vivo;
- sempre l'8 ottobre: la finestra Impostazioni, che assorbe quella degli archivi, con la scorciatoia registrata premendola e provata subito, gli interruttori di Da smistare e quelli di avvio ed Explorer; poi il colore dei riquadri e la selezione a rettangolo. Tutto pubblicato come pre-release 0.2.1.

1. Installare dall'installer, creare l'archivio e cominciare a svuotare il Desktop, pochi file alla volta, con «Sposta in…» e «Invia a Quiver».
2. Provare dal vivo le impostazioni, i gesti nuovi e l'installer, poi Quiver con GlazeWM: focus, più monitor.
3. Della v0.2 restano il menu completo di Windows e la dimensione dei riquadri; poi la v0.3 con GlazeWM. Gli aggiornamenti automatici restano per dopo.
