# Quiver — documento di progetto

*Stato: v0.1 completa nel codice, da provare nell'uso quotidiano e con GlazeWM. Ultimo aggiornamento: 7 ottobre 2026.*

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

### Struttura

La radice è configurabile (es. `D:\Archivio`):

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

Le impostazioni del singolo PC (percorso dell'archivio, scorciatoia, sorgenti dell'inbox, tema) stanno in `%APPDATA%\Quiver\settings.json`, fuori dall'archivio.

## 5. Interfaccia

```
┌──────────────┬─────────────────────────────────────────┐
│ Cerca...     │ Lavoro › Clienti › Rossi                │
│              │ ┌─ Documenti ──────┐ ┌─ Programmi ────┐ │
│ ▾ Lavoro     │ │ Offerta_v3.docx  │ │ SolidWorks     │ │
│   ▾ Clienti  │ │ Commessa 2026\   │ │ TeamViewer     │ │
│     ● Rossi  │ └──────────────────┘ └────────────────┘ │
│     ○ Bianchi│ ┌─ Generale ───────┐                    │
│   ○ Interni  │ │ Note.md          │                    │
│ ○ Personale  │ └──────────────────┘                    │
│ ○ Strumenti  │                                         │
│ Da smistare 4│                                         │
└──────────────┴─────────────────────────────────────────┘
```

- **Barra laterale**: ricerca, albero degli argomenti, inbox con il numero di file da smistare.
- **Bacheca**: il percorso dell'argomento selezionato e i suoi riquadri.

### Due modalità, stessa interfaccia

