# PRD — Sistema di Gestione e Monitoraggio Viaggi Trasporto Merci

*Product Requirements Document*

| Campo | Valore |
|---|---|
| **Versione** | Draft |
| **Cliente** | Priamus Ecologica S.r.l. |
| **Data** | Maggio 2026 |
| **Stato** | Draft |

---

## 1. Executive Summary

Il presente documento descrive i requisiti funzionali e non funzionali del sistema di gestione e monitoraggio dei viaggi di trasporto merci destinato a Priamus Ecologica S.r.l. Il sistema ha lo scopo di sostituire e digitalizzare l'attuale processo manuale di registrazione dei viaggi, con due obiettivi principali: rendere semplice e veloce per gli autisti la registrazione dei viaggi effettuati, e fornire alla segreteria un'interfaccia di consultazione e calcolo che restituisca per ogni cliente il totale economico fatturabile, calcolato secondo regole tariffarie complesse e storicizzate.

Il sistema sarà sviluppato come Progressive Web App (PWA) installabile su smartphone in modalità BYOD per gli autisti, e come applicazione web per la segreteria/amministrazione. Le modalità di pagamento supportate sono quattro (a ore, a peso, a km, a viaggio) per coprire l'intera gamma di accordi commerciali in essere con i clienti di Priamus.

---

## 2. Obiettivi e Non-Obiettivi

### 2.1 Obiettivi (in scope MVP)

- Permettere agli autisti di registrare in autonomia i viaggi effettuati direttamente dal proprio smartphone, in modo rapido e con il minimo numero di tap.
- Permettere all'autista di selezionare la motrice utilizzata e, se la motrice richiede un rimorchio, anche il rimorchio agganciato, in modo che la composizione effettiva del mezzo sia tracciata sul viaggio.
- Permettere alla segreteria di gestire le anagrafiche di clienti, autisti, mezzi, luoghi e tariffe, con storicizzazione delle tariffe nel tempo.
- Calcolare automaticamente il totale fatturabile di ogni viaggio in base a una matrice tariffaria che tenga conto di: cliente, luogo di partenza, luogo di consegna, mezzo utilizzato, modalità di pagamento, fascia oraria (diurna/notturna), minimo fatturabile e scaglioni.
- Fornire alla segreteria una vista filtrabile dei viaggi per cliente, autista, mezzo, periodo, con totali aggregati e capacità di esportazione in Excel/PDF.
- Permettere agli autisti di allegare la documentazione richiesta dal contratto del singolo cliente: DDT, bolla, formulario rifiuti (FIR), riepilogo di fine giornata.
- Segnalare alla segreteria eventuali anomalie nei viaggi (es. mancanza di allegati obbligatori o riepilogo di fine giornata mancante).

### 2.2 Non-obiettivi (out of scope MVP)

- Generazione e invio delle fatture: il sistema produce il dato fatturabile, ma la fattura è prodotta dal gestionale contabile esistente del cliente.
- Tracciamento GPS dei mezzi e calcolo automatico di chilometri o tempi di percorrenza.
- Funzionamento offline dell'app autista. Il sistema richiede connessione attiva al momento del salvataggio del viaggio.
- Riconoscimento ottico (OCR) o validazione dei contenuti dei documenti allegati. Gli allegati sono trattati come semplici file.
- Gestione di mezzi noleggiati occasionalmente non presenti in anagrafica (rimandato a evoluzioni future).
- App per il cliente finale o accesso esterno.

---

## 3. Personas e Permessi

### 3.1 Autista

Conducente di un mezzo aziendale. Utilizza il proprio smartphone (BYOD) con la PWA installata. Si autentica una volta a inizio giornata e mantiene la sessione attiva. Inserisce i viaggi effettuati durante o al termine della giornata. Non gestisce anagrafiche, non vede dati economici di altri autisti né tariffe.

### 3.2 Segreteria

Personale amministrativo che gestisce le anagrafiche, configura le tariffe, monitora le anomalie e prepara il riepilogo dei viaggi da fatturare. Lavora da PC desktop con interfaccia di backoffice.

### 3.3 Admin

Utente con privilegi pieni di configurazione del sistema. In azienda probabilmente coincide con un responsabile amministrativo o con il referente IT. Ha tutte le capacità della segreteria più la gestione utenti, ruoli e configurazioni di sistema.

### 3.4 Matrice permessi

| Funzionalità | Autista | Segreteria | Admin |
|---|---|---|---|
| Inserire / modificare i propri viaggi (entro finestra di edit) | ✓ | — | — |
| Selezionare la motrice e l'eventuale rimorchio agganciato | ✓ | — | — |
| Allegare documenti (DDT, bolla, FIR) ai propri viaggi | ✓ | — | — |
| Compilare il riepilogo di fine giornata | ✓ | — | — |
| Visualizzare elenco viaggi e dashboard anomalie | Solo i propri | Tutti | Tutti |
| Correggere viaggi (incl. quelli di altri autisti) | Solo i propri, finestra ristretta | Tutti, con audit log | Tutti, con audit log |
| Gestire anagrafica clienti | — | ✓ | ✓ |
| Gestire anagrafica autisti | — | ✓ | ✓ |
| Gestire anagrafica mezzi (motrici, rimorchi, autonomi) | — | ✓ | ✓ |
| Configurare contratti, tariffe e regole di fatturazione | — | ✓ | ✓ |
| Esportare dati in Excel/PDF | — | ✓ | ✓ |
| Gestire utenti, ruoli e configurazioni di sistema | — | — | ✓ |

---

## 4. User Stories Principali

### 4.1 Autista

- **US-01 —** Come autista, voglio autenticarmi rapidamente sullo smartphone in modo da essere automaticamente riconosciuto come esecutore dei viaggi che registrerò.
- **US-02 —** Come autista, voglio inserire un viaggio appena concluso selezionando mezzo, cliente, luoghi e i dati necessari al calcolo (es. ore, peso), in modo da non dimenticare nulla.
- **US-03 —** Come autista, voglio inserire a fine giornata più viaggi in sequenza, in modo da poterli rendicontare tutti insieme.
- **US-04 —** Come autista, voglio poter correggere un viaggio appena inserito (entro un intervallo limitato), in modo da poter rimediare a errori di digitazione.
- **US-05 —** Come autista, voglio vedere la lista dei miei viaggi recenti, in modo da verificare di aver registrato tutto.
- **US-11 —** Come autista, voglio selezionare il mezzo che sto utilizzando in cima al flusso di inserimento, perché spesso uso lo stesso mezzo per più viaggi nella giornata.
- **US-11bis —** Come autista, quando seleziono una motrice che richiede un rimorchio, voglio essere obbligato dall'app a selezionare anche il rimorchio agganciato, in modo che la composizione effettiva del mezzo risulti correttamente sul viaggio.
- **US-14 —** Come autista, voglio essere guidato dall'app a fornire i documenti obbligatori (DDT, bolla, formulario rifiuti) richiesti per quel cliente, in modo da non rischiare di salvare un viaggio incompleto.
- **US-15 —** Come autista, voglio poter scattare una foto del DDT, della bolla o del formulario rifiuti direttamente dall'app e allegarla al viaggio, senza dover passare per altre app.
- **US-16 —** Come autista, voglio inserire a fine giornata un riepilogo della giornata con un allegato (quando richiesto dal cliente), in modo da chiudere ufficialmente la giornata di lavoro.

### 4.2 Segreteria

