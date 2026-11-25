# PLAN — benchmark-raccomandazioni

Requisiti canonici: `ANALYSIS.md` §1; stati, dipendenze e stime solo in `STATUS.md` §1.

## TR-01 — Metriche e fold

### T01 — Correggere metriche e costruzione dei fold

- Scope: `src/Pickarr.Core/Recommendations/RecoEvalMetrics.cs`, `RecoEvalModels.cs`; `src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs`; rispettivi test `RecoEvalMetricsTests` e `RecoEvalServiceTests`; `docs/recommendations.md` §17.3 e guide direttamente dipendenti solo se necessarie, secondo `docs/AGENTS.md`. Estensione approvata dall'utente, `STATUS.md` D-07.
- Requisiti: R-04; finding F-001.
- Acceptance: solo Love/Like sono positivi nelle metriche di retrieval; Dislike e NotInterested sono riportati separatamente, non come positivi né come non valutati; pareggi e campioni vuoti hanno semantica esplicita; conteggi e campioni effettivi sono riportati; test con punteggi controllati falliscono sulla vecchia logica; documentazione direttamente dipendente coerente con la semantica effettivamente implementata.
- Verifica: test Core/Infrastructure mirati `FullyQualifiedName~RecoEval`, con output e casi di parità/assenza classi.
- Assunzioni: K-01; nessuna inferenza che i non valutati siano dislike.
- Rollback: ripristino selettivo dei soli cambi propri secondo diff; non toccare database.

## TR-02 — Gate livello 3

### T02 — Correggere confronto e self-leakage

- Scope: `RecoEvalService`, `RecoLevel3`, `RecoLevel3Model`, `RecoBuildService` e soli helper Infrastructure strettamente necessari; test Core/Infrastructure e Integration; `docs/recommendations.md` direttamente dipendente. Estensione a `RecoBuildService` e relativi test motivata da F-008 in `ANALYSIS.md` §7: il rebuild reale addestra L3 con feature che includono il target.
- Requisiti: R-04, R-08; finding F-001.
- Acceptance: feature di ciascun esempio supervisionato sono calcolate su profilo/i senza il suo rating-target, sia in eval sia nel rebuild reale; sono espliciti score/fusione e disponibilità dei componenti utilizzati dal reranker; confronto L3 contro score combinato effettivo sugli stessi fold e gli stessi candidati; senza dati/classi o almeno 10 fold L3 validi non viene promosso. Mantenere DTO/tipi esistenti; l'eventuale nuovo valore di `Level3SkipReason` va esplicitato e documentato. Test regressivi di self-leakage, baseline combinata superiore, classi/campioni/fold insufficienti e training reale.
- Verifica: test mirati Core metriche, Infrastructure eval/rebuild/modello e Integration API; caso sintetico con L2 inferiore ma score combinato superiore non attiva L3; registrare eventuali controlli non eseguibili.
- Assunzioni: K-01; la validazione offline non prova miglioramento online.
- Rollback: ripristinare solo le modifiche proprie; nessuna operazione di reset dati o valutazione sulla produzione.

## TR-03 — Sicurezza e isolamento snapshot

### T03 — Definire e testare acquisizione e isolamento

- Scope: progetto/utility benchmark da introdurre, test Infrastructure sintetici, guida operativa benchmark sotto `docs/developer/` quando definita; nessuna connessione alla produzione.
- Requisiti: R-01, R-02; finding F-004; decisioni D-04/D-05.
- Acceptance: procedura di backup online SQLite coerente con WAL su destinazione nuova e separata; verifiche integrità, conteggi aggregati e integrità referenziale sulla copia; runner apre solo snapshot esplicito in sola lettura e rifiuta output coincidenti con input/percorsi protetti. Nessun avvio API, migrazione, job, chiamata Watchmode/TMDB o lettura/log di secrets. Test su DB sintetico con scritture WAL dimostrano snapshot consistente e input invariato; errori espliciti.
- Verifica: test mirati su SQLite temporaneo e ispezione dei percorsi risolti; registrare comandi ed evidenze aggregate.
- Assunzioni: presenza utility/permessi nel pod non verificata; nessuna installazione o modifica sul pod senza autorizzazione.
- Rollback: rimuovere soltanto output sintetici nominati; mai originali, volumi, sidecar o directory condivise.

