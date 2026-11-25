# benchmark-raccomandazioni — STATUS

Ultimo aggiornamento: 2026-11-24; branch `main`; storico: `ARCHIVE.md`.

## 0. Quick Context

- Obiettivo: benchmark affidabile e correzioni prioritarie; requisiti `ANALYSIS.md` §1.
- Stato dominio: chiuso. Terzo tentativo T13: PASS indipendente sui requisiti rivisti con D-23.
- Codebase: HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`.
- Snapshot di consegna: `SNAPSHOT-T12.md`, con manifest ereditati e override; versione effettivamente in verifica in §9.
- Confronto T08 ripetuto due volte sul solo snapshot T04 locale; metriche embedding disponibili e identiche tra report. T08 DONE con esclusione approvata delle misure cluster, che rimangono non misurate.
- F-014 chiuso sul requisito rivisto; F-001 e F-007 restano limiti documentati.
- Input umano: attività future cluster/misure richiedono approvazione distinta. Snapshot privato e receipt aggregata fuori repository, percorsi e limiti in `ANALYSIS.md` §7 T04.
- Handoff: verifica conclusa. Nessuna autorizzazione cluster o promozione del modello.
- Verifica finale: §9.

## 1. Roadmap

| Tranche | ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TR-01 | T01 | Correggere metriche e fold | L | Alto | DONE | — | PLAN TR-01; D-07; ANALYSIS §7 |
| TR-02 | T02 | Correggere gate L3 e compatibilità | L | Alto | DONE | T01 | PLAN TR-02; ARCHIVE §A-002 |
| TR-03 | T03 | Progettare e verificare isolamento snapshot | L | Alto | DONE | — | PLAN TR-03; ANALYSIS §7 |
| TR-04 | T04 | Acquisire snapshot autorizzato | M | Alto | DONE | T03 | PLAN TR-04; D-11; ANALYSIS §7 T04; SNAPSHOT-T04-FINAL.md |
| TR-05 | T05 | Runner benchmark e fixture | L | Medio | DONE | T03 | PLAN TR-05; ANALYSIS §7 T05; SNAPSHOT-T05.md |
| TR-06 | T06 | Valutazione temporale e ranking, coordinamento acceptance | XL | Alto | DONE | T01, T05; chiusura dopo T06a–c | PLAN TR-06; ANALYSIS §7 T06c |
| TR-06 | T06a | Protocollo e storia temporale | L | Alto | DONE | T01, T05 | PLAN TR-06; ANALYSIS §7 T06a |
| TR-06 | T06b | Baseline e ranking comparabili | L | Alto | DONE | T06a | PLAN TR-06; ANALYSIS §7 T06b |
| TR-06 | T06c | Metriche, integrazione e verifiche | L | Alto | DONE | T06a, T06b | PLAN TR-06; ANALYSIS §7 T06c |
| TR-07 | T07 | Adapter modello multilingue | L | Medio | DONE | T05 | PLAN TR-07; ANALYSIS §7 T07 |
| TR-08 | T08 | Baseline completa e confronto modelli | L | Medio | DONE | T02, T04, T06, T07 | Confronto locale full-pool ripetibile; misure cluster non eseguite, esclusione e rischio approvati D-21 |
| TR-09 | T09 | Correggere segnali fuori pool | L | Alto | DONE | T08 | PLAN TR-09 |
| TR-10 | T10 | Correggere ricerca simili | L | Medio | DONE | T08 | PLAN TR-10 |
| TR-11 | T11 | Ripetere confronto post-correzioni | L | Medio | DONE | T09, T10 | PLAN TR-11 |
| TR-12 | T12 | Documentare uso e compatibilità | M | Medio | DONE | T02, T11 | PLAN TR-12 |
| TR-13 | T13 | Verifica finale indipendente | L | Alto | DONE | T01, T02, T03, T04, T05, T06, T07, T08, T09, T10, T11, T12 | PASS sul requisito rivisto; §9 |

## 2. Flussi

| ID | Stato | Nota |
| --- | --- | --- |
| FLUSSO-01 valutazione | IN_PROGRESS | Gate L3 corretto in T02; benchmark temporale proxy verificato, F-001 residuo |
| FLUSSO-02 ranking | DONE | F-002 verificato nel secondo tentativo |
| FLUSSO-03 simili | DONE | Top-k esatto; caricamento su richiesta verificato (D-23) |
| FLUSSO-04 snapshot/benchmark | IN_PROGRESS | T08/T11 locali completati con limiti e rinuncia cluster D-21; documentazione T12 completata |

## 3. Findings

| ID | Gravità | Sintesi | Task target | Stato |
| --- | --- | --- | --- | --- |
| F-001 | Alta | Evaluator e gate di prodotto rimangono non temporali e campionati; proxy non dimostra rappresentatività produttiva | T08/T13 (residuo dopo T01/T02/T06) | OPEN |
| F-002 | Alta | Segnali esterni utilizzabili esclusi dalla personalizzazione | T09 | RESOLVED: B2, A-011 |
| F-003 | Alta | Recall Simili non esatto con copertura embedding mista | T10 | RESOLVED: B3, A-011 |
| F-004 | Alta | Snapshot live WAL e dati personali richiedono protezione | T05 | RESOLVED: integrazione read-only/output esclusivo; limiti e prove ARCHIVE §A-003 |
| F-005 | Media | Scope documentale di T01 non previsto inizialmente | T01 | RESOLVED: D-07; archivio §A-001 |
| F-006 | Media | Contesto iniziale sincrono non riprendibile dopo approvazione del delta | T01 | RESOLVED |
| F-007 | Media | Git traccia coppie `PickArr`/`Pickarr` con collisione su Windows; le modifiche allo scope appaiono su entrambe | T01 | OPEN |
| F-008 | Alta | Anche il training L3 del rebuild usa feature calcolate includendo il rating-obiettivo | T02 | RESOLVED: cross-fitting condiviso in evaluator/rebuild; ANALYSIS §7; ARCHIVE §A-002 |
| F-009 | Alta | T06 eccede L; scomposizione e acceptance completate | T06a–c | RESOLVED: ARCHIVE §A-005 |
| F-010 | Alta | T06a non fornisce catalogo, vettori o training temporale L3; stato storico impliciti/libreria non ricostruibile | T06b–c | RESOLVED nel perimetro del proxy, limiti storici restano: ARCHIVE §A-004 |
| F-011 | Media | T08 limita i vettori a un campione; l'evaluator rende indisponibile la metrica embedding sull'intero pool, quindi non c'è confronto qualità fra modelli | T08/T11 | RESOLVED in T08 con vettori freschi completi; qualità descrittiva, un solo profilo e nessuna promozione |
| F-012 | Alta | Training L3 senza vicini kNN utilizzabili per costruzione | T02 | RESOLVED: B1, A-011 |
| F-013 | Alta | Il confronto T11 non esercita i percorsi T09/T10 | T11 | RESOLVED: B4, A-011 |
| F-014 | Alta | Simili esatto: p95 1941/2632 ms senza/con embedding | T10/T13 | Limite accettato solo per Simili con UX esplicita D-23; non è uno speedup |

## 4. Decisioni

| ID | Data | Decisione | Owner | Motivazione | Fonte |
| --- | --- | --- | --- | --- | --- |
| D-01 | 2026-11-23 | Nuovo dominio, benchmark e tre correzioni prioritarie | Utente | Scope misurabile | Gate |
| D-02 | 2026-11-23 | Confronto MiniLM corrente/multilingue, senza sostituzione automatica | Utente | Valutare qualità e costo | Gate; report §5 |
| D-03 | 2026-11-23 | Preservare KB comune e usare struttura/CI effettive | Utente | Convenzioni comuni obsolete | Gate; `.github/workflows/ci.yml` |
| D-04 | 2026-11-23 | Snapshot a cura assistente solo dopo conferma operativa; locale poi ambiente cluster isolato | Utente | Protezione dei dati e confronto prestazioni | Gate |
| D-05 | 2026-11-23 | Nessuna modifica o sostituzione del database originale | Utente | Preservare quota e preferenze | Richiesta; `AGENTS.md` |
| D-06 | 2026-11-23 | Piano approvato per esecuzione successiva | Utente | Procedere per tranche mantenendo conferme operative separate | Approvazione piano |
| D-07 | 2026-11-23 | Estendere T01 alla specifica delle metriche offline e relative guide direttamente dipendenti se necessarie | Utente | Evitare documentazione incoerente con la correzione | Conferma esplicita del delta |
| D-08 | 2026-11-23 | Usare cinque partizioni deterministiche di cross-fitting per le feature L3 in evaluator e rebuild, con pesi/disponibilità componenti espliciti | Esecutore T02 | Rimuovere il target dalle proprie feature in entrambi i percorsi e limitare il costo calcolando score grezzi sui soli target della partizione | F-008; ANALYSIS §7; test sintetici |
| D-09 | 2026-11-23 | Utility SQLite autonoma in `tools/Pickarr.RecoBench`, con backup online e validazione della copia; superficie read-only per T05 | Esecutore T03 | Isolare l'acquisizione dal runtime ed evitare dipendenze o runner anticipati | PLAN TR-03; ANALYSIS §7 |
| D-10 | 2026-11-23 | Autorizzata solo la prima ricognizione sul contesto Kubernetes selezionato; seconda conferma obbligatoria prima di scritture, backup o copia | Utente | Separare identificazione strumenti/risorse dall'accesso ai dati e dalle operazioni | Mandato TR04: `autorizza_ricognizione=true, contesto=Contesto attualmente selezionato` |
| D-11 | 2026-11-23 | Autorizzati backup e conteggi: helper revisionabile read-only testato su sintetici, staging privato, online backup e trasferimento/controlli alla destinazione privata proposta | Utente tramite orchestratore/ask_user | Seconda conferma esplicita, senza installazioni, restart, modifiche originali o provider; stop se incompatibilità Linux | `autorizza_backup=true`, `autorizza_conteggi=true`; procedura ANALYSIS §7 T04 |
| D-12 | 2026-11-23 | T05 solo preparazione/diagnostica, qualità indisponibile esplicita; seed registrato ma nessun algoritmo casuale; nessuna esecuzione su dati privati | Utente, mandato TR05 | Non anticipare T06/T07/T08 né inventare metriche o sufficienza statistica | «ok procedi con tr05»; ANALYSIS §7 T05 |
| D-13 | 2026-11-23 | Scomposizione T06 XL in T06a–c approvata; acceptance invariata | Utente | Separare replay temporale, ranking e verifica; avviare solo T06a | «Approva la scomposizione e avvia T06a»; ANALYSIS §7 T06 |
| D-14 | 2026-11-23 | Autorizzata prosecuzione fino a conclusione TR06, questo contesto esegue solo T06b | Utente | T06c affidato successivamente al parent in nuovo contesto | Mandato T06b |
| D-15 | 2026-11-23 | Usare input catalogo corrente esplicito e proxy dichiarato; L3 train-only non selezionato, nessuna promozione | Esecutore T06b | Ricostruzione storica incompleta; confronti senza future label | F-010; PLAN TR-06; ANALYSIS §7 T06b |
| D-16 | 2026-11-23 | CLI valuta tutte le varianti disponibili senza selezione; L3 senza trainer non disponibile; exit 3 sotto soglia diagnostica di 30 positivi test | Esecutore T06c | Evitare selezione su test e falsa sufficienza; soglia di warning Core non certifica significatività | ANALYSIS §7 T06c; `RecoEvalMetrics.LowSampleWarningThreshold` |
| D-17 | 2026-11-23 | Usare `Tokenizers.HuggingFace` solo nel runner per caricare il tokenizer JSON multilingue; fissare revisioni e hash degli artefatti | Esecutore T07 | Il tokenizer Unigram del candidato non è intercambiabile con il `vocab.txt` WordPiece di produzione; dipendenza verificata sui file fissati | PLAN TR-07; model card e artefatti Hugging Face; ANALYSIS §7 T07 |
| D-18 | 2026-11-23 | Eseguire T08 sul solo snapshot T04 autorizzato in ambiente locale; nessun accesso al cluster/pod o database live | Esecutore T08, su richiesta utente | Isolare i dati e non aumentare il carico dell'installazione; le misure cluster richiedono approvazione operativa distinta | «procedi con tr08»; V-02/V-03; PLAN TR-08 |
| D-19 | 2026-11-23 | Limitare il confronto qualitativo a un campione deterministico massimo di 1.024 titoli dell'overlap hash-matching; throughput su massimo 512 titoli | Esecutore T08 | Il passaggio completo su 6.128 vettori non avanza abbastanza rapidamente; il confronto resta proxy a copertura parziale, non certificazione | Feedback utente sul cambio strategia; avanzamento del primo run; ANALYSIS §7 T08 |
| D-20 | 2026-11-23 | Per completare il confronto qualitativo, generare localmente embedding freschi di entrambi i modelli sull'intero pool dello snapshot T04; mantenere il campione throughput a 512 e non accedere al cluster/pod o DB live | Utente | Il cap D-19 non raggiunge copertura; la metrica embedding richiede vettori per tutto il pool. La durata può essere di ore | «authorize_full_local»; D-18; PLAN TR-08; ANALYSIS §7 T08 |
| D-21 | 2026-11-24 | Rinunciare alle misure in ambiente simile al cluster per TR-08 e chiuderla sulla base delle misure locali; accettare che prestazioni e consumi di produzione rimangano non misurati | Utente | Lo snapshot è una copia del database di produzione; la provenienza dei dati non rende equivalenti hardware, limiti o carico | Conferma esplicita «waive» dopo spiegazione del limite; PLAN TR-08; ANALYSIS §7 T08 |

| D-22 | 2026-11-24 | Autorizzata misura locale su snapshot T04 con input RO, hash pre/post, dati di lavoro isolati per rebuild e soli risultati aggregati privati. Esclusi cluster, live e provider. | Utente | Misurare i percorsi effettivi per B4 | Ciclo correttivo T13, risposta `local_only` |

| D-23 | 2026-11-24 | Sostituire il target <300 ms per i soli Simili con pulsante e feedback immediato. Ordine esatto e vincolo per Raccomandazioni invariati. | Utente | Evitare lavoro all'apertura del dettaglio senza sacrificare recall | Risposta esplicita `replace`; ANALYSIS §7 |

## 5. Activity log

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-23 | INIT | Assessment, gate e creazione piano | Piano da approvare | ANALYSIS §1; PLAN |
| 2026-11-23 | APPROVAZIONE | Piano approvato dall'utente | Approvato | D-06 |
| 2026-11-23 | T01 | Verificata freshness e avviata TR-01 in contesto separato | IN_PROGRESS | HEAD e snapshot §0; PLAN TR-01 |
| 2026-11-23 | T01 | Rilevata documentazione direttamente dipendente fuori scope; nessuna modifica applicativa, verifica non avviata | BLOCKED; F-005 OPEN | ANALYSIS §7; `docs/recommendations.md` §17.3; PLAN TR-01 |
| 2026-11-23 | T01 | Estensione documentale approvata e piano aggiornato; storico preservato | F-005 RESOLVED; T01 TODO | D-07; ARCHIVE §A-001; PLAN TR-01 |
| 2026-11-23 | T01 | Ripresa TR-01 dopo sblocco | IN_PROGRESS | D-07; PLAN TR-01 |
| 2026-11-23 | T01 | Follow-up al contesto iniziale rifiutato: agente sincrono non riprendibile | BLOCKED; F-006 OPEN | `write_agent`; nuova sessione richiesta |
| 2026-11-23 | T01 | Nuovo contesto esecutivo separato disponibile; HEAD e worktree verificati | F-006 RESOLVED; T01 BLOCKED → TODO → IN_PROGRESS | Mandato utente; HEAD e snapshot §0 |
| 2026-11-23 | T01 | Individuate coppie di percorsi Git case-distinct ma coincidenti su filesystem Windows; blob iniziali identici | F-007 OPEN; nessuna modifica aggiuntiva intenzionale | `git ls-files -s` sui file di T01; ANALYSIS §7 |
| 2026-11-23 | T01 | Metriche, report, test e specifica aggiornati; controlli e hash registrati | DONE; F-001 resta OPEN | ANALYSIS §7; SNAPSHOT-T01.md |
| 2026-11-23 | T02 | Chiusura task; log ed evidenze archiviati | DONE; F-008 RESOLVED; F-001 resta OPEN per T06 | ARCHIVE.md §A-002; ANALYSIS §7; SNAPSHOT-T02.md |
| 2026-11-23 | T03 | Verificati HEAD, indice vuoto e hash del manifest T02; avvio in contesto separato | IN_PROGRESS | SNAPSHOT-T02.md; PLAN TR-03; F-007 |
| 2026-11-23 | T03 | Backup, isolamento e guida verificati su dati sintetici; nessuna acquisizione reale | DONE; F-004 resta OPEN | ANALYSIS §7; SNAPSHOT-T03.md |
| 2026-11-23 | T04 | Freshness HEAD/32 hash T03/indice verificata; ricognizione autorizzata senza lettura contenuti DB né scritture sul pod | TODO → IN_PROGRESS | D-10; ANALYSIS §7 T04 |
| 2026-11-23 | T04 | Identificati pod/PVC/DB/spazio/runtime; sqlite3 e Python assenti. Procedura alternativa con helper T03 proposta, non eseguita; `write_agent` all'ID owner non instradabile | BLOCKED, seconda conferma necessaria; F-004 resta OPEN | ANALYSIS §7 T04; SNAPSHOT-T04.md; handoff all'orchestratore nel risultato finale |
| 2026-11-23 | T04 | Seconda conferma operativa esplicita acquisita; rivalidati 32 hash, HEAD, pod UID/Ready e spazio; helper conteggi in preparazione | BLOCKED → TODO → IN_PROGRESS | D-11; nessun avvio T05 |
| 2026-11-23 | T04 | Helper conteggi validato su sintetici e Linux; backup online, integrità/FK/conteggi copia e trasferimento binario/hash/ACL verificati. Conteggi origine prima/dopo uguali; inode DB stabile ma mtime/WAL variati; nessuna pretesa di immutabilità byte live | DONE; F-004 resta OPEN per T05 | ANALYSIS §7 T04; receipt aggregata privata; SNAPSHOT-T04-FINAL.md |
| 2026-11-23 | T04 | Rimossi solo helper/archive/copia staging creati, conservata copia Windows read-only privata; controlli format/diff e preservazione modifiche ereditate completati | PASS con avvisi workspace/CRLF documentati; nessun commit/T05 | ANALYSIS §7 T04; checklist §7 |
| 2026-11-23 | T05 | Autorizzazione utente; contesto separato con KB e materiali ereditati. HEAD atteso, 33/33 hash T03+override T04-FINAL corrispondenti, staged vuoto; diff e nuovi ispezionati | IN_PROGRESS; nessun accesso a DB privati/cluster | PLAN TR-05; SNAPSHOT-T04-FINAL.md; nessun dubbio sostanziale residuo sul mandato diagnostico |
| 2026-11-23 | T05 | Runner diagnostico/fixture/guida verificati; 15 test sintetici/CLI PASS, build e format PASS, input hash invariato e output ripetibile; manifest finale e checklist aggiornati | DONE; F-004 OPEN → RESOLVED e archiviato, nessun T06/benchmark reale | ANALYSIS §7 T05, prove E05-01/02/03; ARCHIVE §A-003; SNAPSHOT-T05.md |
| 2026-11-23 | T06 | Autorizzazione utente; contesto separato con KB e materiali ereditati, senza promessa di assenza di bias. HEAD atteso e indice vuoto; 38/38 hash manifest T05 verificati; nessun accesso dati privati | TODO → IN_PROGRESS | PLAN TR-06; SNAPSHOT-T05.md SHA-256 `8318CB2521E2A6A9DDFEFC68BC4DAF448B02FF19FFB69CA29B0088C8A67F0640`; F-007 preservato |
| 2026-11-23 | T06 | Individuato scope XL e aperto F-009 prima di qualsiasi implementazione; scomposizione proposta senza cambiare acceptance, in attesa di conferma | IN_PROGRESS sospeso; nessun benchmark o test | ANALYSIS §7 T06; PLAN TR-06; SNAPSHOT-T06.md |
| 2026-11-23 | T06a | Utente approva scomposizione e avvio esclusivo T06a | TODO → IN_PROGRESS; T06b/c TODO | D-13; ANALYSIS §7 T06a |
| 2026-11-23 | T06a | Protocollo, replay e test sintetici completati; snapshot e checklist aggiornati | DONE; T06 IN_PROGRESS, F-001/F-009 OPEN | E06a-01–04; SNAPSHOT-T06a.md |
| 2026-11-23 | T06b | Autorizzazione e avvio esclusivo; verificati HEAD, indice vuoto e 41/41 hash ereditati T05 con override T06a | IN_PROGRESS; T06c non avviato | D-14; SNAPSHOT-T06a.md; F-007 preservato |
| 2026-11-23 | T06b | Individuato F-010 prima dell'implementazione: catalogo e L3 non disponibili da TemporalInput; ricostruzione storica impliciti impossibile | OPEN; approccio nel perimetro proxy previsto | PLAN TR-06; ANALYSIS §7 T06a; RecoBuildService |
| 2026-11-23 | T06b | Input catalogo, ranking proxy e L3 train-only verificati su sintetici; finding archiviato, snapshot e checklist aggiornati | DONE; F-010 RESOLVED; T06 IN_PROGRESS; F-001/F-009 OPEN | E06b-01–03; ARCHIVE §A-004; SNAPSHOT-T06b.md |
| 2026-11-23 | T06c | Autorizzazione utente a concludere TR06, escluso T07; nuovo contesto con materiali ereditati, non verifica indipendente; HEAD invariato, indice vuoto, 44/44 hash corrispondenti | TODO → IN_PROGRESS | D-14; SNAPSHOT-T06b.md SHA-256 `F28A26819E5E073A8B7EC56A1B2B417EAEF525F65088C66F2F74EB657FB8014B`; F-007 preservato |
| 2026-11-23 | T06c/T06 | Metriche, CLI e acceptance complessiva verificate; F-009 archiviato, F-001 residuo aperto | DONE; T07 non avviato | E06c-01–05; ARCHIVE §A-005; SNAPSHOT-T06c.md |
| 2026-11-23 | T07 | Verificati HEAD, indice pulito e 44 hash ereditati invariati al di fuori dei tre file toccati; nessun accesso a DB o dati utente | TODO → IN_PROGRESS | SNAPSHOT-T06c.md; ANALYSIS §7 T07 |
| 2026-11-23 | T07 | Adapter ONNX/tokenizer, pin di revisioni/hash, golden tokenizer multilingui, guida e test completati; self-test reale su sei lingue, nessun cambio produzione | DONE; T08 non avviato | E07-01–04; ARCHIVE §A-006; SNAPSHOT-T07.md |
| 2026-11-23 | T08 | Avvio autorizzato sul solo snapshot T04; runner da estendere per misure ripetibili e confronto dei due modelli; cluster escluso | TODO → IN_PROGRESS | D-18; PLAN TR-08; T04-FINAL; SNAPSHOT-T07.md |
| 2026-11-23 | T08 | Interrotto il passaggio completo sull'overlap dopo 3.000/6.128 vettori; nessun report scritto. Ridotto il campione qualità a 1.024 e aggiornati runner/guida, mantenendo ranking full-pool con copertura embedding parziale esplicitata | IN_PROGRESS; ripetere una sola volta con il protocollo limitato | D-19; nessun input modificato; processo specifico terminato |
| 2026-11-23 | T08 | Run capped terminato: 33.894 titoli, 1 profilo, 318 rating/369 eventi; 1.024 vettori, throughput e risorse locali misurati. La metrica embedding è `embedding_coverage_incomplete` per entrambi i modelli, quindi nessuna conclusione di qualità. Corretto il criterio runner per richiedere metriche embedding complete prima di exit 0; 17 test mirati, format e diff check PASS | TR-08 BLOCKED: serve copertura completa per qualità e approvazione separata per eventuale cluster | Report protetto SHA-256 `8EFAFE1E144C28B897BE27FE63827055B3B7A467977C78E283FDB7FA206B7C23`; input SHA invariato `B7485065…7EDBB43`; `ANALYSIS.md` §7 T08; `SNAPSHOT-T08.md` |
| 2026-11-23 | T08 | Utente autorizza copertura locale completa; runner rigenera vettori fissati di entrambi i modelli per ogni titolo pool; guida aggiornata e 11 test temporali PASS; input ReadOnly, hash conforme, output nuovo | IN_PROGRESS; avviata predisposizione artefatti fissati; nessun cluster | D-20; `ModelComparisonRunner`; test `RecoTemporalBenchmarkTests`; report precedente intatto |
| 2026-11-23 | T08 | Due run full-pool sullo stesso snapshot/seed/protocollo: 33.894/33.894 vettori per modello, metriche embedding disponibili e risultati di qualità identici; hash input/artefatti, ReadOnly, ACL e report precedente verificati; 18 test mirati PASS | F-011 RESOLVED; T08 locale completato, TR-08 resta BLOCKED per cluster non autorizzato; nessun modello promosso | E08-06/07; A-008; nuovi report privati; output fuori repository |
| 2026-11-24 | T08 | Utente conferma rinuncia alle misure cluster per TR-08, accettando limite sulle prestazioni di produzione; hash dei due report e file D-20 verificati | BLOCKED → DONE con rinuncia D-21; nessuna nuova misurazione o accesso cluster | D-21; PLAN TR-08; ANALYSIS §7 T08; ARCHIVE §A-009 |
| 2026-11-24 | T09 | Filtrati i segnali fuori pool prima di `N_eff`, feature sparse/kNN/TMDB/L3 e motivazioni; aggiunti test Core/Infrastructure e regola BR-REC-04 aggiornata | DONE; nessuna migrazione/database/cluster/provider | Hash T09 registrati nel manifest; test 16+17 PASS; `git diff --check` PASS |
| 2026-11-24 | T10 | Ricerca simili su pool enabled completo; rimosso il prefisso 120, fallback sparse per embedding mancanti e merge deterministico con TMDB | DONE; nessuna migrazione/database/cluster/provider | Core 8 PASS; Integration 1 PASS; Infrastructure build 0 warning/errori; `git diff --check` PASS |
| 2026-11-24 | T11 | Rieseguito confronto full-pool sullo stesso snapshot T04, seed 42 e 2 ripetizioni; qualità invariata rispetto a T08; oracle oltre 120 verificato | DONE descrittivo; nessuna promozione o certificazione | Report privato T11 SHA-256 `8E8ABB2DE8BD2713FE7724E85FE46318B28FA0C266F9D0E3BCD16F88539550E`; Core 9 PASS; Integration 1 PASS; snapshot invariato |
| 2026-11-24 | T12 | Aggiornate guida snapshot, specifica fuori pool/Simili/evaluator e confronto post-correzioni; nessun segreto o dato privato nelle doc | DONE | Link e comandi controllati; test mirati API già passati; nessuna migrazione/provider/cluster |

| 2026-11-24 | T13 | Primo tentativo indipendente; 152 test mirati verdi non coprono i controesempi B1–B3 né provano il confronto B4 | FAIL; T13 BLOCKED | ARCHIVE §A-010; §9 |
| 2026-11-24 | Ciclo B1–B4 | Correzioni autorizzate. B1: test riprodotto rosso prima, verde dopo; voto vicino cambia la feature 0,55→0,45, voto target ininfluente, senza vettore vicino feature indisponibile. Baseline isolata con sorgente rebuild SHA-256 T08 verificato e compilato. | In corso; nessun nuovo PASS indipendente | D-22; test mirati L3/evaluator: 15 PASS |

## 6. KPI

| Indicatore | Baseline | Target | Attuale | Metodo |
| --- | --- | --- | --- | --- |
| Dati originali | Non ispezionati | Nessuna alterazione intenzionale | Conteggi origine/copia stabili nella finestra, inode/dimensione DB invariati; mtime/WAL variati; sorgente aperta read-only, nessuna SQL scrittura/checkpoint | T04: ANALYSIS §7; integrità/FK copia PASS; nessuna affermazione di immutabilità byte/logica del live |
| Ranking/risorse | Non misurati | Confronto riproducibile, non soglia inventata | Due run locali: current 104–108 titoli/s, p50/p95 embedding 8,3–8,7/16,8–18,2 ms, picco 596 MiB; multilingual 46,8–54,1 titoli/s, 18,1–20,2/26,7–40,5 ms, picco 935–968 MiB. Metriche di qualità identiche, descriptive proxy su 1 profilo; cluster non misurato | T08: report privati, stesso seed/protocollo; nessuna comparabilità cluster |
| Simili | Prefisso ≤120 | Recall 100% rispetto a riferimento esatto sul test sintetico | Non misurato | T10 |

## 7. Checklist archiviazione

| Condizione | Verificata |
| --- | --- |
| Activity log oltre 10 task DONE | No: T01–T07, con figli T06a–c |
| Budget bootstrap/pacchetto superato | No: stima orientativa senza tokenizer; letture operative mirate |
| Almeno 3 finding risolti/accettati | Sì: F-005/F-006/F-008 e F-004 già archiviati; F-010 in §A-004, F-009 in §A-005 |
| Decisione nuova rende obsolete note precedenti | Sì: D-20 supera il cap D-19 (A-008); D-21 rinuncia alle misure cluster per T08 (A-009) |

## 8. Tooling

Repository .NET 10; `dotnet test tests/Pickarr.Core.Tests`, `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~Reco`, `dotnet test tests/Pickarr.Integration.Tests --filter FullyQualifiedName~Recommendations`; aggiungere selettori specifici secondo task. Format: `dotnet format Pickarr.slnx --verify-no-changes`; fermare solo previa autorizzazione eventuale API che blocchi DLL. CI effettiva: `.github/workflows/ci.yml`.

## 9. Verifica finale indipendente

| Campo | Valore |
| --- | --- |
| Task finale | T13 |
| Esito | **PASS** al terzo tentativo sul requisito rivisto D-23 |
| Revisore/sessione | `revisore-tr13`, contesto `a967d964-e495-42c6-9b2d-eef3d178ffa9` |
| Isolamento effettivo | Revisore in contesto separato in sola lettura; nessun accesso a DB privati o cluster. Test sintetici eseguiti dal coordinatore. |
| Versione verificata | HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`, indice vuoto, 63 file fisici non-KB; SHA-256 `615acae8b520166348eda510bfd04a6f161fdf59de0a458a82a4c4057a99ef80` ricalcolato dal revisore. |
| Rapporto | ARCHIVE §A-013; misure §A-011 |
| Tentativo precedente/ciclo correttivo | Primo FAIL §A-010; correzioni B1–B4 §A-011. Secondo FAIL §A-012; D-23 e UX su richiesta verificati, F-014 chiuso. |
| Rinvii/rischi approvati | D-21: prestazioni cluster non misurate per T08; nessuna equivalenza prestazionale dedotta dalla copia del DB |
