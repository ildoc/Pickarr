# ARCHIVE — benchmark-raccomandazioni

Creato: 2026-11-23. Archivio append-only; stato corrente in `STATUS.md`.

## Indice delle sezioni

| ID | Data | Argomento/task | Motivazione | Destinazione |
| --- | --- | --- | --- | --- |
| A-001 | 2026-11-23 | Blocco T01, F-005 | Ripianificazione dello scope documentale | §A-001 |
| A-002 | 2026-11-23 | T02, correzione gate L3 e self-leakage; F-008 | Chiusura T02 dopo verifiche e trigger archiviazione (3 finding risolti) | §A-002 |
| A-003 | 2026-11-23 | T05, integrazione runner e chiusura F-004 | Nuovo finding risolto, trigger archiviazione già attivo | §A-003 |
| A-004 | 2026-11-23 | T06b, input e proxy temporale; F-010 | Nuovo finding risolto, trigger archiviazione già attivo | §A-004 |
| A-005 | 2026-11-23 | T06c/TR06, metriche e F-009 | Chiusura aggregatore e trigger finding risolti | §A-005 |
| A-006 | 2026-11-23 | T07, adapter multilingue isolato nel benchmark | Acceptance locale verificata; nessun cambio produzione | §A-006 |
| A-007 | 2026-11-23 | T08, misure locali parziali; F-011 | Traccia delle evidenze senza dichiarare chiusura dell'acceptance | §A-007 |
| A-008 | 2026-11-23 | T08, confronto locale full-pool ripetuto; F-011 | D-20 supera il cap D-19; acceptance locale verificata, cluster ancora bloccato | §A-008 |
| A-009 | 2026-11-24 | T08, rinuncia misure cluster e chiusura | D-21 approva esplicitamente il limite e modifica l'acceptance | §A-009 |
| A-010 | 2026-11-24 | T13, prima verifica indipendente: FAIL | Tre difetti di prodotto e confronto T11 non probante | §A-010 |
| A-011 | 2026-11-24 | Ciclo correttivo B1–B4 | Correzioni, regressioni e misure dei servizi reali | §A-011 |
| A-012 | 2026-11-24 | Seconda verifica indipendente T13: FAIL | Correttezza confermata; latenza Simili oltre target | §A-012 |
| A-013 | 2026-11-24 | Terza verifica indipendente T13: PASS | Requisito Simili rivisto con D-23 | §A-013 |

## A-001 — Blocco documentale T01

Prima dell'approvazione dell'estensione, T01 era BLOCKED e F-005 OPEN: `docs/recommendations.md` §17.3 descrive la valutazione offline ma non era esplicitamente nello scope di T01. Non sono stati modificati codice o test, né eseguiti test o build. Stato e finding originari sono registrati nel log di `STATUS.md`; analisi dettagliata in `ANALYSIS.md` §7 F-005.

## A-002 — T02: gate L3 e feature supervisionate

