# Analisi del sistema di raccomandazioni

Data: 23 novembre 2026  
Revisione del codice analizzata: `f9bbb2c`

## Sintesi esecutiva

Pickarr usa un **sistema ibrido prevalentemente basato sui contenuti**, calcolato localmente per profilo: combina popolarità e voti TMDB, metadati strutturati, similarità semantica opzionale e collegamenti TMDB. Non è un LLM, non è un modello addestrato sui gusti della famiglia e non usa, salvo il piccolo prior familiare, collaborative filtering tra utenti.

La struttura è ragionevole per un'installazione domestica con pochi utenti e poche valutazioni. La maggiore opportunità non è sostituire subito il modello: è **correggere il protocollo di valutazione e alcune limitazioni della selezione dei candidati**, poi misurare le alternative. I risultati più importanti dell'ispezione sono:

1. **Valutazione offline inattendibile per decidere il modello**: anche i titoli valutati negativamente sono trattati come positivi nelle metriche AUC e hit@10; inoltre il confronto non riproduce interamente il ranking reale. Il livello 3 può essere attivato sulla base di tale valutazione.
2. **Segnali fuori dal pool**: entrano in `N_eff`, ma i rispettivi vettori non vengono caricati nel build. Possono quindi aumentare la fiducia nella personalizzazione senza contribuire al profilo. Questo riguarda anche titoli di fonti oggi disabilitate.
3. **Titoli simili**: la ricerca locale si limita ai primi 120 candidati raccolti, prevalentemente dello stesso genere, prima di calcolare la similarità; quindi non garantisce di trovare i più simili nel catalogo.
4. **Titoli “fuori”**: quelli rimossi da un provider rimangono deliberatamente raccomandabili; quelli mai presenti in una fonte abilitata non vengono invece scoperti né proposti. Non sono due malfunzionamenti equivalenti: sono scelte di prodotto esplicite.
5. **Pesi ed esplorazione**: sono perlopiù euristiche non calibrate sull'installazione. L'esplorazione è una sostituzione nel prefisso, non una garanzia esatta del 10% su ogni pagina, e può scegliere titoli con buon prior ma scarsa affinità personale.

**Limiti dell'analisi:** ispezione statica di codice, specifiche e test; non ho letto il database dell'installazione, eseguito job, cambiato configurazioni o misurato accuratezza, copertura, tempi e memoria reali. Le criticità di implementazione sono dedotte dal codice; entità degli effetti e superiorità delle proposte restano da verificare. I default sotto non provano quali valori siano attivi nell'installazione.

## 1. Flusso attuale

1. Il job raccoglie i titoli con almeno una `Availability` su fonte **e** provider abilitati. La presenza di `RemovedAt` non li esclude. Costruisce un ranking per ogni profilo e lo pubblica con una versione. [Build](src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs), [membership](src/Pickarr.Core/Recommendations/RecoMembership.cs).
2. Il profilo raccoglie rating e, se abilitati, segnali impliciti; l'identità è il titolo, indipendentemente dal provider. `N_eff` è la somma dei valori assoluti dei segnali.
3. Per ogni candidato sono calcolati prior, score sparse e, quando disponibile, kNN sugli embedding; nel cold start può intervenire anche il grafo TMDB. Un prior familiare entra nella parte non personale.
4. In lettura si applicano scope, tipo, genere, anno e le esclusioni; poi eventuale diversificazione e quota esplorativa. Il ranking viene restituito con motivazioni e disponibilità. [Query](src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs).
5. Rating e hide pianificano un rebuild con debounce di 30 secondi; build ed embedding hanno anche una schedulazione oraria, il backfill delle feature è giornaliero. I tempi effettivi dipendono dalla coda. [Scheduler](src/Pickarr.Infrastructure/Jobs/RecoRebuildScheduler.cs), [job ricorrenti](src/Pickarr.Infrastructure/Jobs/ScheduleReconciler.cs).

### Cosa entra e cosa no