## TR-04 — Acquisizione copia di produzione

### T04 — Acquisire snapshot autorizzato

- Scope: solo operazioni necessarie a identificazione pod/volume, backup coerente, trasferimento e verifica su destinazione protetta fuori repository; nessuna modifica a sorgente/schema/job.
- Requisiti: R-01, R-02; D-04/D-05; F-004.
- Acceptance: prima di qualsiasi accesso al cluster chiedere conferma esplicita mostrando contesto/namespace/pod, origine e destinazione, strumento, permessi, spazio e impatto previsti; non stampare contenuti sensibili. Eseguire backup verificato secondo T03, verificare integrità e conteggi aggregati di titoli/profili/rating, confrontare segnali di conservazione prima/dopo senza esportare dati personali; registrare percorso protetto e limitazioni, non contenuti. Se backup sicuro non possibile, fermarsi BLOCKED e chiedere alternativa.
- Verifica: integrità e conteggi sulla copia; evidenza che l'origine non sia stata sostituita/modificata intenzionalmente; non affermare uguaglianza byte se l'app continua a scrivere.
- Assunzioni: K-05; cluster, pod, strumenti e dimensioni effettivi da verificare dopo conferma.
- Rollback: se fallisce, conservare originale intatto; eliminare solo file temporanei di destinazione esplicitamente identificati, previa verifica.

## TR-05 — Runner

### T05 — Implementare runner e fixture sintetica

- Scope: nuovo progetto `tools/Pickarr.RecoBench/` o collocazione verificata equivalente, `Pickarr.slnx`, eventuali test dedicati, `Directory.Packages.props` solo se necessario.
- Requisiti: R-01, R-02.
- Acceptance: CLI richiede input/output espliciti, applica garanzie T03, rifiuta percorsi coincidenti e utilizza fixture sintetica senza rete; report machine-readable include versione codice/configurazione, dataset fingerprint non sensibile, seed, copertura e ambiente, senza dati personali o segreti; errori e campioni insufficienti espliciti.
- Verifica: build e test progetto su fixture con casi input assente/output pericoloso e ripetibilità; nessun accesso a DB sviluppatore.
- Assunzioni: riuso di algoritmi esistenti senza introdurre dipendenze inutili.
- Rollback: rimuovere soltanto file creati dal task dopo verifica diff; preservare dati di input.

## TR-06 — Valutazione benchmark

### T06 — Implementare split e metriche comparabili

- Scope: runner T05, funzioni pure condivise solo se necessario, test mirati.
- Requisiti: R-02, R-04, R-07; F-001.
- Acceptance: split temporale per profilo con train/validation/test distinti e seed; etichette esplicite, esclusioni e ranking del pool eleggibile, baseline prior/sparse/embedding/combinato e variante rerank; NDCG/Recall o Hit@10 su pool completo, discriminazione like/dislike separata, copertura e numero di eventi/profili. Nessun uso di target futuro nelle feature addestrate; quando catalogo/feature storici non sono ricostruibili, marcare l'approssimazione e non presentarla come backtest perfetto.
- Verifica: fixture con futuro deliberatamente alterato, negativi e pareggi; attesi calcolati a mano; confronti ripetibili.
- Assunzioni: K-06; definire e registrare protocollo concreto prima di misurare.
- Rollback: cambi propri selettivi; nessuna scrittura sullo snapshot.

#### Scomposizione approvata di T06 (F-009; D-13)

L'acceptance T06 sopra rimane interamente obbligatoria; i sottotask non autorizzano T07/T08 o modifiche al prodotto.

##### T06a — Protocollo e storia temporale
- Scope: runner T05, fixture e test dedicati; eventuale riferimento a Core, senza migrazioni o job.
- Acceptance: protocollo registrato prima delle misure, split train/validation/test temporale per profilo, gestione esplicita di update/delete e timestamp coincidenti, nessun valore corrente retrodatato, seed e approssimazioni dichiarati; fixture sintetica prova l'isolamento dal futuro e casi di storia incompleta.
- Verifica: test mirati sulla ricostruzione e sui cutoff, input sintetico immutato; niente dati reali.