- **US-06 —** Come addetto di segreteria, voglio gestire l'anagrafica clienti inserendo i dati fiscali, le condizioni contrattuali e la modalità di pagamento applicata, con data di validità.
- **US-07 —** Come addetto di segreteria, voglio modificare la tariffa di un cliente creando una nuova versione con decorrenza futura, mantenendo la storica per i viaggi già effettuati.
- **US-08 —** Come addetto di segreteria, voglio visualizzare tutti i viaggi di un cliente in un intervallo di date, con il totale economico di ciascuno, in modo da preparare la fatturazione.
- **US-09 —** Come addetto di segreteria, voglio esportare in Excel o in PDF il riepilogo dei viaggi filtrati, per lavorarli esternamente in foglio di calcolo o per archiviarli/inviarli come documento stampabile.
- **US-10 —** Come addetto di segreteria, voglio correggere un viaggio inserito da un autista se rilevo un errore, mantenendo tracciabilità della modifica.
- **US-12 —** Come addetto di segreteria, voglio gestire l'anagrafica dei mezzi (targa, descrizione, tipologia di mezzo — motrice/rimorchio/autonomo — e per le motrici l'obbligo o meno di rimorchio agganciato), in modo che siano disponibili per la selezione coerente da parte degli autisti.
- **US-13 —** Come addetto di segreteria, voglio filtrare i viaggi per mezzo (motrice e/o rimorchio), in modo da analizzare l'utilizzo della flotta o preparare reportistica per cliente che richiede dettaglio mezzo.
- **US-17 —** Come addetto di segreteria, voglio configurare per ogni cliente il livello di fatturazione (forfettario, per luogo di consegna, per coppia partenza-consegna) e il sistema deve adattarsi di conseguenza nei flussi di inserimento e calcolo.
- **US-18 —** Come addetto di segreteria, voglio configurare un minimo fatturabile e regole a scaglioni per i clienti che lo richiedono, in modo che il calcolo del fatturato rispetti gli accordi commerciali.
- **US-19 —** Come addetto di segreteria, voglio configurare tariffe specifiche per determinati mezzi (con tariffe diurne e notturne separate quando il contratto prevede maggiorazione notturna), in modo che l'utilizzo di quel mezzo modifichi automaticamente il calcolo in tutte le fasce orarie.
- **US-20 —** Come addetto di segreteria, voglio configurare la fascia oraria notturna e le maggiorazioni associate, in modo che il sistema rilevi automaticamente i viaggi notturni e applichi le tariffe corrette.
- **US-21 —** Come addetto di segreteria, voglio vedere in una dashboard le anomalie operative (viaggi senza allegati obbligatori, autisti senza riepilogo di fine giornata), per poter intervenire tempestivamente.

---

## 5. Modello del Dominio

### 5.1 Entità principali

| Entità | Descrizione |
|---|---|
| Autista | Utente del sistema con ruolo "autista". Associato ai viaggi che inserisce e ai riepiloghi di fine giornata che compila. |
| Cliente | Soggetto per cui si effettua la consegna. Contiene dati anagrafici, fiscali e il riferimento al contratto attivo (che a sua volta definisce le regole di fatturazione). |
| Contratto cliente | Insieme delle regole di fatturazione configurate per un cliente, valide in un determinato intervallo temporale. Definisce: livello di fatturazione (forfettario, per luogo di consegna, per coppia di luoghi), obbligatorietà di luoghi, allegati richiesti (DDT, bolla, formulario rifiuti, riepilogo fine giornata), minimo fatturabile, scaglioni, gestione viaggio notturno. Un cliente può avere più contratti storici, uno solo attivo a una data. |
| Luogo | Indirizzo o destinazione utilizzabile come luogo di partenza o di consegna. Può essere associato a un cliente (luoghi tipici di quel cliente) o generale. Per i clienti con contratto a livello di luogo costituisce un'entità anagrafica obbligatoria; per gli altri è opzionale. |
| Mezzo | Veicolo della flotta aziendale. Identificato da targa univoca, descrizione/modello, tipologia (motrice, rimorchio, autonomo) e stato (attivo/dismesso). Per le motrici un flag aggiuntivo indica se richiedono il rimorchio agganciato in fase di registrazione viaggio. I mezzi dismessi non sono più selezionabili sui nuovi viaggi ma rimangono referenziati dai viaggi storici. |
| Tariffa | Configurazione economica applicabile a un viaggio. Multidimensionale: definita per cliente con possibili specializzazioni opzionali per luogo di partenza, luogo di consegna, mezzo. Ogni tariffa è una matrice tariffaria completa che include valore diurno e (quando applicabile) valore notturno + prezzo di chiamata notturna. Storicizzata con date di validità. |
| Viaggio | Singola registrazione effettuata dall'autista. Ha autista, motrice utilizzata, eventuale rimorchio agganciato, cliente, data, luoghi (se richiesti), parametri di calcolo (ore, peso, km), eventuali allegati. Salva uno snapshot della tariffa applicata e dell'importo calcolato per garantire immutabilità storica. |
| Allegato | File (immagine o PDF) caricato dall'autista. Associato a un viaggio (DDT, bolla, formulario rifiuti) o a un riepilogo di fine giornata. Contiene metadati di tipo, dimensione, data caricamento. |
| Riepilogo fine giornata | Documento di chiusura della giornata operativa compilato dall'autista, con allegato obbligatorio. È unico per autista e per giornata operativa, indipendentemente dal numero di clienti coinvolti, ed è richiesto quando almeno uno dei clienti per cui l'autista ha registrato viaggi nella giornata prevede l'obbligo di riepilogo a contratto. La sua mancanza al raggiungimento dell'orario di cut-off configurato genera anomalia. |
| Anomalia | Segnalazione di stato inconsistente che richiede attenzione della segreteria. Tipologie: viaggio senza DDT obbligatorio, viaggio senza bolla obbligatoria, viaggio senza formulario rifiuti obbligatorio, autista senza riepilogo di fine giornata per giornata con viaggi attivi. |

### 5.2 Tipologia mezzi e composizione del viaggio

La flotta aziendale comprende veicoli di natura diversa che, ai fini della registrazione del viaggio, devono essere modellati esplicitamente. Ogni mezzo presente in anagrafica appartiene a una delle tre tipologie seguenti, mutuamente esclusive:

- **Motrice —** Veicolo trattore che può essere utilizzato da solo o in abbinamento a un rimorchio. Ogni motrice ha un flag che indica se richiede il rimorchio in fase di registrazione viaggio. Il flag è una caratteristica del mezzo (non del cliente né del contratto) e riflette la realtà operativa: alcune motrici lavorano sempre con rimorchio agganciato, altre lavorano sia con sia senza.
- **Rimorchio —** Veicolo trainato, non può essere selezionato come mezzo principale del viaggio. Compare come selezione complementare quando l'autista sceglie come mezzo del viaggio una motrice che richiede rimorchio.
- **Autonomo —** Veicolo che non richiede né prevede rimorchio agganciato (es. autospurgo, gru mobile, furgone). Si seleziona come unico mezzo del viaggio.

In fase di registrazione di un nuovo viaggio l'autista seleziona un mezzo principale (una motrice o un autonomo). Se il mezzo selezionato è una motrice con flag "richiede rimorchio" attivo, l'app richiede tassativamente la selezione di un rimorchio dalla lista dei rimorchi attivi e non consente il salvataggio finché questa selezione non è effettuata. La coppia (motrice, rimorchio) è salvata sul viaggio come due riferimenti distinti, in modo che entrambe le targhe siano disponibili per la consultazione e la reportistica.

> **Effetto sul calcolo tariffario:** *la tariffa del viaggio resta determinata dalla motrice (o dal mezzo autonomo). Il rimorchio agganciato è informazione di tracciabilità e non influenza, nel MVP, la matrice tariffaria selezionata. Eventuali esigenze di tariffazione differenziata in funzione del rimorchio sono rimandate a evoluzioni future.*

### 5.3 Storicizzazione tariffe

Le tariffe sono uno degli aspetti più delicati del sistema. Ogni cambiamento di tariffa non deve mai impattare i viaggi già effettuati e calcolati. Per garantire questo principio si adottano due meccanismi complementari.