| Caso | Comportamento |
|---|---|
| Titolo presente su una fonte abilitata, anche se `RemovedAt` è valorizzato | Nel pool e potenzialmente raccomandato; il dato di rimozione è esposto. |
| Titolo su altra fonte abilitata | In scope `enabled` può apparire; in scope `source` solo se ha un'`Availability` per quella fonte. |
| Titolo solo su fonte disabilitata o senza `Availability` abilitata | Non è un candidato; può comunque esistere come rating/segnale. |
| Titolo già valutato o nascosto | Escluso dalle liste in lettura, ma il rating può contribuire al gusto. |
| Titolo in libreria Sonarr/Radarr | Escluso di norma, indipendentemente dallo stato della riga; il profilo può consentirne la visualizzazione. |
| Preferito | Può rimanere candidato se non escluso da altre condizioni; è anche un segnale implicito temporaneo. |

**Simili** cerca nell'unione delle fonti abilitate, anche quando il dettaglio era stato aperto da una sola fonte; esclude titolo di partenza, rating, hide e, se previsto dal profilo, libreria. Mostra `places`, compresi eventuali luoghi rimossi. Una fonte passata al metodo serve anche a scegliere la lingua, non restringe il pool. [QuerySimilarAsync](src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs).

Il limite ai cataloghi già importati e l'inclusione dei rimossi sono requisiti espliciti, non semplici filtri dimenticati: [FR-REC-CD-01–04 e BR-REC-07](docs/recommendations.md). Raccomandare titoli totalmente esterni, filtrarli per disponibilità attuale o ordinarli diversamente richiederebbe una decisione di prodotto e aggiornamento delle specifiche; non basta cambiare il modello. Se il problema percepito è «non posso guardarlo qui», la prima verifica è il `removedAt` dei `places`, distinguendo **storico rimosso**, **altro provider attivo** e **titolo veramente fuori pool**. Un'opzione futura, soggetta ad approvazione, è un filtro/indicatore «disponibile ora» mantenendo il comportamento storico come default.

## 2. Segnali e pesi

Default da [RecoSignals](src/Pickarr.Core/Recommendations/RecoSignals.cs); possono essere configurati. Un rating esplicito sostituisce tutti i segnali impliciti dello stesso titolo.

| Segnale | Peso | Interpretazione e limiti |
|---|---:|---|
| Love | +1,5 | Gradimento forte. |
| Like | +1 | Gradimento. |
| Dislike | −1 | Gradimento negativo, usato anche per penalizzare contenuti affini. |
| Not interested | −0,75 | Mancato interesse; non equivale necessariamente a un'opera già vista e giudicata male. |
| My list / favorito | +0,5 | Interesse temporaneo: scompare dal profilo se rimosso, o è sostituito dal rating. |
| Add Sonarr/Radarr riuscito | +0,6 | Intenzione, non prova di visione né di apprezzamento. |
| Download osservato | +0,4 | Solo se attribuibile al profilo tramite Add o audit storico; non prova di visione. |
| Hide / watched | 0 | Di default nasconde, senza creare gusto positivo. |

Gli impliciti si sommano e vengono limitati a `[0,1]`: per esempio favorito + add = 1, non 1,1. Add + download = 1; eliminare da Arr non annulla il segnale storico. `UseImplicit` è per profilo. [Caricamento dei segnali](src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs).

`N_eff = Σ |s_i|`: un Love vale 1,5 e un Dislike 1 ai fini della «massa», non è un numero di persone o di valutazioni indipendenti. Stadi: `cold` sotto 5, `warming` fino a 30, `personalized` da 30 in poi per `N0=30`. Non c'è decadimento temporale applicato al build corrente, sebbene la specifica menzioni un'opzione di half-life. [RecoLearning](src/Pickarr.Core/Recommendations/RecoLearning.cs), [specifica](docs/recommendations.md).

**Rischio progettuale:** molti impliciti, o molti segnali correlati, possono far crescere la fiducia più rapidamente della reale conoscenza del gusto; molti `Not interested` possono modellare come antipatia ciò che è solo disinteresse. Sono ipotesi da testare, non prove di errore per tutti gli utenti.

## 3. Modello e formule

### 3.1 Prior non personale

Il [prior](src/Pickarr.Core/Recommendations/RecoPrior.cs) calcola:

```text
C = media dei voti TMDB disponibili nel pool
bayes = v/(v+300) × voto + 300/(v+300) × C
prior = 0,7 × minmax(bayes) + 0,3 × minmax(log(1+popolarità))
```

`v` è il numero di voti. Senza voto si usa `C`, senza popolarità il contributo popolarità è zero. La normalizzazione è sull'intero pool, non separata per film e serie nell'attuale build; se tutti i valori di un componente sono uguali il risultato è 0,5. Voti, popolarità e loro scale dipendono dalla copertura e dal mix del catalogo.

