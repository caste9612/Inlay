# La tela

Tutto l'archivio su una superficie da spostare e zoomare, con una zona per argomento: nell'interfaccia è **Mosaic** (fino alla 0.3.3 «Tela»). Come si usa è nel [README](../README.md#uso) (voce **Mosaico**); l'idea, le scelte e il piano a fasi sono in [PROGETTO.md §11](../PROGETTO.md#11-piano-la-tela). Qui c'è com'è fatta.

## A che punto è

| Fase | Contenuto | Stato |
|---|---|---|
| 0 | `MainViewModel` con tutte le bacheche insieme | fatta (0.3.0) |
| 1 | la tela da guardare: interruttore, zone, zoom, tre livelli, voli, riquadri veri da vicino, misura delle prestazioni | fatta (0.3.0); i nomi degli elementi a metà strada con la 0.3.2 |
| 2 | smistare sulla tela: trascinare tra zone diverse, appunti, rinomina, `Ctrl+Z`, il rettangolo con `Shift` | in parte: vedi sotto |
| 3 | la tua scrivania: zone da spostare e allargare, posti salvati nei metadati (`"canvas"`), vista ricordata per archivio | da fare |
| 4 | minimappa, tastiera tra le zone, modalità Home per GlazeWM | da fare |

Nel codice di oggi, quindi:

- le zone le dispone sempre Inlay: nessuna ha un posto fissato (`ZoneBox` senza `X` e `Y`), e `FolderMetadata` non ha ancora `"canvas"`;
- da vicino i riquadri della tela sono quelli della bacheca, quindi buona parte della fase 2 c'è già: file e cartelle trascinati su un riquadro di qualunque zona ci arrivano con le solite regole (anche da lontano, sulla sua scheda), quelli lasciati sulla cornice di una zona vanno nel suo argomento, e appunti, rinomina e `Ctrl+Z` funzionano. Un riquadro lasciato su un riquadro di un'altra zona invece non fa niente (`PlanDrop`): spostarlo in un altro argomento è quello che manca, insieme al rettangolo;
- `Shift` più trascinare sul vuoto non sposta la vista, ma non disegna ancora il rettangolo: la selezione a rettangolo c'è solo sulla bacheca;
- la vista non si ricorda tra un avvio e l'altro: la prima volta che la tela ha una misura, per ogni archivio, si mostra tutta (`_canvasFitFor` in `PanelView`). Si ricorda solo quale vista era in uso (`AppSettings.View`).

## I dati: zone e riquadri

- **`MainViewModel.IsCanvasShown`** sceglie la vista. L'interruttore «Board | Mosaic» è un `ListBox` legato a `ViewIndex`. In cima alla tela, la didascalia «Whole vault», il titolo «Mosaic», lo zoom («57%», con i pulsanti per allontanare e avvicinare) e **Show all**. Passando da una vista all'altra, un nome in modifica viene confermato (`OnIsCanvasShownChanging`).
- **`ReadCanvas`** legge la bacheca di ogni argomento: una zona per argomento della radice, con dentro le zone dei sotto-argomenti. `Reconciler` tiene i view model di zone, riquadri ed elementi che ci sono ancora, così selezione e nomi in modifica sopravvivono. Si fa a ogni ricarica, ma solo mentre la tela è in vista.
- **Le operazioni lavorano su tutte le bacheche insieme**: `ShownTiles` sono i riquadri della bacheca o quelli di tutte le zone, e da lì passano selezione, appunti, rinomina, `Ctrl+Z` e «Show». `SiblingsOf(tile)` dà i riquadri accanto, sulla bacheca o nella zona, per spostare un riquadro.
- **`CanvasViewModel`**: le zone, la camera e il livello, più le ricerche che servono alla vista (`AllZones`, `AllTiles`, `ZoneOf`, `ZoneWith`, `TileOf`). Quando cambia la camera, ricalcola il livello.
- **`ZoneViewModel`**: titolo, riepilogo («3 tiles · 120 items», sotto-zone comprese), colore del suo argomento, riquadri, zone dentro, e `Bounds`, il posto che `ZoneCanvasPanel` dà alle zone della radice. Oggi `Bounds` non lo legge nessuno: i voli misurano la cornice della zona con `CanvasSurface.BoundsOf`. Le colonne sono quante ne servono ai riquadri o alle zone dentro, da 1 a 3 (`UsualColumns`), così una zona piccola resta piccola.
- **`TileViewModel.Preview`**: i nomi dei primi 12 elementi, uno per riga, per la scheda che rappresenta il riquadro a metà strada.

## La camera (`CanvasCamera`)

Un `record struct` con lo zoom e `Origin`, il punto della tela che sta nell'angolo in alto a sinistra della vista. A zoom 1 un punto della tela è un punto dello schermo, e un riquadro è grande come sulla bacheca.

- Zoom da 0,1 a 2 (`MinZoom`, `MaxZoom`).
- `ZoomAt(screen, factor)` zooma tenendo fermo il punto sotto il puntatore; `Pan(delta)` sposta la vista seguendo il puntatore.
- `Fit(area, view)` inquadra un'area con un margine di 48 punti, senza mai avvicinarsi oltre il 100 %: una zona piccola non viene ingrandita.
- `Toward(target, progress, view)` dà la camera a metà di un volo: lo zoom cambia in proporzione costante e il centro della vista si muove in linea retta.
- **I livelli** (`LevelFrom`): lontano sotto il 35 %, medio fino al 70 %, vicino oltre. Tra un livello e l'altro c'è un margine di 3 punti percentuali in tutti e due i versi, perché uno zoom lento non faccia sfarfallare.

`CanvasCamera` sta tra i view model e i suoi test (`CanvasCameraTests`) non hanno bisogno di finestre.

## La superficie (`CanvasSurface`)

Un `Border` che contiene `ZonesControl`, le zone, e le mostra attraverso la camera, a cui è legato in tutti e due i versi.

- **Una sola trasformazione**: la camera diventa una `MatrixTransform` sul contenuto. Il contenuto viene misurato senza limiti, e la camera decide cosa se ne vede.
- **Il livello arriva a tutto per eredità**: `CanvasSurface.Level` è una proprietà ereditata. Gli stili `NearOnly`, `AfarOnly` e `NotFarthest` di `PanelView.xaml` mostrano o nascondono le parti dei riquadri secondo il livello; le zone hanno i loro trigger sul livello scritti nel loro modello (intestazione, tinta, etichetta). Dentro un riquadro visto da vicino il livello resta fisso a `Near`, così un cambio di livello non scende in tutte le righe. Prima migliaia di binding cercavano la superficie risalendo l'albero, e le zone cambiavano misura 155 volte prima di fermarsi.
- **I nomi visti da lontano** restano grandi uguali a ogni zoom: `CanvasSurface.LabelScale`, anche lei ereditata, è una sola `ScaleTransform` che vale 1/zoom. La camera la cambia a ogni fotogramma e le zone non si ridispongono: si rimisurano solo le etichette, perché la loro larghezza massima segue la scala.
- **Il mouse**: la rotella zooma attorno al puntatore del 15 % a scatto, tranne sopra un riquadro che può ancora scorrere (con `Ctrl` zooma sempre). Il tasto centrale sposta la vista da dovunque; il sinistro dove niente prende il clic (`IsEmptySpot`: non su un riquadro vero, un pulsante, un campo, una barra o una riga), quindi da lontano e a metà strada anche sulle schede dei riquadri. Il doppio clic sul vuoto mostra tutto.
- **I voli** (`FlyTo`): 240 ms con la curva standard di Brace, su `CompositionTarget.Rendering`. Con le animazioni di Windows spente la camera salta subito al punto d'arrivo.
- **`Show(area)`**, **`FitAll()`**, **`ZoomBy(factor)`** e **`BoundsOf(element)`** sono quello che usa `PanelView`.
- Il testo delle zone è in modalità `Ideal` (`TextOptions.TextFormattingMode`), perché resti nitido quando è scalato; nel resto del pannello è `Display`.

### Le righe create quando servono

Creare tutte le righe di tutti i riquadri di un archivio grande costava secondi (una riga circa 1,5 ms). Così:

- `PanelView.xaml` mette `TileList.StartsAsleep` sulla superficie della tela, ed è ereditata: ogni lista di un riquadro sulla tela nasce **addormentata**, senza righe (`ItemsSource` vuoto), ma già alta quanto le sue righe (numero di elementi per l'altezza di una riga). Quando si sveglia, intorno non si sposta niente.
- Ogni lista addormentata si fa conoscere dalla superficie alla prima misura (`Sleeping`).
- Quando il livello è vicino e la camera sta ferma da 120 ms, `WakeInView` sveglia le liste in vista e quelle subito intorno (la vista allargata di un quarto per lato), le più vicine al centro per prime, per al massimo 12 ms; le altre dopo il fotogramma, se la camera non si è mossa.
- `WakeInViewNow` le sveglia tutte subito, per le anteprime. `SleepingCount` dice quante dormono ancora, per la misura delle prestazioni.
- La virtualizzazione di WPF non va bene qui, perché la selezione è legata ai view model (`IsSelected`) e deve valere anche per le righe che non si vedono.

## La disposizione

- **`ZoneCanvasPanel`** misura le zone e chiede a `CanvasPlacement` ([archivio.md](archivio.md#le-altre-utilità)) dove metterle. La larghezza delle righe è quella che, vista tutta in uno schermo 16:9, mostra le zone più grandi (`BestRowWidth`), con 96 punti tra una zona e l'altra, lo spazio dove da lontano sta il nome. Le zone non si coprono mai. Le posizioni si ricalcolano solo quando cambia la misura di qualche zona, e le zone della radice ricevono il loro posto in `Bounds`.
- **`TileGridPanel`** dispone i riquadri in colonne, come sulla bacheca. Sulla bacheca le colonne sono quante ne entrano (almeno 280 punti l'una); in una zona sono fisse (`FixedColumns`). Un riquadro può occupare più colonne e, se non ci sta nel resto della riga, va a capo; ognuno tiene la sua altezza.
- **`CoverPanel`**: il primo figlio decide la misura, gli altri gli stanno sopra. Sulla tela il riquadro vero resta sotto, nascosto ma con la sua misura, e la scheda vista da lontano lo copre: zoomando le zone non si muovono mai.

## Cosa si vede a ogni livello

| Parte | Lontano | Medio | Vicino |
|---|---|---|---|
| nome della zona della radice | etichetta sopra l'angolo, solo il nome | etichetta sopra l'angolo, con il riepilogo | intestazione dentro la zona |
| nome di una zona interna | nascosto | intestazione dentro la zona | intestazione dentro la zona |
| tinta del colore dell'argomento | sì (8 %) | sì (8 %) | no |
| riquadro | scheda vuota, con la striscia del suo colore se ne ha uno | scheda con titolo, numero e nomi degli elementi, e la stessa striscia | il riquadro della bacheca, con tutti i suoi gesti |

## I gesti in `PanelView`

- Scegliere un argomento nella barra laterale vola alla sua zona (`ShowZone`); un clic su quello già scelto no.
- Il doppio clic sul nome di una zona la inquadra; quello sulla scheda di un riquadro ci vola sopra (`ShowTile`).
- Il tasto destro sul nome di una zona, l'intestazione da vicino o l'etichetta da lontano, apre `TopicMenu`, il menu dell'argomento: `Zone_ContextMenuOpening` gli dà come `DataContext` l'argomento della zona, e i gestori sono quelli della barra laterale.
- **New space**, al posto di **New tile** della bacheca, crea un argomento nella radice: il mosaico è fatto di argomenti, la bacheca di riquadri. Il nome di un argomento si scrive nella barra laterale: `NewTopic` e **Rename** la aprono se è chiusa. Dopo un argomento nuovo la vista vola sulla sua zona; dopo un riquadro nuovo (dal menu di una zona o con `Ctrl+Shift+N`) vola sul riquadro, perché il campo del nome si vede solo da vicino.
- Un clic su un risultato della ricerca, appena è chiaro che non è un doppio clic, chiude la ricerca, seleziona l'elemento e vola al suo riquadro; un file dentro una cartella di un riquadro si mostra come quella cartella. Il doppio clic invece apre.
- **Show** su un avviso vola al riquadro dove sono finiti gli elementi.
- `Ctrl+0` (o **Show all**), `Ctrl++`, `Ctrl+-` e i pulsanti accanto alla percentuale.
- Da vicino i riquadri sono gli stessi della bacheca (`TileTemplate`), con gli stessi gestori: selezione, menu, rinomina, trascinamenti, angolo.
- Per le anteprime da riga di comando, `FrameCanvas(zoom, topic)` inquadra tutto, o la zona di un argomento, allo zoom chiesto, e sveglia le righe in vista.

## Le prestazioni

Le misure e la loro storia sono in [PROGETTO.md §11, Prestazioni](../PROGETTO.md#come-è-fatta). In breve: sul PC vero, con uno schermo 4K a 125 %, l'archivio vero si apre in 0,6 secondi e va a 55-57 fotogrammi al secondo; quello di prova pesante (24 argomenti, 192 riquadri, 4.800 elementi) si apre in 2 secondi e va a 45. Per misurarle c'è `--benchmark-canvas` ([test.md](test.md#verificare-linterfaccia-senza-aprirla)). Se servirà ancora, il passo successivo è fare delle zone viste da lontano delle immagini (`BitmapCache`).