- **Validità temporale:** ogni tariffa ha data inizio validità e data fine validità. La data fine è nullabile (tariffa attualmente in vigore = "tariffa aperta").
- **Snapshot sul viaggio:** al momento del salvataggio del viaggio il sistema denormalizza sul record stesso i valori della tariffa applicata (tipo, prezzo unitario, parametri, eventuali maggiorazioni e minimi). Questo garantisce che l'importo storico rimanga invariato anche in caso di modifiche retroattive alle anagrafiche.

### 5.4 Livello di fatturazione del contratto

Il contratto di ogni cliente specifica a quale livello di dettaglio è impostata la fatturazione. Dal livello scelto dipende: l'obbligatorietà dei luoghi nel viaggio, la struttura della tariffa, il dettaglio del riepilogo per la fatturazione.

| Livello | Significato | Luogo partenza | Luogo consegna |
|---|---|---|---|
| Forfettario | Tariffa unica a livello cliente, indipendente dai luoghi. | Opzionale | Opzionale |
| Per luogo di consegna | Tariffa specifica per ogni luogo di consegna, indipendente dalla partenza. | Opzionale | Obbligatorio |
| Per luogo di partenza | Tariffa specifica per ogni luogo di partenza, indipendente dalla consegna. | Obbligatorio | Opzionale |
| Per coppia (partenza, consegna) | Tariffa specifica per ogni combinazione di luogo di partenza e luogo di consegna. | Obbligatorio | Obbligatorio |

Quando un luogo è obbligatorio per il livello di fatturazione del contratto, il sistema lo richiede tassativamente in fase di inserimento del viaggio e non consente il salvataggio se mancante.

### 5.5 Modalità di pagamento — tipologie

Il sistema supporta quattro modalità di pagamento, che coprono l'intera gamma di accordi commerciali tipici nel settore del trasporto rifiuti e merci. Ogni modalità determina l'unità di misura della tariffa, l'input richiesto all'autista in fase di registrazione del viaggio, e la formula di calcolo dell'importo. La modalità di pagamento è impostata a livello di contratto cliente.