Nel cold start possono contribuire i Love/Like degli altri profili: media per titolo dei soli voti positivi esistenti, trasformati in un profilo sparse. La quota `h = 0,5 × (1 − min(1,N_eff/N0))` si applica **solo alla parte prior**, se esiste un profilo familiare utile; scompare a `N0`. Gli altri utenti non contribuiscono con dislike o impliciti. [RecoHousehold](src/Pickarr.Core/Recommendations/RecoHousehold.cs).

### 3.2 Profilo sparse

Il [livello 1](src/Pickarr.Core/Recommendations/RecoSparse.cs) usa termini TF-IDF binari, IDF calcolato sul pool e vettori normalizzati L2:

| Dimensione | Moltiplicatore |
|---|---:|
| Generi; keyword | 1,0 ciascuno |
| Registi/creatori | 0,8 |
| Cast; tipo | 0,5 |
| Lingua originale; paese; decennio | 0,3 ciascuno |

Il profilo è `P = normalizza(Σ s_i × vettore_i)`, troncato ai 200 termini di maggiore valore assoluto. Lo score è il coseno tra `P` e il vettore del candidato, poi min-max sul pool. I pesi delle dimensioni non sono direttamente contributi percentuali: intervengono anche IDF, numero di termini e normalizzazione.

### 3.3 Embedding e kNN

Il modello previsto è [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2), variante ONNX quantizzata del catalogo locale: encoder semantico **inglese**, 384 dimensioni, massimo 256 token; mean pooling e normalizzazione L2. Non predice direttamente il gradimento: rappresenta la somiglianza tra testi. L'input concatena nome, anno, generi, fino a 15 keyword, tagline e trama; preferisce la localizzazione inglese e usa una disponibile se manca. [Catalogo modello](src/Pickarr.Core/Recommendations/RecoModelCatalog.cs), [testo](src/Pickarr.Core/Recommendations/RecoEmbeddingText.cs), [inferenza](src/Pickarr.Infrastructure/Metadata/Reco/RecoOnnxEmbedder.cs).

Il livello 2 si usa nel build se embedding abilitati, modello `Ready`, copertura almeno 80% e `N_eff > 0`. Per candidato:

```text
vicini = fino a 10 titoli con segnale e coseno ≥ 0,25
kNN = Σ(coseno³ × segnale) / Σ(coseno³)
se vicini < 10: kNN × numero_vicini/10
limita a [-1,1], mappa in [0,1], poi min-max sul pool con embedding
```

Per limitare il costo sono usati al massimo **24 titoli con segnale e embedding**, selezionati per `|segnale|` e, a parità, ID titolo: non sono necessariamente i 24 più recenti o più rappresentativi. Il segnale Love +1,5 può essere limitato dal clamp finale. Per un candidato senza embedding il build usa valore kNN 0,5, non un ricalcolo sparse esclusivo. [RecoKnn](src/Pickarr.Core/Recommendations/RecoKnn.cs), [build](src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs).

### 3.4 Fusione, TMDB e livello 3

Con `raw = min(1,N_eff/30)` e impostazioni standard:

```text
w = min(raw, 0,9)
prior' = (1-h) × prior + h × household
base = 0,4 × sparse + 0,6 × kNN   # con livello 2
base = sparse                      # senza livello 2
c = 0,25 × (1-raw)
personal = (1-c) × base + c × TMDB   # solo se candidato raggiunto con score TMDB > 0
score = (1-w) × prior' + w × personal
```

Se il profilo sparse è vuoto, il build usa il solo `prior'`, anche in presenza di `N_eff`. Con `N_eff=0` il prior familiare può comunque incidere; con `N_eff≥30` restano 10% prior e 90% personale. I pesi 0,4/0,6, `K=10`, `γ=3`, soglia 0,25 e residuo 0,1 sono costanti nel percorso corrente, non tutti impostazioni effettivamente modificabili come suggerisce la tabella della specifica.

