# Quiver — documento di progetto

*Stato: versione 0.3.2, release su GitHub. Interfaccia su Brace, gesti del desktop provati dal vivo su un archivio di prova, `Ctrl+Z`, il Desktop in «Da smistare», i collegamenti, gli archivi gestiti come i vault di Obsidian. Dalla 0.2.1 anche «Sposta in…», «Invia a Quiver», le impostazioni nell'app, il colore dei riquadri e la selezione a rettangolo. Dalla 0.2.2 la finestra è normale, con la barra del titolo di Brace e il pin «Sempre in primo piano»; con la 0.2.2 il Desktop vero è stato svuotato nell'archivio. Dalla 0.2.3 le operazioni sui file in un thread a parte, la misura e l'ordine dei riquadri, gli argomenti trascinabili. Dalla 0.3.0 la tela (fasi 0 e 1), i colori degli argomenti, la barra di stato come in Explorer, gli avvisi e il controllo dello spazio su disco; dalla 0.3.1 la barra degli argomenti richiudibile. Da provare nell'uso quotidiano e con GlazeWM. Ultimo aggiornamento: 10 ottobre 2026.*

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
    "Lavoro":                         { "color": "#5DA9FF", "autoColor": true },
    "Personale":                      { "color": "none" },
    "Lavoro/Clienti":                 { "kind": "topic", "order": 0 },
    "Lavoro/Clienti/Rossi":           { "kind": "topic", "workspace": "3" },
    "Lavoro/Clienti/Bianchi":         { "kind": "topic" },
    "Lavoro/Interni":                 { "kind": "topic", "order": 1 },
    "Lavoro/Clienti/Rossi/Documenti": { "size": { "columns": 2, "rows": 12 }, "sort": { "key": "modified", "descending": true } },
    "Lavoro/Clienti/Rossi/Programmi": { "order": 0, "color": "#5DA9FF", "items": ["SolidWorks.lnk", "TeamViewer.lnk"] }
  }
}
```

- `"kind": "topic"` serve solo sotto il primo livello, perché le cartelle nella radice sono già argomenti.
- `"order"`: il posto di un riquadro nella sua bacheca, o di un argomento tra quelli accanto, dato trascinandolo. Chi non ce l'ha segue in ordine di nome.
- `"size"`: le colonne della bacheca che il riquadro occupa (da 1 a 3) e le righe che mostra prima di scorrere (da 2 a 40; senza, segue i suoi elementi fino a circa dieci).
- `"sort"`: come sono ordinati gli elementi del riquadro: per nome (`name`, quello di sempre, che non ha bisogno di voce), data di modifica (`modified`), tipo (`type`) o dimensione (`size`), ognuno nei due versi. Le cartelle vengono prima, come in Explorer.
- `"items"`: l'ordine dato agli elementi trascinandoli, per nome. Quelli arrivati dopo seguono; un elemento rinominato da Quiver tiene il suo posto. Ordine a mano e `"sort"` si sostituiscono a vicenda.
- `"color"`: il colore di un riquadro o di un argomento. Un argomento della radice lo riceve da Quiver la prima volta che lo vede, il meno usato dagli altri, con `"autoColor": true`; scelto a mano, il segno non c'è. `"none"` è un argomento lasciato senza colore apposta, che non ne riceve più. Un sotto-argomento senza un colore scelto per lui usa quello del suo argomento, e un riquadro non mostra il colore dato da Quiver quando era un argomento. Una versione di Quiver che non conosce `"autoColor"` lo perde al primo salvataggio e tiene il colore, come se fosse scelto.
- Il riquadro «Generale» usa la voce del suo argomento, perché ne contiene i file.
- Se una cartella viene rinominata o spostata dall'app, i metadati la seguono subito. Se succede da Explorer, i metadati restano sotto il nome di prima: la cartella perde posizione, misura e colore, mai i file.
- Un `archive.json` rovinato, che non è più JSON valido, viene messo da parte com'era, in `archive.json.corrupt`, e l'archivio riparte con i metadati vuoti: i file non cambiano.

Le impostazioni del singolo PC (archivio in uso ed elenco degli archivi, scorciatoia, sorgenti dell'inbox) stanno in `%APPDATA%\Quiver\settings.json`, fuori dall'archivio, e si cambiano dalla finestra **Impostazioni**: le pagine sono Archivi, Scorciatoia, Da smistare e Avvio ed Explorer, e ogni cambiamento vale subito. La scorciatoia si registra premendola in un campo: Quiver la prova all'istante e la tiene solo se Windows gliela dà. Anche un `settings.json` rovinato viene messo da parte, in `settings.json.corrupt`, e Quiver riparte dalle impostazioni predefinite.

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
- **Barra laterale**: l'albero degli argomenti, con + per crearne uno, e in fondo l'inbox con il numero di file da smistare. È la navigazione principale, non un albero di file: righe da 32 px, nomi da 14 px (quelli del primo livello un po' più marcati) e le icone da 20 px che Brace dà alla navigazione. Quello scelto ha lo sfondo `accent-subtle` e la sua icona piena in `accent`, perché Brace tiene le icone piene per la selezione della navigazione principale. Si chiude e si riapre con il pulsante accanto al nome di Quiver, nella barra del titolo, o con `Ctrl+B`: chiusa non occupa spazio, e Quiver ricorda com'era (dalla 0.3.1).
- **Bacheca**: il percorso e il nome dell'argomento, il pulsante **Nuovo riquadro**, i sotto-argomenti come pulsanti e i riquadri in colonne, larghe almeno 280 px. Ogni riquadro ha il titolo, il numero di elementi e il menu …; ogni elemento ha la sua icona e la data di modifica. Un riquadro può occupare fino a tre colonne: se non ci sta in quello che resta della riga, va a capo.
- **Barra di stato**, come in Explorer e come br-statusbar di Brace: a sinistra l'archivio, quanti elementi contiene e quanto occupano, oppure l'operazione in corso con i LED di br-activity; a destra la selezione e quanto pesa, lo spazio libero sul disco dell'archivio (in giallo sotto il 10 % o sotto 5 GB) e **?** con i tasti da sapere.
- **Avvisi** (br-toast di Brace), in basso a destra: l'esito di un'operazione con **Annulla** e **Mostra**, e i problemi. Gli esiti si chiudono da soli dopo sei secondi, non mentre il puntatore è sopra; i problemi restano finché non si chiudono, e vanno anche nel log.

### Aspetto

- **Brace**, il design system personale, dà colori (tema scuro), caratteri (Inter, Cascadia Mono), icone (Fluent) e componenti. La sua versione WPF è copiata in `src/Quiver.App/Brace` e non si modifica lì: le correzioni vanno fatte in Brace, così valgono per tutti i software che lo usano.
- **Icone dei file**: cartelle e tipi comuni hanno l'icona di Brace del loro concetto, dal suo dizionario «un concetto, un'icona». Ci sono PDF, Word, fogli di calcolo, presentazioni, immagini, video, audio, archivi compressi, codice, posta, pagine web e modelli 3D. Le icone sono monocromatiche come tutte quelle di Brace; quelle che mancavano sono state aggiunte a Brace stesso. I file che nessun programma del PC apre hanno il documento di Brace. I programmi, i collegamenti e i file dei programmi installati tengono l'icona di Windows, che per loro è un logo.
- **Icona**: una bacheca con tre riquadri, uno dei quali ambra come un riquadro selezionato. È disegnata in `scripts/genera-icona.py`; il logo nella barra del titolo è lo stesso disegno, in `Themes/Quiver.xaml`.
- **Barra del titolo** (dalla 0.2.2): la disegna Quiver, come `br-titlebar` dell'AppShell di Brace, perché Brace per WPF non ha ancora la sua.
  - Alta `titlebar-height` (32 px), su `surface-base`: il logo e il nome, il pulsante della barra degli argomenti, la ricerca al centro (280 × 24), l'archivio come badge, il pin, poi riduci, ingrandisci e chiudi, larghi 46 px e alti quanto la barra. Chiudi diventa `danger` al passaggio, con l'icona in `text-on-accent`.
  - Si trascina dove non ci sono controlli, e il doppio clic ingrandisce, come chiede Brace.
  - Il pin è quello di Pulse su Brace: un `Brace.IconButton` da 24 px che, attivo, ha l'icona in `accent` su `accent-subtle`, con l'icona `Pin16` normale in tutti e due gli stati. Dice «Sempre in primo piano: attivo» o «disattivato». C'è anche nel menu della tray, come in Hive e Pulse.
  - Il badge dell'archivio è neutro, perché la barra tiene il suo unico ambra per il pin attivo.
  - L'icona per ripristinare una finestra ingrandita manca in Brace: è disegnata in `Themes/Quiver.xaml` e va portata in Brace, insieme a una barra del titolo per WPF.
  - La finestra Impostazioni ha la stessa barra, con il solo pulsante chiudi.

### Due modalità, stessa interfaccia

- **Finestra** (dalla 0.2.2): con la scorciatoia Quiver viene davanti, con il cursore nella casella di ricerca; se è già davanti, la scorciatoia lo rimette nell'area di notifica. È una finestra normale con una barra del titolo di Brace: si sposta, si ridimensiona, si ingrandisce, compare nella barra delle applicazioni e in Alt+Tab, e non si chiude da sola quando si clicca altrove. Il pin la tiene sopra le altre finestre, per trascinarci dentro i file da Explorer. La X, `Esc` e la scorciatoia la rimettono nell'area di notifica. Ricorda posizione, dimensione e pin.
  - Fino alla 0.2.1 era un pannello che compariva al centro del monitor attivo e si nascondeva con un clic fuori. Con il mouse non si poteva spostare, né usarlo insieme a Explorer: per questo è cambiato.
- **Workspace Home** (versione successiva): l'app resta aperta a schermo intero in un workspace dedicato di GlazeWM. Quando apri un elemento da qui, l'app torna prima al workspace in cui stavi lavorando (o a quello collegato all'argomento) e lo apre lì.

### Tastiera

| Tasto | Azione |
|---|---|
| `Alt+E` (scorciatoia globale) | porta Quiver davanti, o lo rimette nell'area di notifica se è già davanti |
| scrivere | cerca in tutto l'archivio |
| frecce, `Tab` | spostarsi tra argomenti, riquadri ed elementi |
| `Invio` | apre (anche più elementi insieme) |
| `Ctrl+Invio` | mostra l'elemento in Explorer |
| `F2` | rinomina sul posto |
| `Canc` | sposta nel Cestino |
| `Ctrl+C`, `Ctrl+X`, `Ctrl+V` | copia, taglia, incolla, anche con Explorer |
| `Ctrl+A` | seleziona tutto il riquadro |
| `Ctrl+Maiusc+N` | nuovo riquadro |
| `Ctrl+B` | chiude o riapre la barra degli argomenti |
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
- Dopo un'operazione, nel suo avviso ci sono **Annulla** e **Mostra**: la via per tornare indietro e quella per andare a vedere, senza tastiera.
- Mentre si trascina, un'etichetta accanto al puntatore dice cosa si ha in mano; un riquadro, una riga o un argomento che cambia posto mostra con una riga dove andrà.
- Drag & drop:
  - in entrata, da Explorer, dalla posta (allegati e messaggi di Outlook) o dal browser (link e immagini);
  - tra riquadri e argomenti: tenendo fermo l'elemento sopra un argomento, l'argomento si apre e lo si lascia nel riquadro giusto;
  - in uscita, per esempio per allegare un file a una mail;
  - i riquadri si riordinano trascinandoli per il titolo; al passaggio del mouse una maniglia a puntini mostra dove prenderli;
  - gli elementi si mettono nell'ordine voluto trascinandoli dentro il loro riquadro;
  - gli argomenti si riordinano trascinandoli sopra o sotto un altro; lasciati al centro di un altro argomento ci entrano come sotto-argomenti, con la loro cartella. Due argomenti con lo stesso nome non si uniscono: Quiver chiede di rinominarne uno.
- L'angolo in basso a destra di un riquadro, al passaggio del mouse, ne cambia la misura: in larghezza scatta a una, due o tre colonne, in altezza a righe. Il doppio clic sull'angolo, o «Misura automatica» nel menu, torna alla misura che segue gli elementi. Le righe sono misurate sullo schermo, non supposte: con la scala al 125 % Windows arrotonda una riga da 22 a 22,4 punti.
- Il menu … del riquadro ha **Ordina per**: nome, data di modifica, tipo o dimensione, crescente o decrescente. Prende il posto dell'ordine dato a mano, e viceversa.
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
- **Fatto**: annulla (`Ctrl+Z`) per spostamenti, copie, collegamenti, rinomine, nuove cartelle, ordine dei riquadri e degli argomenti, ordinamenti, misure, promozioni ed eliminazioni. Quello che è andato nel Cestino torna al suo posto.
- Menu contestuale completo di Windows (7-Zip e simili).
- **Fatto**: riquadri con dimensione personalizzata, dall'angolo: da una a tre colonne e da due a quaranta righe.
  - **Fatto**: il colore, scelto tra gli otto di Brace dal menu del riquadro, come striscia accanto al titolo; sta nei metadati e si annulla.
- **Fatto**: elementi ordinati per nome, data, tipo o dimensione, oppure a mano trascinandoli; argomenti nell'ordine voluto, e dentro altri argomenti, trascinandoli nella barra laterale.
- **Fatto**: selezione con il rettangolo, trascinando sul vuoto della bacheca, anche attraverso più riquadri; con `Ctrl` si aggiunge.
- **Fatto**: aggiunta di collegamenti a programmi, cartelle esterne e siti:
  - trascinando: i programmi e quello che è in rete diventano collegamenti da soli, con `Alt` qualunque cosa;
  - con «Incolla collegamento» e con «Nuovo collegamento…», scrivendo un sito o un percorso;
  - da Outlook e dal browser arrivano allegati, messaggi e link.

### v0.3 — la tela

- **Tela**: tutto l'archivio su una superficie da spostare e zoomare, con un argomento per zona e le zone dove le mette l'utente. Piano in §11.

### v0.4 — trasloco e GlazeWM

- **Trasloco dal desktop**: l'app legge la posizione delle icone, raggruppa quelle vicine e propone argomenti e riquadri. Si vede un'anteprima, si conferma e i file vengono spostati; il trasloco si può annullare. Su questo PC il Desktop è stato svuotato a mano con la 0.2.2.
- **Workspace Home**, con la tela a schermo intero.
- **Argomento ↔ workspace**: se apri il pannello in un workspace collegato, si apre subito il suo argomento. "Apri argomento" porta sul workspace collegato e apre gli elementi segnati come "di avvio".

### Idee per dopo

- Anteprima rapida con `Spazio` e miniature.
- Ricerca dentro i file (con l'indice di Windows o con Everything).
- Recenti e preferiti per argomento.
- Regole automatiche per l'inbox (es. `*.dwg` → Rossi/Disegni).
- Pulsante in Zebar.
- Archivio condiviso tra più PC.

## 7. Integrazione con GlazeWM

- **Dalla 0.2.2 la finestra di Quiver è normale, e GlazeWM la affianca alle altre.** Fino alla 0.2.1 il pannello era una finestra di strumenti (`WS_EX_TOOLWINDOW`), che GlazeWM non gestisce: lo dice `is_manageable` in `packages/wm-platform/src/native_window.rs`, verificato il 7 ottobre 2026 su GlazeWM 3.9.1. Ora non lo è più.
  - Per tenerla libera, sopra le finestre affiancate, basta una regola; nel config attuale le finestre libere non stanno sempre sopra (`shown_on_top: false`), e il pin di Quiver fa la sua parte. Da provare.

    ```yaml
    window_rules:
      - commands: ['set-floating']
        match:
          - window_process: { equals: 'Quiver' }
    ```
- **Focus**: quando nascondi Quiver con `Esc`, la X o la scorciatoia, Quiver ridà il focus alla finestra in cui stavi lavorando, invece di lasciarlo scegliere a Windows: con GlazeWM potrebbe finire su una finestra di un altro workspace, che verrebbe portato in primo piano. Le finestre nascoste da GlazeWM ("cloaked") sono escluse.
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
- **Cornice della finestra**: `WindowChrome` con `CaptionHeight` 32 e senza vetro, come Pulse su Brace: Windows tiene la sua cornice invisibile per i bordi da trascinare, l'ombra, gli angoli piccoli (`DWMWCP_ROUNDSMALL`) e l'aggancio ai lati. Da ingrandita, la cornice resta 8 px fuori dallo schermo (a 100 %): il contenuto rientra di altrettanto, calcolato per lo schermo su cui sta.
  - Costo noto: in Reticle (TacticalUI) `WindowChrome` è stato misurato a 4K su una GPU integrata, con una finestra che scorre immagini: 60 fps senza, 32 con vetro 0, e circa 70 MB di memoria della GPU in più. Reticle 0.6.0 l'ha sostituito con la gestione diretta dei messaggi della cornice. Se Quiver dovesse risultare pesante, quella è la strada.
- **Finestra creata una volta sola**: chiuderla, con la X, Alt+F4 o dalla barra delle applicazioni, la nasconde soltanto; si chiude davvero solo con **Esci** o quando Windows si spegne.
- **Movimento**: il contenuto del pannello e le domande sopra la bacheca entrano con una dissolvenza e uno scorrimento di pochi pixel, con le durate e le curve di Brace (`Views/Motion.cs`); niente si muove se Windows ha le animazioni disattivate. Durante un trascinamento due adorner disegnano l'etichetta che segue il puntatore e la riga dove andrà un riquadro.
- **Operazioni sui file**: passano tutte dalla shell di Windows, con le sue finestre di avanzamento e di conflitto, e usano il Cestino. Ognuna lascia un passo nel registro per annullarla, con quello che serve a rifarla al contrario, metadati compresi: dove sono finiti gli elementi, come si chiamavano, quando sono andati nel Cestino.
  - Spostamenti, copie, eliminazioni e i loro annullamenti girano su un thread STA tutto loro (`FileOperationQueue`), uno dopo l'altro: durante una copia lunga la finestra resta usabile, con i LED di `br-activity` e quello che si sta facendo nella barra di stato. Ognuna aspetta il suo turno e parte dai file come li ha lasciati la precedente; un `Ctrl+Z` durante una copia aspetta che finisca e annulla quella.
  - Le rinomine restano immediate: sono brevi, e il campo del nome aspetta la risposta.
  - Mentre i file arrivano la bacheca non si rilegge e l'indice della ricerca aspetta: tutti e due si aggiornano una volta, alla fine. **Esci** aspetta che finisca l'operazione in corso, per non lasciare un file copiato a metà.
  - A operazione finita la finestra si riprende il focus che le finestre della shell le hanno tolto, ma solo se nel frattempo non si è passati a un altro programma.
  - Fino alla 0.2.2 le operazioni giravano nel thread dell'interfaccia, che restava bloccata finché la shell non finiva.
  - Anche la copia di un archivio su un altro disco (Impostazioni › Archivi › **Sposta…**) gira su un thread a parte, dopo la 0.3.2: intanto Quiver resta usabile, la finestra Impostazioni non si chiude e **Esci** aspetta la fine della copia.
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
- **Convenzioni**: codice e commenti in inglese; interfaccia in italiano. I testi per ora sono scritti nel XAML e nel codice: se servirà tradurli, andranno spostati in file di risorse.

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
- sempre l'8 ottobre: la finestra Impostazioni, che assorbe quella degli archivi, con la scorciatoia registrata premendola e provata subito, gli interruttori di Da smistare e quelli di avvio ed Explorer; poi il colore dei riquadri e la selezione a rettangolo. Tutto pubblicato come pre-release 0.2.1;
- dopo la prima prova dal vivo della 0.2.1, che non si poteva spostare e si chiudeva a ogni clic fuori: la finestra normale, con la barra del titolo di Brace, il pin e la posizione ricordata. Verificata con i test e le anteprime, pubblicata come pre-release 0.2.2;
- svuotando il Desktop con la 0.2.2: le operazioni sui file in un thread a parte, perché la finestra si bloccava durante le copie; la barra laterale come navigazione; gli argomenti trascinabili; la misura dei riquadri dall'angolo; l'ordinamento e l'ordine a mano degli elementi. Verificato con i test (anche sul thread a parte, con un dispatcher come quello della finestra), con le anteprime e sull'archivio vero: letto in sola lettura con il codice della 0.2.2 e con il nuovo, dà gli stessi argomenti, riquadri ed elementi nello stesso ordine. Pubblicato come 0.2.3, la prima release normale: da qui niente più pre-release;
- le fasi 0 e 1 della tela (§11): `MainViewModel` con tutte le bacheche insieme, la vista Tela con una zona per argomento e i sotto-argomenti dentro, lo zoom a tre livelli e i voli dalla barra laterale, dalla ricerca e da «Mostra». Sull'archivio di prova pesante si apre in meno di 2 secondi invece di 14; misurata sul PC vero, va a 55-60 fotogrammi al secondo con l'archivio vero e a 45 con quello pesante. Verificate con i test, con le anteprime e con la misura, non ancora dal vivo;
- subito dopo, su richiesta: i colori degli argomenti, dati una volta da Quiver e tenuti nell'archivio, nella barra laterale e sulla tela; la barra di stato come in Explorer, con la selezione, lo spazio libero e i tasti da sapere sotto **?**; esiti e problemi in avvisi in basso a destra; quanto occupa ogni archivio in Impostazioni, e il controllo dello spazio prima di copiare. Verificato con i test e con le anteprime, non ancora dal vivo;
- l'8 ottobre, dopo la 0.3.0: il tasto destro sugli argomenti di nuovo funzionante e la barra degli argomenti richiudibile con `Ctrl+B` (0.3.1); sulla tela, i nomi degli elementi nei riquadri visti a metà strada (0.3.2);
- il 9 e il 10 ottobre, le correzioni trovate leggendo tutto il codice. Il **?** dice la scorciatoia in uso, non sempre `Alt+E`; un `settings.json` rovinato viene messo da parte invece di andare perso; la copia di un archivio su un altro disco non blocca più Quiver; una copia fatta in una cartella di cui non si leggono i nomi non si annulla più, perché `Ctrl+Z` avrebbe mandato nel Cestino anche quello che c'era già; sulla tela, una cartella nuova nel «Generale» di una zona va nel suo argomento. Verificato con i test, non ancora dal vivo.

1. Provare dal vivo la 0.2.3: le copie lunghe, gli argomenti trascinati, l'angolo dei riquadri, «Ordina per» e l'ordine a mano.
2. Provare dal vivo le impostazioni, i gesti nuovi e l'installer, poi Quiver con GlazeWM: focus, più monitor. E la copia di un archivio su un altro disco, che ora lascia Quiver usabile.
3. Provare dal vivo la tela; poi finire la fase 2: buona parte funziona già, mancano lo spostamento di un riquadro in un'altra zona e il rettangolo con `Maiusc`.
4. Della v0.2 resta il menu completo di Windows; poi la v0.4 con GlazeWM. Gli aggiornamenti automatici restano per dopo.

## 11. Piano: la tela

*Deciso l'8 ottobre 2026. Le scelte: una vista alternativa alla bacheca; zone libere, come su una scrivania; la rotella zooma, come su una mappa; i sotto-argomenti dentro il loro argomento. Le fasi 0 e 1 sono fatte, verificate con i test, con le anteprime e con la misura delle prestazioni sul PC vero; manca la prova dal vivo dei gesti.*

### L'idea

Una superficie unica, da spostare e da zoomare, con tutto l'archivio insieme: la scrivania organizzata dell'idea di partenza. Ogni argomento è una **zona** con dentro i suoi riquadri, gli stessi della bacheca con le loro misure, colori e ordini; i sotto-argomenti sono zone dentro la zona del loro argomento. Anche **Da smistare** è una zona: si trascina da lì a qualunque riquadro dell'archivio senza cambiare argomento.

Le zone stanno dove le mette l'utente, e i posti restano nell'archivio: non saltano quando cambiano la risoluzione o i monitor, come succedeva sul desktop (§1).

### Come si usa

- **Bacheca | Tela**: un interruttore in alto a destra, accanto a «Nuovo riquadro». Quiver ricorda l'ultima vista usata. La barra degli argomenti resta: un clic su un argomento vola sulla sua zona.
- **Zoom**: la rotella zooma attorno al puntatore, dal 10 % al 200 % (al 100 % un riquadro è grande come sulla bacheca). Al livello vicino, sopra un riquadro che scorre la rotella lo fa scorrere; con `Ctrl` zooma sempre. `Ctrl+0` mostra tutto, `+` e `-` zoomano.
- **Spostare la vista**: trascinando sul vuoto, oppure con la rotella premuta, dovunque. Doppio clic sul vuoto: tutto in vista; sul titolo di una zona: la zona inquadrata.
- **Tre livelli**, secondo lo zoom:
  - lontano, sotto il 35 %: le zone con il nome, i riquadri come blocchi, con la striscia del loro colore se ne hanno uno;
  - medio, fino al 70 %: i riquadri con titolo e numero e, sotto, i nomi dei loro elementi in grande, quanti ne entrano; i nomi dei sotto-argomenti;
  - vicino: i riquadri veri, come sulla bacheca: selezione, doppio clic, tasto destro, rinomina, trascinamenti, `Ctrl+Z`.

  Ai livelli lontano e medio un doppio clic su un riquadro ci vola sopra. Tra un livello e l'altro le soglie hanno un po' di margine, perché lo zoom lento non faccia sfarfallare.
- **Selezione a rettangolo**: `Maiusc` + trascinare sul vuoto, perché trascinare sul vuoto sposta la vista.
- **Le zone** si spostano trascinandone il titolo e si allargano a colonne dall'angolo, come i riquadri. Dentro, riquadri e sotto-argomenti si dispongono a righe nelle colonne della zona, come sulla bacheca.
- **Ricerca**: come oggi; un clic su un risultato vola al suo riquadro con l'elemento selezionato, doppio clic o `Invio` lo aprono.
- Per ogni archivio Quiver ricorda dov'era la vista.

### Dove vanno le zone

- La prima volta le dispone Quiver, nell'ordine della barra laterale, con Da smistare in alto a sinistra.
- Appena l'utente sposta una zona, tutte tengono il posto in cui sono: da lì in poi i posti sono suoi.
- Un argomento nuovo trova posto da solo, nel primo spazio libero sotto le zone che ci sono.
- Quando una zona cresce (un riquadro nuovo, uno più alto) e arriva su un'altra, quella sotto scende quanto basta. Le zone non si coprono mai.
- Nei metadati, per ogni argomento della radice e per Da smistare: `"canvas": { "x": 0, "y": 0, "columns": 3 }`, in punti della tela al 100 %. Per la tela non si sposta nessun file; spostare e allargare una zona si annulla con `Ctrl+Z`.

### Come è fatta

- **Core**: `CanvasPlacement` sistema le zone, dati i posti salvati e le misure delle zone: dove va una zona nuova, quali scendono quando una cresce. È geometria pura, con i suoi test.
- **App**:
  - la tela sta dentro `PanelView`, accanto alla bacheca: riquadri, menu e trascinamenti sono gli stessi, con lo stesso codice, e il modello del riquadro diventa una risorsa condivisa;
  - `MainViewModel` oggi ragiona su una bacheca alla volta (selezione, appunti, rinomina, «Mostra», selezione dopo una ricarica): deve poterne gestire tante insieme. È il lavoro più delicato, quindi viene per primo, senza niente di visibile e con i test che ci sono;
  - `CanvasViewModel`: le zone (argomento, riquadri, zone dentro, posto, colonne), la vista (centro e zoom) e il livello;
  - la superficie: una sola trasformazione per zoom e spostamento, i voli animati con le durate e le curve di Brace, il testo in modalità Ideal perché resti nitido quando è scalato;
  - le anteprime da riga di comando imparano `--canvas` e `--zoom`, per controllare la tela senza aprire finestre.
- **Prestazioni**: misurate già nella fase 1. Su un archivio di prova pesante (24 argomenti, 192 riquadri, 4800 elementi) la prima tela, che creava subito tutti i riquadri con tutte le righe, si apriva in 14 secondi. Una riga da sola costa circa un millisecondo e mezzo, e nessuna sua parte pesa più delle altre: il rimedio è non creare le righe che non si vedono. Così ora:
  - la lista di un riquadro nasce senza righe, ma già alta quanto le sue righe: le crea quando il riquadro è in vista al livello vicino e la vista sta ferma, poche alla volta tra un fotogramma e l'altro, e svegliandosi non sposta niente. La virtualizzazione di WPF non va bene, perché la selezione è legata ai view model (`IsSelected`);
  - il livello arriva a tutto quello che sta sulla tela come proprietà ereditata (`CanvasSurface.Level`): migliaia di binding che cercavano la vista risalendo l'albero si risolvevano uno alla volta, e le zone cambiavano misura 155 volte prima di fermarsi;
  - il menu di un riquadro si crea quando si apre, non per ogni riquadro (era un secondo su 192 riquadri);
  - le zone si dispongono di nuovo solo quando cambia la misura di qualcuna.

  La stessa tela ora si apre in meno di 2 secondi, processo compreso, e la bacheca è passata da 1,5 a 1,1.

  Sul PC vero (schermo da 3492 × 1902 pixel a 125 %, accelerazione grafica di WPF attiva), con `--benchmark-canvas`, che muove la tela per otto secondi sempre sullo stesso percorso:
  - l'archivio vero (13 riquadri) si apre in 0,6 secondi e va a 55-57 fotogrammi al secondo; qualche fotogramma da 33 ms quando si zooma da vicino, perché WPF ridisegna i testi a ogni scala;
  - l'archivio pesante si apre in 2 secondi. All'inizio andava a 33 fotogrammi al secondo, con un blocco di oltre mezzo secondo passando al livello vicino: il cambio di livello scendeva in tutte le righe, e le righe si creavano mentre la vista si muoveva. Ora dentro un riquadro il livello resta fermo, e le righe arrivano quando la vista sta ferma da 120 ms: 45 fotogrammi al secondo, 60 spostandosi da vicino, il peggiore da 59 ms.

  Se servirà ancora: da lontano le zone diventano immagini (`BitmapCache`).

### Fasi

0. **Preparazione**, senza niente di nuovo da vedere: `MainViewModel` con più bacheche insieme, il modello del riquadro condiviso. Tutti i test di oggi devono passare.
1. **La tela da guardare**: l'interruttore, le zone disposte da Quiver (Da smistare compreso, i sotto-argomenti dentro), zoom e spostamento, i tre livelli, i voli dalla barra laterale e dalla ricerca, aprire e tasto destro al livello vicino. Più la misura delle prestazioni.
2. **Smistare sulla tela**: trascinare tra riquadri di argomenti diversi e dalla zona Da smistare, il rettangolo con `Maiusc`, gli appunti, la rinomina, `Ctrl+Z`, le righe che mostrano dove va un riquadro o un elemento. Da vicino buona parte funziona già, perché i riquadri della tela sono quelli della bacheca; mancano lo spostamento di un riquadro in un'altra zona e il rettangolo.
3. **La tua scrivania**: zone da spostare e allargare, i posti nell'archivio, i nuovi argomenti sistemati da soli, le zone che scendono invece di coprirsi, la vista ricordata.
4. **Dopo**: la minimappa, la tastiera tra le zone e la modalità Home a schermo intero per il workspace di GlazeWM, con la tela dentro (§7). Il colore delle zone, previsto qui, è arrivato prima, con la 0.3.0.

Ogni fase si chiude con i test e le anteprime e, prima di pubblicare, con la prova che l'archivio vero si legge come prima.

### Rischi

- **Prestazioni a 4K** su una GPU integrata: per questo la misura è nella prima fase, prima di costruirci sopra.
- **Il passaggio a più bacheche** tocca il cuore di `MainViewModel`: i test di oggi fanno da rete, e la fase 0 non cambia niente di visibile.
- **Gesti del mouse**: trascinare sul vuoto sposta la vista e il rettangolo vuole `Maiusc`. Va provato dal vivo se viene naturale.
- **Compatibilità**: le versioni senza la tela non conoscono `"canvas"`. Tornando indietro, i posti delle zone si perderebbero alla prima modifica dei metadati; i file mai.