| Tipo | Parametri tariffa | Input richiesto al viaggio | Formula base |
|---|---|---|---|
| A ore | Prezzo orario diurno (€/h), prezzo orario notturno (€/h, opzionale), chiamata notturna (€, opzionale) | Orari di inizio e fine viaggio (le ore vengono calcolate automaticamente) | ore × €/h applicabile + eventuale chiamata notturna |
| A peso | Prezzo a peso diurno (€/ton), prezzo a peso notturno (€/ton, opzionale), chiamata notturna (€, opzionale) | Peso trasportato (in tonnellate) e orario di inizio viaggio (per la fascia) | ton × €/ton applicabile + eventuale chiamata notturna |
| A km | Prezzo al km diurno (€/km), prezzo al km notturno (€/km, opzionale), chiamata notturna (€, opzionale) | Chilometri percorsi (inseriti manualmente dall'autista) e orario di inizio viaggio | km × €/km applicabile + eventuale chiamata notturna |
| A viaggio | Prezzo a viaggio diurno (€), prezzo a viaggio notturno (€, opzionale), chiamata notturna (€, opzionale) | Solo orario di inizio viaggio (per determinare la fascia) | Tariffa fissa applicabile + eventuale chiamata notturna |

**Note operative**

- **Modalità "A km" — origine dei chilometri:** nel MVP, i km sono inseriti manualmente dall'autista a fine viaggio. Il calcolo automatico tramite servizi di mappa (Google Maps, OpenStreetMap) è considerato evoluzione futura per le complessità di integrazione, costi ricorrenti e accuratezza per veicoli industriali.
- **Modalità "A viaggio" — minimo non applicabile:** essendo la tariffa già un valore fisso, le configurazioni di minimo fatturabile e scaglione con eccesso (vedi §5.6) non si applicano a questa modalità. Il sistema in fase di configurazione contratto nasconde automaticamente la sezione corrispondente.
- **Notturno applicabile a tutte le modalità:** la maggiorazione notturna è una scelta indipendente del contratto e si applica a tutte e quattro le modalità di pagamento. Quando attivata, ogni modalità prevede una tariffa diurna, una tariffa notturna e una chiamata notturna additiva (vedi §5.8).
- **Estensibilità:** l'architettura del modello tariffario consente l'aggiunta di ulteriori modalità in fasi future (es. tariffe miste, a colli, a passaggio) senza riprogettazione strutturale, grazie alla separazione tra calcolo base specifico della modalità e applicazione delle regole comuni (notturno, minimo, override mezzo).

### 5.6 Minimo fatturabile e scaglioni

Il contratto del cliente può prevedere un minimo fatturabile e un meccanismo a scaglioni per il calcolo dell'importo. Le configurazioni possibili sono tre, mutuamente esclusive.

- **Nessun minimo:** l'importo è calcolato applicando la formula base senza vincoli.
- **Minimo fisso:** se l'importo calcolato dalla formula base è inferiore a un valore soglia M, il viaggio è fatturato come M. Esempio: tariffa 10 €/h, minimo 30 €. Un viaggio di 2 ore (calcolato 20 €) è fatturato 30 €.
- **Scaglione con eccesso:** fino a una soglia X (in unità della modalità di pagamento) si paga un importo fisso M; oltre la soglia X si somma un costo Y per ogni unità eccedente. Formula: importo = M + max(0, parametro − X) × Y. Esempio: minimo 50 € fino a 4 ore, oltre le 4 ore si sommano 12 €/h sull'eccedenza. Un viaggio di 6 ore costa 50 + (6 − 4) × 12 = 74 €. Un viaggio di 3 ore costa 50 €.

La configurazione del minimo/scaglione è impostata a livello di tariffa (può quindi variare per cliente, per luogo, per mezzo, ecc., a seconda del livello di fatturazione).

**Eccezione per la modalità "A viaggio".** Le regole di minimo fatturabile e scaglione con eccesso non sono applicabili alla modalità "A viaggio", poiché in quella modalità la tariffa è già un valore fisso. Il sistema, in fase di configurazione del contratto, nasconde la sezione corrispondente quando la modalità di pagamento selezionata è "A viaggio". Eventuali clienti che richiedono comportamenti tipo "tariffa minima sotto le X ore + tariffa standard altrimenti" devono essere configurati con modalità "A ore" e regola di scaglione con eccesso, non con modalità "A viaggio" più minimo.

### 5.7 Tariffe specifiche per mezzo (override)

Alcuni mezzi della flotta possono avere una tariffa specifica diversa da quella standard del cliente. Tipico esempio nel settore: autospurghi, gru mobili, walking floor o altri mezzi premium che giustificano una remunerazione differenziata. Per modellare correttamente questa esigenza, l'override tariffario per mezzo è gestito come una matrice tariffaria completa parallela a quella standard, non come un singolo valore.

**Principi guida del modello**

- **Override completo, non parziale:** quando per un cliente è configurato un override per uno specifico mezzo, tale configurazione è una tariffa completa con tutti i campi del modello standard (prezzo diurno, prezzo notturno, prezzo chiamata notturna, eventuale minimo o scaglione). Non è un singolo valore che sostituisce la tariffa di base.
- **Selezione binaria:** il sistema prima decide quale matrice tariffaria utilizzare (standard del contratto oppure override del mezzo). Una volta scelta la matrice, applica al suo interno la tariffa appropriata in base alla fascia oraria. Le due decisioni sono indipendenti e in cascata.
- **Configurabilità per singolo mezzo:** ogni override si applica a un mezzo specifico identificato da targa. Sono configurabili override solo su motrici e mezzi autonomi, non su rimorchi (i rimorchi non determinano la tariffa). Non sono previste categorie di mezzi nel MVP.
- **Coerenza con il contratto:** i campi notturni dell'override (prezzo notturno + chiamata notturna) sono richiesti solo quando il contratto base prevede la maggiorazione notturna. In caso contrario, l'override contiene solo il valore diurno. La modalità di pagamento del contratto (a ore, a peso, a km, a viaggio) non condiziona questa regola: l'override è supportato per tutte le modalità.

**Algoritmo di calcolo**

Dato un viaggio con cliente, mezzo principale (motrice o autonomo) e fascia oraria identificata, il sistema applica i seguenti passi nell'ordine:

1. **Selezione matrice tariffaria:** se esiste un override per il mezzo principale del viaggio configurato sul cliente/contratto, utilizza la matrice override; altrimenti utilizza la matrice standard del contratto.
2. **Selezione tariffa nella fascia:** dalla matrice scelta al passo 1, seleziona il valore diurno o il valore notturno in base alla fascia oraria del viaggio (calcolata automaticamente dagli orari).
3. **Calcolo base:** applica la formula della modalità di pagamento (es. ore × prezzo orario) usando la tariffa selezionata.
4. **Maggiorazioni notturne additive:** se il viaggio è notturno, somma il prezzo di chiamata notturna preso dalla stessa matrice scelta al passo 1.
5. **Applicazione minimo/scaglione:** applica eventuale minimo fisso o scaglione con eccesso configurato sulla matrice selezionata al passo 1.

### 5.8 Viaggio notturno

Il sistema deve gestire le maggiorazioni per viaggi effettuati in fascia oraria notturna. La fascia notturna è configurabile a livello di sistema (parametro di default) o, se richiesto da contratto, a livello di singolo cliente. Il valore tipico è 22:00-06:00 ma può variare.

Il riconoscimento del viaggio notturno è automatico ed è basato sull'orario di inizio del viaggio: un viaggio è considerato notturno se l'orario di inizio cade nella fascia notturna configurata. La durata del viaggio e l'orario di fine non incidono sulla classificazione. Questa regola si applica in modo uniforme a tutte e quattro le modalità di pagamento (a ore, a peso, a km, a viaggio). Per i viaggi notturni si applicano due maggiorazioni cumulative quando previsto dal contratto:

- **Prezzo di chiamata notturno:** importo fisso aggiunto sempre al viaggio notturno, indipendentemente dalla modalità di pagamento. Il valore applicato proviene dalla matrice tariffaria selezionata (standard o override mezzo, vedi §5.7).
- **Tariffa notturna specifica:** se la tariffa contiene un prezzo notturno specifico, questo sostituisce il prezzo diurno per il calcolo. La maggiorazione si applica all'intero viaggio (semplificazione MVP) o alle sole ore in fascia notturna (configurazione avanzata, da concordare).

### 5.9 Principi di funzionamento del calcolo

Indipendentemente dalle scelte tecniche di implementazione, il sistema deve rispettare alcuni principi di comportamento che hanno effetto diretto sulla correttezza e sulla coerenza dei dati prodotti. Questi principi non sono dettagli implementativi ma requisiti di qualità funzionale del prodotto consegnato.

- **Calcolo lato server.** Il calcolo dell'importo di un viaggio avviene sempre sul backend al momento del salvataggio. L'app autista può mostrare una stima per scopi informativi, ma il valore autoritativo è quello calcolato dal server: il client non può imporre un importo arbitrario.
- **Snapshot della tariffa sul viaggio.** Al momento del salvataggio, il viaggio conserva una copia denormalizzata dei valori della tariffa applicata (matrice scelta, valori diurni e notturni, parametri, minimi, maggiorazioni). Una modifica successiva alle anagrafiche tariffarie non altera l'importo dei viaggi già salvati. Eventuali ricalcoli devono essere operazioni esplicite e tracciate.
- **Motore di calcolo unico.** Lo stesso componente di calcolo è utilizzato sia in fase di salvataggio del viaggio sia in fase di simulazione (ad esempio quando la segreteria vuole verificare la configurazione di un nuovo contratto). Questa unicità garantisce che la simulazione rifletta esattamente il calcolo reale, evitando divergenze fra ambiente di test e ambiente di produzione.
- **Importi a precisione decimale fissa.** Tutti gli importi e le tariffe sono gestiti con tipo decimale a precisione fissa. Non si utilizzano tipi a virgola mobile, per evitare gli errori di arrotondamento tipici del calcolo finanziario.
- **Selezione della tariffa in due passi.** L'algoritmo di calcolo applica le decisioni in cascata descritte in §5.7: prima identifica la matrice tariffaria applicabile (standard del contratto o override del mezzo), poi seleziona al suo interno la tariffa appropriata in base alla fascia oraria. Le due decisioni sono indipendenti e in cascata.

---

## 6. Requisiti Funzionali

### 6.1 Autenticazione

- **RF-01 —** Il sistema deve consentire l'accesso tramite credenziali (email/username + password).
- **RF-02 —** L'app deve mantenere la sessione attiva sullo smartphone dell'autista per una durata configurabile (es. 12 ore), minimizzando la frequenza dei login durante la giornata lavorativa. La sessione deve poter essere revocata da remoto dalla segreteria/admin in caso di necessità.
- **RF-03 —** Dopo l'autenticazione, il campo "Autista" nei nuovi viaggi deve essere precompilato e non modificabile dall'autista stesso.

### 6.2 Inserimento viaggio (autista)

- **RF-04 —** L'autista deve poter inserire un viaggio fornendo almeno: mezzo principale (motrice o autonomo), eventuale rimorchio agganciato (quando richiesto dalla motrice), cliente, data di esecuzione, orari di inizio e fine (per viaggi a ore), parametri specifici della modalità di pagamento. I luoghi di partenza e consegna sono richiesti in base al livello di fatturazione configurato per il contratto del cliente selezionato.
- **RF-05 —** Il sistema deve mostrare dinamicamente i campi richiesti in base al cliente selezionato: i campi luogo si attivano e diventano obbligatori solo quando il livello di fatturazione lo prevede.
- **RF-06 —** Il sistema deve impedire il salvataggio di un viaggio con dati obbligatori mancanti, indicando chiaramente quali campi vanno completati.
- **RF-07 —** L'autista deve poter correggere un viaggio appena inserito entro una finestra temporale configurabile (es. 2 ore dalla creazione).
- **RF-08 —** L'autista deve poter visualizzare la lista dei propri viaggi del giorno e dei giorni recenti.
- **RF-08bis —** All'apertura del form di nuovo viaggio, il sistema deve preselezionare automaticamente l'ultima motrice (o ultimo mezzo autonomo) utilizzata dall'autista nella giornata corrente, se presente. Se la motrice preselezionata richiede rimorchio, il sistema preseleziona anche l'ultimo rimorchio utilizzato nella stessa giornata, se presente.
- **RF-08ter —** Solo i mezzi attivi (non dismessi) devono essere selezionabili nel form di nuovo viaggio.
- **RF-08quater —** Quando l'autista seleziona come mezzo principale del viaggio una motrice il cui flag "richiede rimorchio" è attivo, il sistema deve mostrare in modo evidente il campo di selezione del rimorchio, popolato con la lista dei rimorchi attivi, e impedire il salvataggio del viaggio finché il rimorchio non è stato selezionato. Se la motrice non richiede rimorchio o se il mezzo principale è un autonomo, il campo rimorchio non è mostrato.

### 6.3 Allegati e riepilogo fine giornata

- **RF-09 —** Quando il contratto del cliente prevede DDT obbligatorio, il sistema deve impedire il salvataggio del viaggio se non è stato allegato un file di tipo DDT.
- **RF-10 —** Quando il contratto del cliente prevede bolla obbligatoria, il sistema deve impedire il salvataggio del viaggio se non è stata allegata una bolla.
- **RF-10bis —** Quando il contratto del cliente prevede formulario rifiuti (FIR) obbligatorio, il sistema deve impedire il salvataggio del viaggio se non è stato allegato un file di tipo formulario rifiuti.
- **RF-11 —** L'app deve permettere il caricamento di allegati (immagini o PDF) sia tramite scatto fotografico diretto da fotocamera che da galleria del dispositivo.
- **RF-12 —** Il sistema deve imporre limiti su numero di allegati per viaggio e dimensione massima di ciascun file (parametri configurabili).
- **RF-13 —** Quando il contratto di almeno uno dei clienti per cui l'autista ha registrato viaggi nella giornata operativa corrente prevede riepilogo di fine giornata obbligatorio, l'app deve mostrare in modo permanente nella schermata principale, a partire da un orario di soglia configurabile a livello di sistema e sovrascrivibile per singolo cliente, un avviso non chiudibile che invita alla compilazione del riepilogo. L'avviso resta visibile finché l'autista non completa la compilazione e il caricamento dell'allegato obbligatorio.
- **RF-13bis —** Il riepilogo di fine giornata è unico per autista e per giornata operativa, indipendentemente dal numero di clienti con riepilogo obbligatorio per i quali l'autista ha lavorato nella giornata. L'allegato è unico; il riepilogo riporta automaticamente l'elenco dei clienti coperti. Per "giornata operativa" si intende una finestra temporale di 24 ore con orario di stacco configurabile a livello di sistema, da concordare con il Cliente in fase di setup iniziale e scelto in modo da non spezzare i turni operativi tipici della flotta.
- **RF-13ter —** Se l'autista non ha ancora compilato il riepilogo per giornate operative concluse, l'avviso permanente continua a essere visibile nella home dei giorni successivi finché il riepilogo arretrato non viene compilato o l'anomalia corrispondente non viene risolta dalla segreteria.
- **RF-14 —** Se al raggiungimento di un orario di cut-off configurabile a livello di sistema l'autista non ha compilato il riepilogo richiesto per la giornata operativa di riferimento, il sistema deve generare automaticamente un'anomalia visibile alla segreteria, riportante autista, giornata operativa e clienti coinvolti.
- **RF-14bis —** La compilazione tardiva del riepilogo (oltre il cut-off di RF-14) è sempre consentita all'autista e risolve automaticamente l'anomalia generata, mantenendo traccia in audit log dell'orario effettivo di compilazione.

### 6.4 Anagrafica clienti, contratti e tariffe (segreteria)

- **RF-15 —** La segreteria deve poter creare, modificare e disattivare i clienti.
- **RF-16 —** Per ogni cliente la segreteria deve poter definire uno o più contratti con intervalli di validità non sovrapposti. Un contratto è attivo se la data corrente cade nel suo intervallo.
- **RF-17 —** Il contratto deve specificare obbligatoriamente: livello di fatturazione (forfettario, per luogo di consegna, per luogo di partenza, per coppia di luoghi), modalità di pagamento (a ore, a peso, a km, a viaggio), eventuale obbligatorietà di DDT, bolla, formulario rifiuti, riepilogo fine giornata, eventuale fascia notturna personalizzata.
- **RF-18 —** All'inserimento di un nuovo contratto per un cliente che ne ha già uno attivo, il sistema deve proporre di chiudere il contratto precedente alla data precedente all'inizio del nuovo.
- **RF-19 —** Il sistema deve impedire la cancellazione di contratti o tariffe già utilizzati per il calcolo di viaggi (solo chiusura/archiviazione).
- **RF-20 —** La segreteria deve poter definire le tariffe associate al contratto con la specificità richiesta dal livello di fatturazione: una tariffa unica per il cliente (forfettario), una tariffa per ogni luogo (per luogo), una tariffa per ogni coppia (per coppia).
- **RF-21 —** Per ogni tariffa la segreteria deve poter configurare: prezzo unitario diurno, prezzo unitario notturno (se applicabile), prezzo di chiamata notturno (se applicabile), tipo di minimo (nessuno / fisso / scaglione con eccesso), parametri del minimo (importo M, soglia X, prezzo unitario eccedenza Y).
- **RF-22 —** La segreteria deve poter definire tariffe specifiche per mezzo (override). Una tariffa override è una matrice tariffaria completa con gli stessi campi della tariffa standard (diurno, notturno, chiamata notturna, minimo) e si applica al posto di quella standard quando il viaggio è effettuato con il mezzo specifico configurato. Gli override sono configurabili solo su motrici e mezzi autonomi.
- **RF-22bis —** Quando il contratto del cliente non prevede maggiorazione notturna, il form di configurazione di un override mezzo non deve mostrare i campi relativi a tariffa notturna e chiamata notturna, mantenendo coerenza con la struttura del contratto base. La modalità di pagamento del contratto (a ore, a peso, a km, a viaggio) non influisce su questa regola: l'override è ammesso e configurabile per tutte le modalità e include i campi notturni se e solo se il contratto base li prevede.
- **RF-23 —** Per ogni cliente con livello di fatturazione che richiede luoghi, deve essere possibile definire l'elenco dei luoghi associati (sia di partenza che di consegna).

### 6.5 Anagrafica autisti

- **RF-24 —** La segreteria deve poter creare, modificare e disattivare gli account autista.
- **RF-25 —** Un autista disattivato non può più accedere ma i suoi viaggi storici rimangono consultabili e attribuiti a lui.

### 6.6 Anagrafica mezzi

- **RF-26 —** La segreteria deve poter creare, modificare e dismettere i mezzi.
- **RF-27 —** Ogni mezzo è identificato almeno da targa (univoca), descrizione/modello, tipologia (motrice, rimorchio, autonomo) e stato (attivo/dismesso).
- **RF-27bis —** Per i mezzi di tipologia "motrice", la segreteria deve poter configurare un flag "richiede rimorchio". Quando il flag è attivo, i viaggi che utilizzano la motrice come mezzo principale richiedono obbligatoriamente la selezione di un rimorchio in fase di registrazione (vedi RF-08quater).
- **RF-27ter —** I mezzi di tipologia "rimorchio" non possono essere selezionati come mezzo principale di un viaggio. Possono essere selezionati esclusivamente come rimorchio agganciato a una motrice.
- **RF-28 —** La targa deve essere univoca all'interno del sistema, indipendentemente dalla tipologia di mezzo. Il sistema deve impedire l'inserimento di due mezzi attivi con la stessa targa.
- **RF-29 —** La dismissione di un mezzo è logica: il record rimane per coerenza storica dei viaggi ma non è più selezionabile sui nuovi viaggi.
- **RF-30 —** Il sistema deve impedire la cancellazione fisica di un mezzo già referenziato da uno o più viaggi (sia come mezzo principale sia come rimorchio).

### 6.7 Configurazione fascia oraria notturna

- **RF-31 —** L'admin deve poter configurare la fascia oraria notturna predefinita di sistema (orario inizio e orario fine, es. 22:00-06:00).
- **RF-32 —** Per i clienti con contratto che richiede fascia notturna personalizzata, deve essere possibile sovrascrivere la configurazione di sistema con orari specifici.
- **RF-33 —** Il sistema deve determinare automaticamente se un viaggio è notturno in base all'orario di inizio inserito dall'autista (vedi §5.8): un viaggio il cui orario di inizio cade nella fascia notturna configurata è classificato come notturno e riceve le maggiorazioni configurate sulla matrice tariffaria selezionata (standard o override mezzo).

### 6.8 Anomalie

- **RF-34 —** Il sistema deve mantenere un registro delle anomalie operative, generato automaticamente in base alle regole di business: viaggio salvato senza DDT obbligatorio, viaggio salvato senza bolla obbligatoria, viaggio salvato senza formulario rifiuti obbligatorio, autista senza riepilogo fine giornata per giornata con viaggi attivi richiedenti riepilogo.
- **RF-35 —** La segreteria deve poter visualizzare l'elenco delle anomalie aperte, filtrabile per tipo, autista, cliente, periodo.
- **RF-36 —** Ogni anomalia deve poter essere risolta: caricamento postumo dell'allegato mancante, compilazione tardiva del riepilogo, oppure marcatura come "accettata" con motivazione testuale tracciata in audit log.
- **RF-37 —** L'elenco viaggi della segreteria deve evidenziare visivamente i viaggi che presentano anomalie.

### 6.9 Reportistica e consultazione

- **RF-38 —** La segreteria deve poter visualizzare una lista filtrabile di viaggi per: intervallo date, cliente, autista, mezzo (motrice o rimorchio), luogo, stato, presenza di anomalie.
- **RF-39 —** Il sistema deve mostrare totali aggregati per cliente (numero viaggi, importo totale, breakdown per modalità di pagamento, eventuali maggiorazioni notturne separate).
- **RF-40 —** Deve essere possibile esportare i risultati filtrati in due formati alternativi: Excel (.xlsx) per il riutilizzo in foglio di calcolo, e PDF per la stampa, l'archiviazione documentale o l'invio al cliente. L'export PDF deve presentarsi come un report formattato e leggibile, con intestazione (cliente, periodo, criteri di filtro applicati), tabella dei viaggi e totali aggregati in calce; non come una semplice stampa della pagina web.
- **RF-41 —** L'export deve includere tutti i dati necessari alla fatturazione e all'analisi operativa: cliente, data viaggio, luoghi, autista, motrice (targa), eventuale rimorchio (targa), parametri (ore/peso/km), tariffa applicata, eventuale maggiorazione notturna, importo calcolato.

---

## 7. Flusso Operativo Principale

### 7.1 Inserimento viaggio da parte dell'autista

| # | Passo |
|---|---|
| 1 | L'autista apre la PWA sul proprio smartphone (precedentemente installata sulla schermata principale come fosse un'app). La sessione è già attiva (login fatto a inizio giornata). |
| 2 | Tocca il pulsante "Nuovo viaggio". |
| 3 | Seleziona il mezzo principale (motrice o autonomo). Se il sistema ha rilevato un mezzo già usato nella giornata, lo propone preselezionato. |
| 4 | Se il mezzo selezionato al passo 3 è una motrice con flag "richiede rimorchio" attivo, l'app richiede tassativamente la selezione di un rimorchio dalla lista dei rimorchi attivi. Se la motrice non richiede rimorchio o se il mezzo principale è un autonomo, il campo non viene mostrato. |
| 5 | Seleziona il cliente da una lista ricercabile (i clienti più usati di recente sono in cima). Il sistema verifica il livello di fatturazione del contratto attivo per quel cliente. |
| 6 | Se il contratto richiede luogo di partenza, l'autista lo seleziona da lista associata al cliente o lo inserisce manualmente. Se richiede luogo di consegna, idem. |
| 7 | La data del viaggio è preimpostata a "oggi". Per i viaggi a ore l'autista inserisce gli orari di inizio e fine. |
| 8 | In base alla modalità di pagamento del contratto, il sistema mostra i campi rilevanti: "Ore" calcolate dagli orari per tariffa oraria, "Peso" per tariffa a peso, "Km" per tariffa a km, solo orario di inizio per tariffa a viaggio. |
| 9 | Se il contratto richiede DDT, bolla o formulario rifiuti, l'app mostra in modo evidente i pulsanti per allegare i documenti, fotografandoli direttamente o selezionando dalla galleria. |
| 10 | L'autista compila i campi e conferma. Il sistema valida la completezza in base alle regole del contratto, inclusi gli allegati obbligatori e la presenza del rimorchio quando richiesto dalla motrice. |
| 11 | Il sistema applica l'algoritmo di selezione tariffa (vedi §5.7): prima sceglie la matrice tariffaria (standard o override del mezzo), poi seleziona la tariffa diurna/notturna in base all'orario, applica eventuali maggiorazioni notturne, minimi/scaglioni, e salva il viaggio con snapshot della tariffa. |
| 12 | Viene mostrata una schermata di conferma con riepilogo. L'autista può aggiungere un nuovo viaggio o tornare alla home. |