Il grafo usa archi `/recommendations` TMDB memorizzati tra titoli locali nel pool. Le sorgenti con segnale positivo propagano `segnale × (21-rank)/20` per rank 1–20; un candidato con segnale negativo non riceve contributi. I contributi sono sommati e normalizzati. **“Amplificatore” è una miscela, non sempre un bonus**: se lo score TMDB normalizzato è inferiore a `base`, può abbassare il personale del titolo raggiunto. Il suo peso va a zero a `N0`. Per Simili il contributo diretto TMDB è invece additivo, fino a `0,15 × peso_rank`. [RecoTmdbBoost](src/Pickarr.Core/Recommendations/RecoTmdbBoost.cs).

**Livello 3 opzionale:** regressione logistica ML.NET regolarizzata, con 32 bucket hash di feature sparse e 6 valori numerici. Richiede per default almeno 100 rating e 20 esempi per classe; è attivata dopo valutazione e sostituisce il personale quando l'addestramento riesce. Non è un LLM. La decisione di attivazione è nello stato admin e il build prova ad addestrare separatamente sui rating del profilo corrente. Non assumere che sia attiva: verificare lo stato. [Feature](src/Pickarr.Core/Recommendations/RecoLevel3.cs), [modello](src/Pickarr.Infrastructure/Metadata/Reco/RecoLevel3Model.cs), [valutazione](src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs).

### 3.5 Diversità, esplorazione e spiegazioni

MMR usa `0,7 × score − 0,3 × massima_similarità_con_i_già_scelti`, sul prefisso di **120** candidati idonei. Per prefissi oltre 48 usa Jaccard dei generi; per prefissi più piccoli può usare gli embedding, se almeno due presenti. In quest'ultimo caso le coppie senza embedding restano senza similarità calcolata e sono trattate come zero. Con `diversify=false` si disabilita MMR, non necessariamente l'esplorazione. [RecoMmr](src/Pickarr.Core/Recommendations/RecoMmr.cs), [query](src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs).

L'esplorazione default 10% sceglie fino a 40 candidati tra i primi 200 per prior, eleggibili e fuori dai primi 40 del prefisso; preferisce anche titoli con almeno un genere non positivo nel profilo, o senza genere. L'iniezione scarta quelli già nel prefisso, sostituisce `floor(lunghezza_prefisso × rate)` posizioni se disponibili, e usa un seed basato sulla versione. I titoli sostituiti possono rientrare dopo il prefisso. Non garantisce una quota per pagina o che un candidato abbia score personale medio-alto. I titoli esplorativi già presenti nel ranking recuperano le loro motivazioni normali: non è garantita la motivazione `explore`. [RecoExplore](src/Pickarr.Core/Recommendations/RecoExplore.cs), [query](src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs).

Le motivazioni `similarTo` del build derivano principalmente dalla **similarità sparse**, non necessariamente dai vicini embedding che hanno contribuito al kNN; possono anche riferirsi al grafo TMDB. I termini sono sovrapposizioni positive, mentre `popular` può essere un fallback. Nel cold start con prior familiare viene comunque indicato solo `popular`: la spiegazione non rende necessariamente conto di tutti i contributi effettivi. [RecoReasons](src/Pickarr.Core/Recommendations/RecoReasons.cs).

## 4. Criticità verificate e impatto possibile

### P0 — Valutazione e attivazione del classificatore

In [RecoEvalService](src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs), ogni rating tra i primi 50 per ID diventa held-out, ma `Collect` tratta sempre il suo score come `pos`, **anche per Dislike e Not interested**, e lo confronta con fino a 40 titoli non valutati. Un dislike ben penalizzato può quindi peggiorare AUC e hit@10. I non valutati non sono negativi certi; hit@10 su circa 41 titoli non è hit@10 sull'intero catalogo. Confronti in parità sono favoriti da hit@10; se non ci sono campioni, AUC restituisce 0,5. La metrica separata Like-vs-Dislike usa invece le etichette dei fold.

Ulteriori limiti:

- valutazione solo dell'admin e dei rating nel pool, non di ogni profilo; max 50 scelti per ID, non per tempo o campionamento rappresentativo;
- usa pesi espliciti default e ignora impliciti, preferenze reali, prior familiare, amplificatore TMDB, filtri di lettura, MMR ed esplorazione: il `combined` valutato non è il sistema servito completo;
- training del livello 3 usa feature sparse derivate da un profilo che include i rating dei propri esempi: possibile **self-leakage**. La validazione rimuove il titolo held-out dal profilo, ma le feature dei restanti esempi di training non sono cross-fitted;
- quando esiste L2, l'attivazione confronta AUC di L3 con **L2 puro**, non con il `combined` finale che sostituirebbe; i punteggi L3 valutati non sono fusi con il prior come nel build;
- la soglia per classe conta solo `Dislike` come negativo, mentre la costruzione degli esempi include anche `Not interested` nella classe negativa. La valutazione è manuale e aggiorna uno stato che il build legge.