- Log T02: freshness verificata e F-008 trovato nel callsite rebuild; scope esteso solo a `RecoBuildService`/test e helper indispensabili. Gate su `Combined`, cross-fitting, test/documentazione completati; F-008 RESOLVED, F-001 resta OPEN per T06.
- T02 ha esteso lo scope ai callsite `RecoBuildService` e relativi test Infrastructure dopo aver provato F-008: il rebuild reale addestrava L3 usando il rating-target nelle feature sparse. Implementazione e motivazione: `PLAN.md` TR-02, `ANALYSIS.md` §7 T02.
- `RecoLevel3Training` usa cinque partizioni deterministiche condivise da evaluator e rebuild, escludendo dai segnali ogni partizione mentre calcola le sue feature; pesi e disponibilità componenti sono espliciti. La valutazione confronta L3 con `Combined`, richiede fold completi e almeno 10 fold; DTO invariato, nuovo `Level3SkipReason = "did_not_beat_combined"`.
- Prove red/green: col vecchio gate (`l2Metrics ?? combinedMetrics`) il controesempio sintetico promuoveva erroneamente L3 (`Level2 AUC 0.5`, `Level3 0.8`, `Combined 1.0`); col vecchio training che includeva tutti i segnali il test di self-leakage osservava feature diverse cambiando il rating-target. Entrambi i test passano con la correzione.
- Verifiche finali, SDK 10.0.403: Infrastructure `FullyQualifiedName~Reco` 48 PASS; Core `FullyQualifiedName~RecoEvalMetricsTests` 7 PASS; Integration `RecoAdminApiTests|RecommendationsApiTests` 6 PASS; `dotnet format` sui file C# T02 PASS; `git diff --check` PASS con avvisi CRLF. Dettagli comandi, iterazioni e limiti in `ANALYSIS.md` §7 T02.
- Nessun database dev/prod, cluster, provider esterno, app/job reale o benchmark reale usato. F-001 resta OPEN per TR-06; F-007 resta OPEN per collisioni Git su Windows. Punto di lavoro: HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`, hash finali in `SNAPSHOT-T02.md`.

## A-003 — T05: runner diagnostico e F-004

- F-004 rimaneva OPEN dopo T03/T04: `OpenBenchmark` non riservava l'output, mancava l'integrazione read-only del runner. T05 aggiunge `prepare` e `CreateBenchmarkOutput`, con rivalidazione al momento d'uso e creazione esclusiva; report aggregato senza righe personali/segreti. Test su sole fixture sintetiche confermano output tardivo/sidecar/percorso protetto rifiutati, scrittura input rifiutata e hash invariato.
- F-004 RESOLVED nelle precondizioni operative già documentate: directory private fidate, input standalone immutabile, tutte le root protette esplicite. Hardlink, alias/mount, short-name e race avversarie non vengono dichiarati risolti dai controlli path. Nessuna nuova autorizzazione cluster o accesso a snapshot privato in T05.
- T05 DONE dopo15 test PASS (7 snapshot preesistenti+8 preparazione), build0 warning/errori, format e diff check PASS. Prove canoniche per acceptance/comandi/limiti: `ANALYSIS.md` §7 T05 E05-01/02/03; codebase `SNAPSHOT-T05.md`. Nessuna qualità/ranking/modello alternativo o baseline reale misurata: solo diagnostica, qualità null anche con exit0.

## A-004 — T06b: input e proxy temporale

- F-010 rilevato prima dell'implementazione: catalogo e vettori non disponibili da TemporalInput; impliciti/libreria non ricostruibili storicamente. Risolto nel perimetro del proxy mediante lettura esplicita query-only, limitazioni e variante L3 non promossa; la storia incompleta non è risolta.
- Prove e vincoli: `ANALYSIS.md` §7 T06b E06b-01–03; snapshot `SNAPSHOT-T06b.md`. T06c resta da eseguire in contesto separato; T06/F-001/F-009 restano aperti.

## A-005 — T06c/TR06: metriche e integrazione

- F-009 RESOLVED: scomposizione T06a–c completata, acceptance originale riconciliata con prove sintetiche E06a-02, E06b-02 e E06c-04 in `ANALYSIS.md` §7. Il benchmark rimane esplicitamente proxy; nessuna pretesa di ranking storico fedele.
- F-001 resta OPEN: evaluator e gate di prodotto usano ancora fold leave-one-out campionati e non sono una valutazione temporale rappresentativa; l'esistenza di un benchmark proxy separato non dimostra l'equivalenza al ranking di produzione. T08 dovrà interpretare tali limiti, T13 verificare il dominio completo.
- Evidenze, comandi e limiti: `ANALYSIS.md` §7 T06c; manifest `SNAPSHOT-T06c.md`. Nessuna misurazione reale o verifica finale indipendente.

## A-006 — T07: adapter multilingue

- Adapter limitato al runner: `Tokenizers.HuggingFace` carica l'esatto `tokenizer.json`; revisioni e SHA-256 di ONNX/tokenizer sono fissati e controllati, insieme a special token, firma ONNX, dimensione e limite sequenza. Non si applica al modello di produzione.
- Prove red/green locali su artefatti pubblici fissati: sei hash golden tokenizer multilingui, input effettivi identici per i due modelli, embedding a 384 dimensioni con norma L2 unitaria e ripetibilità cosine 1. Test standard senza download: 5 test mirati e 87 test Infrastructure `Reco` PASS; build/format PASS.
- Evidenze, comandi, hash completi e limiti: `ANALYSIS.md` §7 T07 e `SNAPSHOT-T07.md`. Nessuna misura di qualità/performance, benchmark snapshot, invio di testi o promozione modello; T08 resta TODO.

## A-007 — T08: misure locali parziali e F-011

- Report privato del solo snapshot T04 con input hash invariato; conteggi per profilo/classe, throughput, latenza e working set registrati in `ANALYSIS.md` §7 T08. Report e receipt restano fuori repository e protetti.
- Con il cap 1.024/33.894 la metrica di ranking embedding è indisponibile per copertura incompleta; i valori combined con fallback non attestano qualità del modello. Il runner è stato corretto per rendere indisponibile il confronto/exit 0 senza metriche complete, ma il report storico non è stato alterato.
- TR-08 resta BLOCKED: confronto qualità con copertura valida e misure cluster (con distinta autorizzazione operativa) non eseguiti. Nessuna selezione o promozione; F-011 OPEN. Evidenze e limiti: `ANALYSIS.md` §7 T08; manifest `SNAPSHOT-T08.md`.

## A-008 — T08: confronto locale full-pool e ripetibilità

- D-20 autorizza il superamento locale del cap D-19 sul solo snapshot T04. Il runner produce embedding freschi dai due artefatti fissati per tutti i 33.894 titoli del pool; i vettori restano in memoria e non vengono scritti nel DB. Self-test e SHA degli artefatti PASS.
- Due esecuzioni con seed 42, snapshot, cutoff e protocollo `temporal-v1` identici hanno copertura embedding completa e metriche di qualità identiche. Entrambi i report restano privati fuori repository; input ReadOnly, hash pre/post e ACL input/output verificati; report parziale precedente intatto.
- I risultati sono descrittivi su un solo profilo: 318 rating correnti e 369 eventi, split 221/73/75. Recall@10 embedding = 0 in validation/test per entrambi; combined test Recall@10 = 1/69 per entrambi. AUC dipende da classe e fold; nessuna selezione/promozione o affermazione di superiorità.
- Prestazioni per singolo host e due run: current 104–108 titoli/s, p50/p95 embedding 8,3–8,7/16,8–18,2 ms, picco working set ~596 MiB; multilingual 46,8–54,1 titoli/s, 18,1–20,2/26,7–40,5 ms, picco ~935–968 MiB. Limiti, ranking, CPU e tempi di generazione completi sono in `ANALYSIS.md` §7 T08; non confrontare con cluster.
- F-011 RESOLVED per la copertura; F-001 resta OPEN. T08 locale completato, ma TR-08 rimane BLOCKED in attesa di ambiente isolato e autorizzazione operativa separata per il cluster prevista dal piano. Nessun accesso cluster/pod/live, provider, API o job; T13 non eseguita. Verifiche: 18 test mirati, format e `git diff --check` PASS. Manifest aggiornato in `SNAPSHOT-T08.md`.

## A-009 — T08: chiusura con rinuncia approvata

- Il 2026-11-24 l'utente ha esplicitamente confermato D-21: rinuncia alle misure in ambiente simile al cluster per TR-08 e accetta il rischio che prestazioni e consumi in produzione restino ignoti. Il precedente stato BLOCKED di A-008 era corretto rispetto al piano allora vigente.
- TR-08 è DONE sul piano modificato: restano valide le due misure locali documentate in A-008 e ANALYSIS §7, senza pretese di equivalenza tra hardware o limiti locali e cluster. F-001 resta OPEN; T13 non eseguita; nessuna selezione o promozione modello.
- Prima della chiusura sono stati ricontrollati gli hash dei sei file D-20 e dei due report privati. Nessun nuovo benchmark, accesso al database o al cluster. L'eventuale accesso futuro al cluster richiede sempre conferma operativa separata. Il nuovo manifest è in `SNAPSHOT-T08.md`.

## A-010 — T13: prima verifica indipendente, FAIL

Revisore `revisore-tr13` (`a967d964-e495-42c6-9b2d-eef3d178ffa9`), contesto separato in sola lettura. Mandato: richiesta originale, R-01–09, modifiche approvate e acceptance TR-13. Il revisore ha esaminato codice, fixture e asserzioni, inclusi i nuovi file, senza eseguire il prodotto o modificare la versione. L'esito dipende da difetti verificabili nel codice, non dalla mancata riapertura dello snapshot privato.

| Requisito | Esito | Evidenza e limiti |
| --- | --- | --- |
| R-01 Conservazione | PASS nel perimetro verificabile | Origine read-only, backup SQLite compatibile con WAL, destinazione nuova e controlli integrità/FK. Per T04 reale solo evidenza documentale, non prova di identità byte per byte del live. |
| R-02 Isolamento | PASS | Input query-only, rifiuto di sidecar e destinazioni protette o esistenti, test sintetici anche su WAL. |
| R-03 Modelli | PASS locale | Tokenizer e artefatti fissati, golden e controlli vettori; confronto full-pool documentale, nessuna promozione. |
| R-04 Valutazione | FAIL | Gate contro combined ed esclusione target presenti, ma training kNN degenere (B1). |
| R-05 Segnali fuori pool | FAIL | Pool candidati preservato, ma segnali utilizzabili esclusi (B2). |
| R-06 Simili | FAIL | Rimosso il cap embedding, non garantito il top-k misto (B3). |
| R-07 Prima/dopo | FAIL | Il proxy temporale non misura gli interventi T09/T10 (B4). |
| R-08 Compatibilità | FAIL comportamentale | Nessuna rottura HTTP emersa; specifica fuori pool non conforme al requisito e fallback non esatto. |
| R-09 Verifica finale | FAIL | Revisione effettuata, ma mancano le condizioni per PASS. |

### Blocchi e criteri per il secondo tentativo

1. **B1 — Training L3 (T02).** `RecoLevel3Training.cs:45–81` passa a `RecoKnn.ScoreAll` i soli embedding dei target held-out, mentre i segnali escludono proprio quei target (`RecoKnn.cs:41–43,77–80`). Nessun vicino può contribuire: feature sempre 0,5 in evaluator e rebuild. Separare vettori target/vicini; il voto del vicino deve cambiare la feature del target, il voto del target no.
2. **B2 — Segnali utilizzabili (T09/T12).** `RecoBuildService.cs:89–108,149–156,174–184` carica feature solo per il pool; `RecoSignals.cs:83–95` filtra il dizionario dei segnali. `RecoBuildServiceTests.cs:559–619` verifica l'esclusione, non l'influenza richiesta. Provare che un voto esterno con feature condivise cambia il ranking interno, a candidati e IDF invariati, e che un segnale inutilizzabile non aumenta la fiducia. Riallineare la specifica.
3. **B3 — Top-k misto Simili (T10).** `RecoQueryService.cs:425–434,906–923` tronca la lista sparse prima di escludere chi ha embedding. Con limite 1, se il primo sparse A ha embedding da 0,60 e il secondo B non ha embedding ma vale 0,90, B non arriva al merge. Il test Core oltre 120 verifica solo `TopSimilar`; la fixture API non semina embedding misti. Applicare top-k ai candidati senza vettore mantenendo l'IDF previsto; confrontare il servizio con un oracle indipendente con copertura mista, filtri, grafo e vincitore oltre 120.
4. **B4 — Misure delle correzioni (T11).** `ModelComparisonRunner.cs:293–304` cronometra `TemporalBenchmark.Evaluate`, non `QuerySimilarAsync`. `TemporalRanking.cs:76–85,166–185` calcola `nEff` nel replay senza attraversare il percorso T09. Ripetere il prima/dopo locale a modello fisso esercitando i percorsi modificati; dichiarare campioni interessati e misurare recall e latenza Simili separatamente. Campione zero non significa equivalenza. D-21 rinvia solo le misure cluster T08, non queste prove.

**Versione e prove del coordinatore.** HEAD e fingerprint in `STATUS.md` §9. Digest dei 49 file fisici ottenuto unendo `git diff HEAD --name-only` e `git ls-files --others --exclude-standard`, escludendo `_skill_memory`, normalizzando i percorsi Windows in minuscolo e deduplicandoli. SHA-256 delle righe UTF-8 ordinate per percorso: `percorso<TAB>sha256-dei-byte-in-minuscolo<LF>`. 48/49 hash corrispondono alla catena di manifest: per `RecoKnnTests.cs`, aggiornato in T11 senza override, SHA-256 verificato `DFEEBE8AF004BAE6BFC31B944A249D78EDA80EEB53DC891F43EBD77F49B019CC`. Non è il motivo del FAIL.

Windows, SDK 10.0.403, VSTest/xUnit 2: `dotnet test` con `--no-restore`, filtro `FullyQualifiedName~Reco` (Core 51, Infrastructure 95), `FullyQualifiedName~RecommendationsApiTests|FullyQualifiedName~RecoAdminApiTests` (Integration 6): **152 PASS**, zero skipped. Suite mirate, non una certificazione di prodotto. Report privati T08/T11 e misure su un solo profilo non rieseguiti. Nessun accesso privato, cluster, provider o commit in T13. Dopo B1–B4 serve una nuova verifica indipendente su nuova versione identificata.

## A-011 — Ciclo correttivo dopo A-010

- B1: separati target e vicini kNN nei cinque fold; test rosso/verde sul voto vicino e sul voto target. B2: sparse esterno proiettato sul vocabolario in-pool e vettori locali compatibili senza ampliare candidati/IDF. B3: classificazione completa dei candidati prima del limite, dopo fallback e grafo; test oracle di servizio con filtri. Test `~Reco`: Core 52 e Infrastructure 101 PASS; Integration suite 73 PASS dopo la modifica SQLite.
- B4 sintetico, stesso harness Release sui servizi T08 e corretti: voto esterno con genere condiviso sposta il vincitore 132°→1°; voto senza termini condivisi `N_eff` 1→0. Quattro query Simili, recall complessiva 2/6→6/6 slot. Nessun confronto tra modelli in questo esperimento.
- B4 reale locale D-22: input T04, SHA-256 pre/post invariato, 0 file scratch residui. Report privati `T13-pathbench-t08.json` (`2BB1E8819231BFB0ECB3798CBB9FF855E3D48AE63A26105731FBE70B95D4F5D8`) e `T13-pathbench-corrected.json` (`EBE373D83F25608E0AA45EEB7B07BD3667CF52F945836A505EB8B829A7239DE9`). Baseline da HEAD + delta T02 con hash rebuild conforme a T05 ereditato da T08. Modello, osservazione, impostazioni default, hardware, corpus e procedura identici. La configurazione EF originaria supera il limite variabili SQLite su 33.894 titoli: stesso binding JSON su entrambi i lati, introdotto anche nella configurazione SQLite del prodotto; non si confronta il binding legacy.
- Un solo profilo, **zero voti espliciti esterni**: nessuna conclusione di efficacia reale per quel sottoinsieme; `N_eff` 372,25 invariato. Rebuild 12,09→11,89 s (una misura, non indicativa di vantaggio). Simili: 8 query prive di embedding, recall@10 39/80→80/80, p50 60→1309 ms, p95 86→1941 ms; 8 con embedding, 0/80→80/80, p50 53→1520 ms, p95 67→2632 ms. Cinque chiamate dopo warm-up per query; ordine esatto 0/16→16/16. Picco processo 470→659 MiB (include oracle). **Costo di latenza sostanziale**: non dedurre SLA cluster o generalizzare il campione. Mantenere recall esatta e modello corrente; eventuale ottimizzazione da misurare contro lo stesso oracle.
- Versione candidata secondo metodo A-010: HEAD invariato, 60 file fisici non-KB cambiati o nuovi, SHA-256 `9e211afb64b8aa582e8ccbf9fdd8c329aa6621a2cad8aead35608a42feb8954d`. Misure del coordinatore, non verdetto indipendente.

## A-012 — Seconda verifica indipendente T13: FAIL

Lo stesso revisore indipendente ha ricalcolato il digest A-011 e verificato codice, fixture e percorso cronometrato. Accetta B1–B3 per correttezza e B4 per metodo; nessun nuovo blocco privacy/API. I 226 test e le misure T04 sono del coordinatore; il revisore non ha aperto dati privati. Il p95 locale del solo servizio supera già il target dell'endpoint (<300 ms, `docs/recommendations.md` §4): 1941 ms senza embedding e 2632 ms con embedding, pur mantenendo ordine esatto in 16/16 query. Non sono misure HTTP o cluster. D-21/D-22 non derogano al requisito. R08 prestazionale e gate R09 restano aperti (F-014): ottimizzare senza ridurre il recall e rimisurare, oppure ottenere una deroga esplicita prima di un nuovo verdetto.

## A-013 — Terza verifica indipendente T13: PASS

Il revisore ha verificato il delta D-23 rispetto ad A-012: aprire il dettaglio non avvia Simili; il pulsante avvia la richiesta, mostra uno stato di caricamento accessibile e mantiene l'ordine del servizio. Specifica, guida, i18n e test coerenti. Candidato identificato in STATUS §9. Evidenze del coordinatore: SimilarRow 3/3 PASS, typecheck PASS, lint senza errori. Nessun accesso privato del revisore. F-014 non blocca più la chiusura: il limite p95 <300 ms resta per `GET /recommendations`, non per Simili. Nessuna nuova misura di quell'endpoint o riduzione dichiarata della latenza Simili.