### 7.2 Riepilogo di fine giornata

Quando nella giornata operativa corrente l'autista ha registrato almeno un viaggio per un cliente il cui contratto prevede riepilogo di fine giornata obbligatorio, l'app a partire dall'orario di soglia configurato (eventualmente sovrascritto a livello di singolo cliente) mostra un avviso permanente nella home che invita alla compilazione. Il riepilogo è unico per autista e per giornata, anche quando coinvolge più clienti, e riporta automaticamente l'elenco dei clienti coperti dalla giornata. La compilazione richiede un breve testo libero e un allegato obbligatorio (foto o PDF). Una volta confermato, l'avviso scompare e la giornata è considerata completa.

Se l'autista non compila il riepilogo entro l'orario di cut-off configurato, il sistema genera automaticamente un'anomalia visibile alla segreteria. L'autista può comunque compilare il riepilogo nei giorni successivi: l'avviso permanente rimane visibile nella home finché il riepilogo arretrato non è stato compilato, e la compilazione tardiva risolve automaticamente l'anomalia.

---

## 8. Requisiti Non Funzionali

### 8.1 Usabilità

- **RNF-01 —** L'app autista deve essere ottimizzata per smartphone in orientamento verticale (portrait), con design mobile-first. Le aree tappabili devono avere dimensione minima di 44×44 px per garantire l'usabilità con guanti da lavoro o in condizioni di fretta.
- **RNF-02 —** I messaggi di errore devono essere chiari e non tecnici (es. "Connessione persa. Riprova." invece di "Network error 503").
- **RNF-03 —** Le immagini caricate come allegato (DDT, bolla, formulario rifiuti, riepilogo fine giornata) devono essere compresse e normalizzate nell'orientamento lato client prima dell'invio al server, in modo da contenere la dimensione del file trasmesso e garantire la corretta rotazione del documento indipendentemente dal visualizzatore. La compressione deve preservare la leggibilità di tutti i contenuti rilevanti del documento (numeri, codici, descrizioni, firme); i parametri specifici sono di calibrazione tecnica e vanno validati su documenti reali del cliente prima del go-live. Il requisito si applica alle sole immagini; i file PDF caricati non sono soggetti a ricompressione lato client. Durante l'elaborazione locale dell'immagine l'app deve mostrare un indicatore di stato per evitare che l'autista percepisca l'app come bloccata.