**Raccomandazione:** non usare i numeri attuali come prova di qualità né come unico criterio di attivazione. Priorità alla correzione di etichette, parità, campionamento e comparatore; generare feature di training fuori dal fold per evitare che ogni etichetta influenzi la propria feature. Decidere separatamente la semantica di `Not interested`. Valutare per profilo e confrontare il **ranking completo effettivamente servito**, incluse le varianti con e senza reranking.

### P0 — `N_eff` può contare segnali che non personalizzano

Il build carica fatti di segnale globali, ma costruisce `titleVectors` e `embMap` solo per il pool abilitato; `BuildProfile` e kNN ignorano i segnali senza vettore. Anche il grafo richiede sorgente e destinazione nel pool. Tuttavia `N_eff` comprende tutti i segnali. Esempio: 30 Like solo su titoli non nel pool possono dare `N_eff=30`, azzerare l'apporto familiare e quello TMDB, ma lasciare il profilo sparse vuoto e il ranking sul prior. Se invece almeno un segnale è nel pool, pochi segnali utilizzabili possono ricevere un peso personale ingiustificatamente elevato. [Build](src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs), [sparse](src/Pickarr.Core/Recommendations/RecoSparse.cs), [kNN](src/Pickarr.Core/Recommendations/RecoKnn.cs).

**Raccomandazione:** mantenere candidati confinati al pool, ma costruire feature per `pool ∪ titoli con segnale` già presenti nel database; mantenere l'IDF riferito al pool e gestire termini mai visti in modo definito. In alternativa calcolare fiducia separata sulla massa effettivamente utilizzabile per ciascun componente. Testare fonte disabilitata, assenza di metadati/embedding e profilo familiare; non ampliare implicitamente il catalogo.

### P1 — Recall della ricerca Simili

La [selezione](src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs) raccoglie fino a 120 candidati prima di ordinare per similarità, di norma condividendo almeno un genere, senza ordinamento semantico esplicito prima del limite. Se meno di 20, ripiega su un prefisso del pool. Un vicino migliore fuori da quei 120 può non essere mai confrontato; titoli affini di generi diversi possono mancare. Se il titolo di partenza ha embedding, si cercano solo embedding dei candidati selezionati: non c'è fallback sparse per i candidati senza vettore, anche se il grafo può aggiungere altri titoli.

**Raccomandazione:** per pool domestici misurare prima una scansione esatta vettorizzata di tutti gli embedding eleggibili, più fallback sparse per i mancanti; solo se necessario introdurre un indice ANN locale, misurando recall@K rispetto alla scansione esatta e costo di aggiornamento. Conservare il grafo TMDB come segnale distinto e confrontare la lista completa, non solo il tempo.

### P1 — Normalizzazione, fiducia e limiti dei segnali

- Il min-max per pool dà dipendenza dagli estremi e dal mix delle fonti: cambiare pochi titoli può cambiare la scala e il peso relativo dei componenti. Con score identici si torna a 0,5. I 0,4/0,6 non sono una probabilità calibrata.
- Con kNN il limite di 24 privilegia forza e ID, non freschezza/diversità; molti Love possono escludere segnali negativi. Il damping per numero di vicini e il successivo min-max possono alterare l'effetto di una confidenza bassa.
- Disponibilità e completezza di generi, keyword, crediti, testo inglese ed embedding incidono sulla qualità: la soglia di copertura globale non garantisce copertura uniforme per tipo, lingua o singolo candidato.
- Il prior familiare non misura somiglianza tra profili; un gusto molto diverso di un familiare può influire sul cold start. In cold start la motivazione `popular` non chiarisce tale contributo.
- Gli archi TMDB sono conservati solo se il titolo raccomandato esiste già localmente al momento dell'arricchimento; il timestamp evita ulteriori fetch nel percorso ordinario. Titoli importati dopo possono quindi non ottenere subito archi retroattivi. [Enrichment](src/Pickarr.Infrastructure/Metadata/EnrichmentService.cs).