##### T06b — Baseline e ranking comparabili
- Scope: runner T05 e funzioni Core esistenti, test sintetici relativi.
- Acceptance: pool completo eleggibile e filtri coerenti, `RemovedAt` non escluso, baseline prior/sparse/embedding/combinato e rerank; stesso modelId per vettori e indisponibilità componenti esplicita; combinato fedele oppure proxy dichiarato, nessun segnale futuro nei profili o household.
- Verifica: test con negativi, vettori mancanti, pareggi, esclusioni e ranking completo.

##### T06c — Metriche, integrazione e verifiche
- Scope: CLI e report privato runner T05, fixture/test mirati, guida direttamente dipendente e KB.
- Acceptance: NDCG/Recall o Hit@10 su pool completo, discriminazione esplicita Like/Dislike separata, conteggi e copertura con denominatori, report aggregato senza dati personali, ripetibilità e garanzie T03/T05; prove per tutti i criteri T06.
- Verifica: attesi calcolati a mano, CLI su fixture con hash invariato e output protetto, build/format/diff, test preesistenti pertinenti; registrare limiti, evidenze e snapshot. Nessun DONE senza prove obbligatorie.

## TR-07 — Secondo modello

### T07 — Integrare modello multilingue nel solo benchmark

- Scope: runner, adapter ONNX/tokenizer, fixture/test e descrittore modello sperimentale; eventuali dipendenze centralizzate motivate.
- Requisiti: R-03.
- Acceptance: esegue modello corrente e `sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2` su stessi testi; vocab/tokenizer, input ONNX, pooling, dimensioni e lunghezza sono verificati rispetto a model card e artefatti specifici; versione/hash registrati, embedding di modelli diversi non mescolati; niente invio di testi o dati utente a servizi esterni; mancato modello segnalato. Nessun cambio automatico al modello di produzione.
- Verifica: self-test golden verificabili, dimensione e normalizzazione, test linguistici e confronto con implementazione di riferimento; test standard senza download.
- Assunzioni: disponibilità ONNX/quantizzazione e tokenizer compatibile non dimostrata; se incompatibili fermarsi e proporre alternativa, senza cambiare modello implicitamente.
- Rollback: eliminare solo artefatti sperimentali identificati; modello/dati di produzione invariati.

## TR-08 — Baseline e confronto

### T08 — Misurare baseline reale e alternative

- Scope: runner, snapshot autorizzato T04 in sola lettura, output protetti; benchmark locale. D-21: l'utente rinuncia alle misure in ambiente simile al cluster per TR-08, originariamente previste dopo quelle locali.
- Requisiti: R-02, R-03, R-07.
- Acceptance: registrare conteggi reali per profilo/classe, coperture, numero risultati, incertezze e limiti; misurare throughput embedding, tempo totale, CPU/RAM di picco e latenza p50/p95 delle operazioni definite, warm-up e ripetizioni, hardware/limiti; confrontare baseline e modello alternativo su stesso snapshot/protocollo; nessuna metrica di qualità inventata né promozione automatica. Test cluster solo in ambiente isolato e con approvazione operativa separata, senza carico nel pod di produzione.
- Verifica: ripetere le misure locali e confrontare report/seed; documentare sottoinsiemi piccoli e limiti. Per D-21 non si richiedono misure cluster per chiudere TR-08: le prestazioni cluster restano non misurate e non deducibili da quelle locali. Eventuali futuri test cluster richiedono ambiente isolato e approvazione operativa separata, senza carico nel pod di produzione.
- Assunzioni: K-05, K-06, K-07.
- Rollback: nessun intervento su sorgente; pulire solo output temporanei nominati se autorizzato.

## TR-09 — Segnali fuori pool

### T09 — Coerenza tra segnali e fiducia