### 8.2 PWA e connettività

- **RNF-04 —** L'applicazione deve essere una Progressive Web App installabile sullo smartphone dell'autista come icona sulla schermata principale, accessibile con un singolo tocco al pari delle app native.
- **RNF-05 —** L'applicazione richiede una connessione Internet attiva per la registrazione dei viaggi. La gestione degli errori di rete è quella standard di un'applicazione web.

### 8.3 Sicurezza e dati

- **RNF-06 —** Tutte le comunicazioni client-server devono utilizzare HTTPS.
- **RNF-07 —** Le password devono essere salvate in formato hashato con algoritmo moderno (es. bcrypt o Argon2).
- **RNF-08 —** Il sistema deve mantenere un audit log delle operazioni sensibili: modifiche a tariffe, modifiche a viaggi già salvati, accessi falliti, risoluzioni di anomalie.
- **RNF-09 —** Tutti gli importi devono essere gestiti con tipo decimale a precisione fissa (no floating point), per evitare errori di arrotondamento.
- **RNF-10 —** Gli allegati devono essere conservati su storage sicuro con accesso autenticato. La cancellazione deve essere logica (soft delete) con possibilità di recupero per un periodo configurabile.

### 8.4 Compatibilità

- **RNF-11 —** L'app autista deve funzionare sui browser mobile di Android (Chrome) e iOS (Safari) versioni rilasciate negli ultimi 2 anni.
- **RNF-12 —** L'interfaccia di backoffice deve funzionare su Chrome, Edge, Firefox desktop versioni recenti.