**Raccomandazione:** confrontare normalizzazioni robuste o calibrazione per componente, con fallback esplicito quando un componente manca; valutare selezione vicini stratificata per segno, recenza e varietà, e fiducia basata su massa e copertura effettiva. Testare un eventuale decadimento solo come esperimento: la preferenza per un film non diventa necessariamente falsa col passare del tempo. Esporre copertura e spiegazioni fedeli, senza identificare il familiare.

### P2 — Esplorazione e diversità

MMR a generi può penalizzare due opere diverse solo perché condividono generi; allo stesso tempo può non riconoscere franchise ripetitivi con etichette differenti. L'esplorazione selezionata per prior può sottrarre posti a titoli più pertinenti e non rappresenta necessariamente «generi mai visti». Il rischio dipende dalla distribuzione reale; non si può dedurre un peggioramento netto senza test.

**Raccomandazione:** misurare qualità e diversità prima/dopo reranking; confrontare un insieme di esplorazione tratto da una fascia minima di rilevanza personale e novità, con esposizione distribuita e reason coerente. Eventuali modifiche alla quota o a `RemovedAt` richiedono attenzione ai requisiti di prodotto.

## 5. Miglioramenti proposti ai modelli e ai segnali

| Proposta | Beneficio ipotizzato | Costo / cautela | Priorità |
|---|---|---|---|
| Valutazione corretta e segnali fuori pool | Misurazione affidabile e uso del gusto già disponibile | Richiede test accurati, non una nuova dipendenza | P0 |
| Ricerca Simili esatta, poi ANN solo se serve | Maggiore recall | Misurare latenza e RAM sul catalogo reale | P1 |
| Confidence per componente e pesi adattivi con shrinkage verso default | Ridurre influenza di componenti senza copertura o con pochi dati | Rischio overfitting con poche valutazioni; non ottimizzare per singolo utente su pochi esempi | P1 |
| Separare espliciti, intenzioni e rifiuti | Evitare che Add/download vengano equiparati a una visione apprezzata | Serve una definizione di prodotto per `Not interested`; non trattare hide come voto | P1 |
| Migliorare copertura e lingua dei metadati | Ridurre vettori poveri e input misti | Chiamate TMDB e tempi da misurare; rispettare limiti e condizioni dei fornitori | P1 |
| Regressione regolarizzata / learning-to-rank semplice, **solo dopo** validazione senza leakage | Apprendere combinazioni invece di fissare 0,4/0,6 | Pochi profili e label; rischio forte di overfitting e instabilità | P2 |
| Embedding multilingue o diverso | Potenziale beneficio su testi non inglesi | Nessuna superiorità garantita; dimensione, RAM, CPU e qualità vanno misurati | P2 |
| Collaborative filtering / modello sequenziale / LLM | Potrebbe catturare segnali non descritti dai metadati | Fino a 10 profili: matrice probabilmente molto sparsa; complessità e privacy; non giustificati senza dati | Non prioritario |

**Candidato concreto da confrontare**, non sostituzione consigliata a priori: [paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2), 384 dimensioni, con modello card che indica lunghezza massima 128. Può aiutare quando i testi non sono inglesi, ma è un modello diverso e potrebbe costare di più o perdere informazioni per il troncamento; misurare su titoli reali multilingue e hardware target. La sola modifica dell'ID non basta: l'attuale downloader/hash, tokenizer e dimensione/limite sono legati al modello corrente. Servono descriptor specifici, compatibilità tokenizer/ONNX, self-test con vettori di riferimento, versionamento e ricalcolo degli embedding senza toccare rating o database. [Modello corrente](src/Pickarr.Infrastructure/Metadata/Reco/RecoModelService.cs).

Per i pesi, iniziare con griglie ristrette e interpretabili (ad esempio confrontare `a/b` 1/0, 0,7/0,3, 0,4/0,6 e 0/1); sono **valori sperimentali, non nuove impostazioni raccomandate**. Confrontare anche prior-only, sparse-only, embedding-only, senza TMDB, e ranking finale con/senza MMR ed esplorazione. Scegliere iperparametri su validation e confermare su test temporale separato; evitare di regolare i pesi sulle stesse valutazioni usate per dichiarare il successo.

## 6. Piano di verifica pratico