- Scope: `RecoBuildService`, `RecoSparse`, `RecoKnn`, eventuale servizio embedding; test Core/Infrastructure, spec e guide direttamente impattate.
- Requisiti: R-01, R-05, R-08; F-002.
- Acceptance: generare feature anche per titoli con segnale già presenti fuori dalle fonti abilitate, mantenendo IDF riferito al pool e comportamento definito per termini non presenti; massa/fiducia riflette dati effettivamente utilizzabili e fallback robusto per feature mancanti; candidati restano solo nel pool esistente. Preservare ranking per profilo, rating, favoriti e DB senza reset/migrazione distruttiva.
- Verifica: test con segnali solo fuori pool, misti, senza metadati/embedding, due profili e fonti disabilitate; dimostrare che nessun titolo fuori pool diventa candidato.
- Assunzioni: K-02; eventuale esigenza di nuove chiamate esterne o cambi di prodotto richiede chiarimento prima di procedere.
- Rollback: ripristino selettivo codice; eventuali migrazioni esclusivamente additive/data-preserving, nessuna cancellazione dati.

## TR-10 — Simili

### T10 — Ricerca esatta e fallback

- Scope: `RecoQueryService`, `RecoKnn`, test Core, Infrastructure e API.
- Requisiti: R-06, R-08; F-003.
- Acceptance: per tutti i candidati eleggibili esegue confronto esatto embeddings, con fallback sparse definito per titoli mancanti, e conserva comportamento del grafo e filtri; un vicino migliore oltre i primi 120 è trovato. Esplicitare scala/combinazione del fallback, assicurare ordinamento deterministico e non duplicare risultati; nessuna scoperta fuori pool.
- Verifica: confronto con oracle brute force su fixture inclusi embedding mancanti, filtri profilo/Arr e grafo; test API mirati; misurare latenza su snapshot per K-07.
- Assunzioni: esattezza può avere costo su 33.000 titoli; se non sostenibile, non ridurre recall di nascosto, proporre soluzione con misure e approvazione.
- Rollback: ripristino selettivo codice; dati persistenti invariati.

## TR-11 — Verifica comparativa

### T11 — Rieseguire benchmark e documentare risultati

- Scope: runner, snapshot e output T08; nessuna modifica al database.
- Requisiti: R-01, R-03, R-07.
- Acceptance: confronto prima/dopo su stesso snapshot, split e ambiente; qualità, recall Simili rispetto a oracle, tempi, memoria e campioni; separare effetto modello da effetto correzioni. Spiegare eventuali regressioni o incertezza e raccomandare scelta senza promozione automatica. Controllare integrità dello snapshot e nessuna nuova chiamata ai provider.
- Verifica: riesecuzione con seed e registrazione ambiente, confronto con T08; se i risultati non sono comparabili, segnalare e non dichiarare successo.
- Assunzioni: K-06/K-07.
- Rollback: solo output del benchmark esplicitamente identificati.

## TR-12 — Documentazione e integrazione

### T12 — Documentare uso, limiti e conservazione dati

- Scope: `docs/developer/`, `docs/user/` e `docs/recommendations.md` secondo impatto; eventuali test/documentazione API.
- Requisiti: R-01, R-08.
- Acceptance: guida inglese a snapshot protetto, uso runner, isolamento, modelli e interpretazione metriche; aggiornare specifiche e guide per comportamenti realmente modificati, includendo eventuali FR/BR/D-ID; nessun segreto o dato personale, istruzioni coerenti con WAL e senza reset. Conservare comportamento API e fonti salvo modifiche approvate.
- Verifica: controllare link e comandi applicabili; test API e format/backend in base allo scope, registrando eventuali blocchi.
- Assunzioni: nessuna.
- Rollback: ripristino selettivo delle sole modifiche proprie.

## TR-13 — Verifica finale indipendente

### T13 — Verifica finale indipendente

- Scope: tutti i requisiti approvati, codice/test/configurazione, dati e output del benchmark accessibili in modo protetto.
- Requisiti: `ANALYSIS.md` §1 e modifiche approvate in §7.
- Acceptance: tutti i criteri obbligatori verificati su versione identificata, nessun difetto bloccante, rinvii approvati esplicitamente, rapporto indipendente PASS registrato in `STATUS.md` §9.
- Verifica: revisore in contesto separato con richiesta originale, requisiti, vincoli e versione; verifica requisito → codice → test, regressioni e integrazione; nessuna modifica del revisore. Se indisponibile, handoff a nuova sessione e task BLOCKED.
- Assunzioni: nessuna; accesso ai dati privati solo se autorizzato.
- Rollback: nessuna modifica; seguire il protocollo FAIL/INCONCLUSIVE della skill.