### 8.5 Manutenibilità

- **RNF-13 —** Il codice deve essere accompagnato da test automatizzati (unit e integration) per le funzionalità core, in particolare per il calcolo tariffario.
- **RNF-14 —** Il sistema deve avere documentazione tecnica aggiornata: schema DB, API, procedure di deploy.
- **RNF-15 —** I codici di errore e le label di sistema devono essere centralizzati per facilitare future eventuali traduzioni.

---

## 9. Metriche di Successo

- **M-01 —** Percentuale di viaggi inseriti entro 24h dalla loro esecuzione (target: >90% entro 3 mesi dal go-live).
- **M-02 —** Numero medio di correzioni manuali effettuate dalla segreteria per viaggio (target: <5%).
- **M-03 —** Tempo medio di preparazione del riepilogo mensile per cliente da parte della segreteria (target: riduzione >50% rispetto al processo attuale).
- **M-04 —** Percentuale di anomalie aperte oltre 7 giorni (target: <10%).
- **M-05 —** Percentuale di viaggi con allegati obbligatori mancanti al primo salvataggio (target: <5% dopo 3 mesi).

---

## 10. Assunzioni, Dipendenze, Rischi

### 10.1 Assunzioni

- Gli autisti dispongono di uno smartphone personale (modello BYOD) con connettività dati. Priamus si farà carico della relativa policy aziendale BYOD, comprensiva degli aspetti GDPR e di eventuali rimborsi.
- La segreteria dispone di competenze base per utilizzare un'interfaccia web di backoffice.
- Le modalità di pagamento dei clienti esistenti possono essere categorizzate all'interno delle quattro tipologie supportate (a ore, a peso, a km, a viaggio).
- Le anagrafiche iniziali (clienti, autisti, mezzi — motrici, rimorchi, autonomi — luoghi, contratti, tariffe storiche) saranno caricate manualmente o tramite import CSV una tantum prima del go-live. Per ogni motrice sarà stabilito al caricamento se richiede rimorchio in fase di registrazione viaggio.
- La flotta aziendale è nota e anagrafabile prima del go-live: non sono previsti scenari di mezzi noleggiati occasionalmente non presenti in anagrafica (se ne terrà conto in eventuali evoluzioni future).
- Le regole di calcolo tariffario emerse in fase di raccolta requisiti rappresentano la totalità delle casistiche operative correnti del cliente. Eventuali casi non emersi in questa fase saranno gestiti tramite change request.

### 10.2 Rischi e mitigazioni

| Rischio | Mitigazione |
|---|---|
| Connettività instabile in luoghi remoti durante l'inserimento viaggio. | Gestione robusta degli errori di rete con retry esplicito e indicatore di stato connessione. Possibilità per l'autista di riprovare l'invio senza perdita dei dati compilati. |
| Errore nell'algoritmo di selezione tariffa per combinazioni complesse (cliente + luoghi + mezzo + notturno + scaglioni + override notturno). | Suite di test completa con scenari rappresentativi di ogni combinazione, inclusi i casi di override mezzo applicato a viaggi notturni. Validazione preliminare con il cliente su esempi reali tramite la funzionalità di simulazione integrata. Snapshot della tariffa sul viaggio per immutabilità storica. |
| Resistenza al cambiamento da parte degli autisti, abituati al processo manuale. | Coinvolgimento degli autisti in fase di test di accettazione. Onboarding strutturato e materiale formativo in italiano. Periodo di affiancamento post go-live. |
| Configurazione errata del flag "richiede rimorchio" sulle motrici (motrici che non richiedono rimorchio configurate come obbligatorie e viceversa). | Validazione anagrafica iniziale insieme al referente operativo Priamus. Possibilità per la segreteria di modificare il flag senza impatto sui viaggi storici. Anomalia generata se un autista forza ripetutamente il salvataggio di un viaggio segnalando incoerenza. |
| Variabilità non documentata nelle regole tariffarie reali del cliente (eccezioni storiche, accordi verbali). | Validazione in parallelo nei primi 30 giorni post go-live: confronto sistematico tra il calcolo del sistema e i risultati del processo manuale, con correzione iterativa di eventuali divergenze. |
| Privacy e gestione dispositivi BYOD. | Definizione di policy aziendale BYOD lato cliente. Niente accesso a dati personali del dispositivo dell'autista da parte dell'app. Sessione revocabile da remoto. |

---

## 11. Evoluzioni Future (Out of Scope MVP)

- Funzionamento offline come possibile estensione futura, con coda di sincronizzazione e store locale dei viaggi.
- Passaggio da modello BYOD (smartphone personale) a tablet o smartphone aziendali dedicati, con possibilità di configurazione MDM (Mobile Device Management) per maggiore controllo e sicurezza.
- Integrazione con gestionale di fatturazione per generazione automatica delle fatture.
- Riconoscimento ottico (OCR) di DDT, bolle e formulari rifiuti per estrarre automaticamente dati come quantità, peso, numero documento, codice CER.
- Definizione e supporto di modalità di pagamento aggiuntive (a colli, a passaggio, tariffe miste).
- Categorizzazione dei mezzi (es. "mezzi speciali", "mezzi standard") con tariffe configurabili a livello di categoria invece che per singolo mezzo, per ridurre la duplicazione di configurazioni quando più mezzi simili condividono le stesse condizioni economiche.
- Tariffazione differenziata in funzione del rimorchio agganciato (oggi il rimorchio è informazione di tracciabilità e non incide sulla tariffa).
- Tracciamento GPS e calcolo automatico di chilometri e tempi.
- Notifiche push per il riepilogo di fine giornata: oltre all'avviso permanente in-app, invio automatico di una notifica push allo smartphone dell'autista al raggiungimento dell'orario di soglia, in modo da sollecitare la compilazione anche quando l'app non è aperta. Tecnicamente realizzabile via Web Push API; rimandato a evoluzione futura per la complessità del supporto cross-platform (Android pieno, iOS limitato a versioni recenti con PWA installata) e la gestione delle subscription.
- Firma digitale del destinatario e caricamento foto della consegna.
- App per il cliente finale con visibilità sui propri viaggi e spese.
- Analytics avanzata: marginalità per cliente, ore uomo/mezzo, saturazione flotta.
- Gestione turni e pianificazione viaggi lato segreteria.
- Gestione mezzi noleggiati occasionalmente non presenti in anagrafica.

---

## 12. Appendice — Esempi di Calcolo

### 12.1 Tariffa oraria con minimo fisso

- Cliente Beta — contratto: forfettario, a ore, 10 €/h, minimo fisso 30 €.
- Viaggio di 2 ore → calcolo base 20 € < minimo 30 € → fatturato 30 €.
- Viaggio di 5 ore → calcolo base 50 € > minimo 30 € → fatturato 50 €.

### 12.2 Scaglione con eccesso

- Cliente Gamma — contratto: forfettario, a ore, minimo 50 € fino a 4 ore, eccedenza 12 €/h.
- Viaggio di 3 ore → fatturato 50 € (sotto soglia).
- Viaggio di 4 ore → fatturato 50 €.
- Viaggio di 6 ore → fatturato 50 + (6 − 4) × 12 = 74 €.

### 12.3 Tariffa per coppia di luoghi con override mezzo (diurno e notturno)

Lo scenario seguente illustra come si combinano override per mezzo, fascia oraria e relative maggiorazioni. Cliente Delta — contratto: per coppia di luoghi, modalità a ore, fascia notturna 22:00-06:00.

**Configurazione delle tariffe per la coppia (Magazzino A → Cantiere X):**

| Matrice | Diurno | Notturno | Chiamata notturna |
|---|---|---|---|
| Standard del contratto | 30 €/h | 42 €/h | 35 € |
| Override "Autospurgo XY-123" | 55 €/h | 72 €/h | 50 € |