1. **Baseline locale, senza segreti:** per profilo raccogliere conteggi di rating per classe, segnali impliciti attribuiti, massa utilizzabile e non utilizzabile, copertura embedding/testo/keyword/crediti, frazione di suggerimenti solo rimossi e latenza p50/p95. Non pubblicare titoli o dati personali fuori dall'installazione senza consenso.
2. **Test di regressione sintetici:** held-out positivo e negativo con ordine noto; nessun confronto positivo quando il voto è negativo; parità esplicite; titoli con segnali fuori pool; 25+ segnali misti; embeddings mancanti; migliore vicino oltre i primi 120; fonti rimosse e disabilitate; spiegazione coerente con ranking. Verificare che non cambino esclusioni e confini di prodotto.
3. **Offline temporale per profilo:** train prima del cutoff, validation e test successivi; ricostruire segnali, catalogo e feature come disponibili al cutoff dove la storia lo consente. Per i casi senza storia completa, dichiarare il limite. Valutare positivi futuri nel pool realmente eleggibile; misurare separatamente la discriminazione di negativi espliciti e non chiamare «negativi» tutti i non valutati.
4. **Metriche:** NDCG@10 e Recall/Hit@10 sul pool eleggibile completo, AUC Like/Love vs Dislike separata, copertura catalogo, diversità intra-lista, quota rimossi, latenza e memoria. Riportare numero di profili, eventi e positivi, intervalli di incertezza e risultati per cold/warming/personalized e film/serie. Campioni piccoli significano conclusioni preliminari, non cifre affidabili.
5. **Verifica reale volontaria:** mostrare confronti A/B o liste accoppiate ai membri della famiglia, con assegnazione riproducibile e poche esposizioni casuali controllate. Registrare localmente solo eventi necessari, esposizione effettiva nel viewport, posizione, versione, scope e azione successiva; definire finestra di attribuzione, deduplicazione e conservazione. «Non cliccato» non significa necessariamente dislike. Test piccoli non stabiliscono significatività automaticamente.
6. **Decisione di rilascio:** promuovere una variante solo se migliora le metriche prefissate rispetto al ranking completo corrente senza regressioni importanti di diversità, quota rimossi, latenza e stabilità; mantenere fallback e possibilità di confronto. Nessuna soglia di miglioramento numerica è dimostrabile dai dati attualmente disponibili.

Il logging eventi esiste ed è disabilitato di default; la UI definisce una funzione di invio, ma nell'attuale codice non risultano sue chiamate. Il servizio di rating può registrare voti anche fuori dalla pagina raccomandazioni e senza versione/rank: non basta abilitarlo per ottenere impression e conversioni attribuite correttamente. [API web](src/Pickarr.WebApp/src/api/recommendations.ts), [log](src/Pickarr.Infrastructure/Catalog/RecoEventLogService.cs), [rating](src/Pickarr.Infrastructure/Catalog/RatingService.cs).

## 7. Ordine consigliato

1. **Prima:** correggere valutazione e criterio di attivazione livello 3; verificare separatamente i profili. Fino a quel momento considerare sperimentale qualsiasi decisione basata sul report offline corrente.
2. **Poi:** correggere l'utilizzo dei segnali fuori pool e misurare il recall di Simili; mantenere invariato il perimetro dei candidati.
3. **Quindi:** raccogliere baseline e confrontare calibrazione, pesi, segnali, grafo e diversificazione con test temporali ed esposizioni volontarie.
4. **Infine:** provare modelli alternativi solo se i dati mostrano che la rappresentazione testuale è il collo di bottiglia. Valutare esplicitamente qualsiasi nuova esperienza per titoli rimossi o fuori catalogo come modifica di prodotto.

## Fonti esterne

- [Model card ufficiale all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2).
- [Model card ufficiale paraphrase-multilingual-MiniLM-L12-v2](https://huggingface.co/sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2).
- Gusak et al., [Time to Split](https://arxiv.org/abs/2507.16289): gli split temporali possono cambiare il confronto fra modelli; studio riferito a recommender sequenziali, principio utile anche per progettare qui una verifica realistica.
- Pereira et al., [On the Reliability of Sampling Strategies in Offline Recommender Evaluation](https://arxiv.org/abs/2508.05398): bias di esposizione e campionamento possono distorcere il confronto offline.