- **Pannello** (prima versione): con la scorciatoia compare sopra le finestre, sul monitor attivo, con il cursore già nella casella di ricerca. Quando apri un elemento il pannello sparisce e il file si apre nel workspace in cui stavi lavorando. Si chiude con `Esc` o con un clic fuori.
- **Workspace Home** (versione successiva): l'app resta aperta a schermo intero in un workspace dedicato di GlazeWM. Quando apri un elemento da qui, l'app torna prima al workspace in cui stavi lavorando (o a quello collegato all'argomento) e lo apre lì.

### Tastiera

| Tasto | Azione |
|---|---|
| scorciatoia globale (proposta: `Alt+E`) | apre/chiude il pannello |
| scrivere | cerca in tutto l'archivio |
| frecce, `Tab` | spostarsi tra argomenti, riquadri ed elementi |
| `Invio` | apri |
| `Ctrl+Invio` | apri in Explorer la cartella che lo contiene |
| `F2` | rinomina |
| `Canc` | sposta nel Cestino |
| `Ctrl+Z` | annulla l'ultima operazione |
| `Ctrl+Shift+C` | copia il percorso |
| `Esc` | cancella la ricerca, poi chiude il pannello |

### Mouse

- Doppio clic per aprire, come sul desktop (si può impostare il clic singolo).
- Il tasto destro mostra il menu di Windows (Apri con, Proprietà, 7-Zip…) più le azioni dell'app (Sposta in…, Promuovi ad argomento…).
- Drag & drop: in entrata (da Explorer, dalla posta, dal browser), tra riquadri e argomenti, e in uscita (per esempio per allegare un file a una mail).

## 6. Funzioni e versioni

### v0.1 — MVP: "il desktop non mi serve più"

- Prima configurazione: scelta o creazione della radice dell'archivio.
- Pannello con scorciatoia globale, icona nella tray, avvio con Windows.
- Albero degli argomenti e bacheca con riquadri (disposti automaticamente) e "Generale".
- Icone vere di file, cartelle e programmi.
- Apri, apri la cartella che lo contiene, ricerca istantanea per nome.
- Crea argomento o riquadro, rinomina, sposta nel Cestino.
- Drag & drop in entrata e tra riquadri.
- Aggiornamento immediato se l'archivio cambia da Explorer.

### v0.2 — gestione completa

- Inbox: `_Da smistare`, il Desktop e, se si vuole, Download. Con un tasto un file va nel riquadro giusto; le destinazioni recenti compaiono in cima. Ci finiscono anche i collegamenti che gli installer mettono sul desktop, così un programma appena installato si aggiunge con un gesto.
- Annulla (`Ctrl+Z`) per spostamenti, rinomine ed eliminazioni.
- Menu contestuale di Windows.
- Drag & drop in uscita.
- Riquadri con posizione, dimensione e colore personalizzati.
- Aggiunta di collegamenti a programmi, cartelle esterne e siti.

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

- **Il pannello non va affiancato.** Nella regola `ignore` già presente in `config.yaml` va aggiunta la finestra del pannello (solo quella: la finestra Home invece deve essere gestita da GlazeWM). Prima va verificato se serve davvero, perché dipende da come GlazeWM tratta una finestra senza bordi sempre in primo piano.

  ```yaml
  window_rules:
    - commands: ['ignore']
      match:
        - window_process: { equals: 'zebar' }
        # ...regole esistenti...
        - window_process: { equals: 'Quiver' }
          window_title: { equals: 'Quiver' }
  ```

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
  - `Quiver.Core`: la logica, senza interfaccia grafica. Contiene il modello dell'archivio, i metadati, le operazioni sui file con il registro per annullarle, l'indice per la ricerca e il controllo delle modifiche fatte da fuori. Si può testare in automatico.
  - `Quiver.App`: l'interfaccia WPF. Contiene finestre (pannello e Home), viste e view model, tray, impostazioni, il collegamento con le funzioni di Windows (icone, menu contestuale, collegamenti, posizione delle icone sul desktop, scorciatoia globale) e il client IPC di GlazeWM.
  - `Quiver.Tests`: i test di `Quiver.Core`.
- **Librerie**: CommunityToolkit.Mvvm (MVVM), H.NotifyIcon (icona nella tray), System.Text.Json. Le poche chiamate alle API di Windows per ora sono scritte a mano; WPF UI (stile Windows 11, Mica) e CsWin32 restano candidate per quando l'interfaccia e l'integrazione cresceranno.
- **Istanza unica**: se l'app viene lanciata una seconda volta (`Quiver.exe --toggle`), passa il comando all'istanza già aperta tramite una named pipe e si chiude.
- **Operazioni sui file**: passano tutte da un unico servizio, che le registra per poterle annullare e usa il Cestino di Windows.
- **Obiettivi di prestazioni**: pannello visibile in meno di 100 ms dalla scorciatoia (la finestra è già creata e solo nascosta); ricerca su 50.000 file in meno di 50 ms; meno di 100 MB di memoria a riposo.
- **Convenzioni**: codice e commenti in inglese; interfaccia in italiano, con i testi in file di risorse per eventuali traduzioni.

## 9. Rischi e punti aperti

- **File spostati ad app chiusa**: all'avvio i metadati vanno riallineati.
- **Cartelle di rete e OneDrive**: il controllo delle modifiche è meno affidabile, e sulle cartelle di rete il Cestino non esiste. Prima di eliminare lì serve una conferma esplicita.
- **Focus**: Windows limita le app che si portano in primo piano da sole. Una scorciatoia registrata dall'app lo consente, ma va provato bene con GlazeWM e con più monitor.
- **Archivio su più PC**: possibili conflitti su `archive.json` e programmi installati in percorsi diversi. Per ora è rimandato.
- **Da decidere**: il nome definitivo (e rinominare la cartella `aaa`); dove mettere la radice (disco locale o OneDrive); clic singolo o doppio; se includere Download nell'inbox.

## 10. Prossimi passi

1. Creare la soluzione .NET 10 (`Quiver.Core`, `Quiver.App`, `Quiver.Tests`) e il repository git.
2. **Prototipo del pannello**: scorciatoia → finestra senza bordi sopra GlazeWM → contenuto vero di una cartella con le icone → apertura degli elementi. Serve a verificare subito i punti delicati: focus, GlazeWM e velocità.
3. Completare l'MVP (v0.1) e usarlo davvero per qualche giorno prima di andare avanti.