**Calcolo per quattro casi rappresentativi (viaggi di 3 ore):**

- **Caso A — diurno, mezzo standard (Camion AB-456):** matrice scelta = standard. Tariffa applicabile = diurno (30 €/h). Calcolo: 3 × 30 = 90 €.
- **Caso B — notturno, mezzo standard:** matrice scelta = standard. Tariffa applicabile = notturno (42 €/h). Calcolo: 3 × 42 + 35 (chiamata notturna standard) = 161 €.
- **Caso C — diurno, Autospurgo XY-123:** matrice scelta = override. Tariffa applicabile = diurno della matrice override (55 €/h). Calcolo: 3 × 55 = 165 €.
- **Caso D — notturno, Autospurgo XY-123:** matrice scelta = override. Tariffa applicabile = notturno della matrice override (72 €/h). Chiamata notturna applicata = chiamata della matrice override (50 €). Calcolo: 3 × 72 + 50 = 266 €.

### 12.4 Maggiorazione notturna su tariffa unica cliente

- Cliente Epsilon — contratto: forfettario, a ore, 20 €/h diurno, 30 €/h notturno, prezzo chiamata notturno 25 €. Fascia notturna 22:00-06:00.
- Viaggio diurno di 3 ore (10:00-13:00) → 60 €.
- Viaggio notturno di 3 ore (23:00-02:00) → 25 (chiamata) + 3 × 30 = 115 €.

### 12.5 Modalità a km con maggiorazione notturna

- Cliente Zeta — contratto: forfettario, a km, 1,20 €/km diurno, 1,60 €/km notturno, chiamata notturna 30 €. Fascia notturna 22:00-06:00.
- Viaggio diurno di 80 km (partenza ore 09:00) → 80 × 1,20 = 96 €.
- Viaggio notturno di 80 km (partenza ore 23:00) → 80 × 1,60 + 30 = 158 €.

### 12.6 Modalità a viaggio con maggiorazione notturna

- Cliente Eta — contratto: forfettario, a viaggio, tariffa diurna 150 €, tariffa notturna 200 €, chiamata notturna 40 €.
- Viaggio diurno (partenza ore 14:00) → 150 €.
- Viaggio notturno (partenza ore 23:00) → 200 + 40 = 240 €.

> *Nota: la modalità "A viaggio" non supporta minimo fatturabile o scaglione con eccesso. La tariffa è già un valore fisso che fa da minimo implicito.*

### 12.7 Storicizzazione tariffa

- Cliente Alpha — tariffa: "a ore", 10 €/h, valida dal 01/01/2026 al 31/03/2026.
- Cliente Alpha — tariffa: "a ore", 12 €/h, valida dal 01/04/2026 (aperta).
- Viaggio A: data 15/03/2026, 5 ore → 5 × 10 = 50,00 € (tariffa valida al 15/03/2026).
- Viaggio B: data 10/04/2026, 5 ore → 5 × 12 = 60,00 € (tariffa valida al 10/04/2026).

Se in data 20/04/2026 la segreteria modificasse retroattivamente la tariffa del 01/04, il Viaggio B già salvato manterrebbe il proprio importo originale grazie allo snapshot denormalizzato sul record. Una eventuale funzionalità di "ricalcolo" dovrà essere esplicita e tracciata.

---

## 13. Glossario

| Termine | Definizione |
|---|---|
| PWA | Progressive Web App — applicazione web installabile sul dispositivo che funziona in modo simile a un'app nativa (icona sulla home, esperienza a tutto schermo, splash screen) senza necessità di passare da app store. |
| BYOD | Bring Your Own Device — modello organizzativo in cui i dipendenti utilizzano i propri dispositivi personali (smartphone, tablet, PC) per attività lavorative, in alternativa alla fornitura di dispositivi aziendali dedicati. |
| MVP | Minimum Viable Product — versione iniziale del prodotto con le sole funzionalità essenziali per validare il concept. |
| Audit log | Registro delle operazioni che tiene traccia di chi ha fatto cosa e quando, ai fini di tracciabilità e accountability. |
| Soft delete | Cancellazione logica: il record non viene fisicamente rimosso ma marcato come cancellato. |
| Snapshot tariffa | Copia dei valori della tariffa denormalizzati sul record del viaggio per garantire immutabilità dell'importo storico. |
| Tariffa aperta | Tariffa con data fine validità non valorizzata, cioè attualmente in vigore. |
| Livello di fatturazione | Granularità con cui sono definite le tariffe di un cliente: forfettario (cliente), per luogo, per coppia di luoghi. |
| Minimo fatturabile | Importo minimo applicato a un viaggio anche se il calcolo base risulta inferiore. |
| Scaglione con eccesso | Schema tariffario in cui fino a una soglia si paga un importo fisso, oltre la soglia si somma un costo unitario per ogni unità eccedente. |
| DDT | Documento di Trasporto — documento fiscale che accompagna la merce trasportata, contenente dati su mittente, destinatario, descrizione e quantità. |
| Bolla | Documento di consegna firmato dal destinatario a riprova dell'avvenuta consegna. |
| Formulario rifiuti (FIR) | Formulario di Identificazione del Rifiuto — documento previsto dalla normativa di settore per il trasporto di rifiuti, che identifica produttore, trasportatore, destinatario, codice CER, quantità e caratteristiche del rifiuto. Nel sistema è trattato come allegato del viaggio, opzionale o obbligatorio in funzione del contratto del cliente. |
| Riepilogo fine giornata | Documento riassuntivo compilato dall'autista a chiusura della giornata lavorativa, con allegato fotografico, richiesto da specifici contratti. Unico per autista e per giornata operativa, anche quando coinvolge più clienti. |
| Giornata operativa | Finestra temporale di 24 ore di riferimento per la chiusura della giornata di lavoro dell'autista. L'orario di stacco è configurabile a livello di sistema e va concordato con il Cliente in fase di setup, scelto in modo da non attraversare i turni operativi tipici della flotta. Pensata per gestire correttamente i turni che attraversano la mezzanotte: tutti i viaggi di un singolo turno appartengono alla stessa giornata operativa anche se cadono in giorni solari diversi. |
| Cut-off riepilogo | Orario, configurabile, oltre il quale la mancata compilazione del riepilogo di fine giornata genera automaticamente un'anomalia. La compilazione resta comunque possibile dopo il cut-off e risolve l'anomalia generata. |
| Anomalia | Stato inconsistente di un dato (viaggio o giornata) che richiede attenzione della segreteria per essere risolto. |
| Fascia notturna | Intervallo orario configurabile durante il quale i viaggi sono soggetti a maggiorazioni tariffarie. |
| Matrice tariffaria | Insieme strutturato dei valori economici di una tariffa: prezzo diurno, prezzo notturno (se applicabile), prezzo chiamata notturna (se applicabile), eventuale minimo o scaglione. Una tariffa standard del contratto è una matrice; un override per mezzo è un'altra matrice parallela con la stessa struttura ma valori diversi. |
| Override tariffa per mezzo | Configurazione di una tariffa specifica che si applica solo quando un determinato mezzo è utilizzato come mezzo principale di un viaggio. È una matrice tariffaria completa che sostituisce integralmente la matrice standard del contratto. |
| Motrice | Veicolo trattore della flotta. Può essere usato da solo o, se così configurato, in abbinamento obbligatorio con un rimorchio in fase di registrazione viaggio. |
| Rimorchio | Veicolo trainato della flotta. Non può essere selezionato come mezzo principale di un viaggio; viene selezionato come complemento di una motrice che richiede l'aggancio. |
| Mezzo autonomo | Veicolo che non richiede né prevede rimorchio agganciato (es. autospurgo, gru mobile, furgone). Si seleziona come unico mezzo del viaggio. |
