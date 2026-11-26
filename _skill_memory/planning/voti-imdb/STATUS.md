# VOTI-IMDB STATUS

> Ultimo aggiornamento: 2026-11-26
> Branch: `main`
> Storico operativo: `ARCHIVE.md`

---

## 0. Quick Context

- Obiettivo: job settimanale del dump IMDb, voto calcolato persistito, uso in UI, filtro, sort, prior e livello 3. Requisiti in ANALYSIS.md §1 R-01…R-13.
- Stato dominio: verifica finale PASS (T17, ARCHIVE.md §A-07). Chiusura in attesa della decisione dell'utente su F-005 e F-006 (rinvio o fix + nuova verifica).
- Codebase: HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` su `main`. TR-01…TR-08 non sono committate.
- Snapshot locale: rispetto alla versione di T12, TR-08 modifica `docs/technical-spec.md`, `docs/recommendations.md`, `docs/user/features.md`, `tools/Pickarr.RecoBench/BenchmarkCatalogInput.cs`, `tools/Pickarr.RecoBench/TemporalRanking.cs`, `tests/Pickarr.Infrastructure.Tests/RecoTemporalRankingTests.cs`, `tests/Pickarr.Infrastructure.Tests/RecoTemporalBenchmarkTests.cs`. `.data/Pickarr.db` non è stato migrato.
- Tranche/task candidato: nessuno. Tutte le tranche concluse.
- Dettagli: rapporti ARCHIVE.md §A-03 (FAIL), §A-05 (PASS superato), §A-07 (PASS corrente).
- Blocchi e findings critici: nessuno bloccante. F-005 e F-006 bassi aperti, vedi §3.
- Input umano: rinviare F-005 e F-006 e chiudere il dominio, oppure correggerli con una nuova verifica.
- Handoff: T17 eseguita da un subagente separato dalla sessione che ha scritto TR-10.
- Verifica finale: vedi §9.

---

## 1. Roadmap

### TR-01 — Formula e schema

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T01 | Formula pura del voto | S | 🟢 | DONE | — | PLAN.md TR-01 T01; test 9 passed |
| T02 | Migrazione additiva e backfill | M | 🟡 | DONE | — | PLAN.md TR-01 T02; test 16 passed |

### TR-02 — Import settimanale

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T03 | Download, parse e sostituzione dello snapshot | L | 🟡 | DONE | T02 | PLAN.md TR-02 T03; import test |
| T04 | Allineamento ai titoli | M | 🟡 | DONE | T01, T03 | PLAN.md TR-02 T04; import test |
| T05 | Job, orario, trigger e riallineamento del build | M | 🟡 | DONE | T04 | PLAN.md TR-02 T05; job, schedule, admin |

### TR-03 — Arricchimento

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T06 | Lookup locale durante l'arricchimento | M | 🟡 | DONE | T01, T02 | PLAN.md TR-03 T06; EnrichmentServiceTests |

### TR-04 — Catalogo e UI

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T07 | Query, filtro e contratti API | M | 🟡 | DONE | T02 | PLAN.md TR-04 T07; TitleQueryServiceTests 17 passed |
| T08 | Card, scheda e copy | M | 🟢 | DONE | T07 | PLAN.md TR-04 T08; card, dialog, typecheck, lint |

### TR-05 — Raccomandazioni

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T09 | Prior senza secondo shrink | M | 🔴 | DONE | T02 | PLAN.md TR-05 T09; RecoPriorTests |
| T10 | Feature di livello 3 | L | 🟡 | DONE | T01, T09 | PLAN.md TR-05 T10; RecoLevel3FeatureTests |

### TR-06 — Allineamento specifiche

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T11 | D-03 e coerenza dei docs | S | 🟢 | DONE | T05, T08, T10 | PLAN.md TR-06 T11; lettura dei quattro documenti |

### TR-07 — Verifica finale indipendente

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T12 | Verifica finale indipendente | M | 🔴 | FAILED | TR-01, TR-02, TR-03, TR-04, TR-05, TR-06 | PLAN.md TR-07 T12; rapporto ARCHIVE.md §A-03 |

### TR-08 — Remediation della verifica T12

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T13 | Formula nei docs e riga BR-REC-15 | S | 🟢 | DONE | — | PLAN.md TR-08 T13; F-003, F-002 |
| T14 | RecoBench allineato a voto calcolato e conteggio | S | 🟡 | DONE | — | PLAN.md TR-08 T14; F-001 |

### TR-09 — Verifica finale indipendente, tentativo 2

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T15 | Verifica finale indipendente | M | 🔴 | DONE (PASS da ripetere dopo TR-10) | TR-01, TR-02, TR-03, TR-04, TR-05, TR-06, TR-08 | PLAN.md TR-09 T15; ARCHIVE.md §A-05 |

### TR-10 — Remediation di F-004

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T16 | Test del conteggio voti nel livello 3 del benchmark | S | 🟢 | DONE | — | PLAN.md TR-10 T16; F-004 |

### TR-11 — Verifica finale indipendente, tentativo 3

| ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- |
| T17 | Verifica finale indipendente | M | 🔴 | DONE | TR-01, TR-02, TR-03, TR-04, TR-05, TR-06, TR-08, TR-10 | PLAN.md TR-11 T17; rapporto §9 |

Stima di lettura del bootstrap (addendum + §0 + questa roadmap, senza tokenizer): entro il budget orientativo di circa 1.000 token. Non è un conteggio esatto.

---

## 2. Flussi — stato per area

| Flusso | Stato | Note |
| --- | --- | --- |
| FLUSSO-01 Import dataset IMDb | ✅ Completato | Job `imdb-ratings`, dump e ricalcolo titoli. Il lookup in arricchimento è FLUSSO-02 |
| FLUSSO-02 Arricchimento TMDB | ✅ Completato | Dopo i dettagli TMDB copia il voto dal dump locale. Nessun download IMDb |
| FLUSSO-03 Lettura catalogo | ✅ Completato | `minRating` e `sort=rating` su `VoteBlended`. Lista: `ratingScore` e `ratingTmdb`. Dettaglio: score, imdb, tmdb |
| FLUSSO-04 Prior e livello 3 | ✅ Completato | Bayes = `VoteBlended`, senza `m = 300`. Feature voto = blend/10; in coda `log10(1+v)/6` |
| FLUSSO-05 UI voto | ✅ Completato | Badge e numero della scheda su `ratingScore`. Sotto, IMDb e TMDB con i conteggi |

---

## 3. Findings aperti

| ID | Gravità | Descrizione | Task | Stato |
| --- | --- | --- | --- | --- |
| F-005 | Bassa | Nessun test verifica che training e scoring del livello 3 in `RecoBuildService` / `RecoEvalService` ricevano `UsableVoteCount` e `VoteBlended`. Non bloccante, codice corretto oggi | — | OPEN, decisione utente |
| F-006 | Bassa | Storico run: tipo «ImdbRatings» senza chiave `admin.kind.ImdbRatings`; statistiche «0 credits · N pages» con N = righe del dump. Cosmetico, non bloccante | — | OPEN, decisione utente |

F-001…F-003: ARCHIVE.md §A-06. F-004 chiuso e verificato da T17: ARCHIVE.md §A-07. Origine di F-005 e F-006: rapporto T17, §A-07.

I rilievi di assessment A-001, A-002 e A-003 sono in ANALYSIS.md §5. Non sono finding di esecuzione.

---

## 4. Decisioni architetturali

| ID | Data | Decisione | Owner | Motivazione | Fonte |
| --- | --- | --- | --- | --- | --- |
| D-001 | 2026-11-26 | Dominio nuovo `voti-imdb` | utente | Nome confermato al gate. Non si estendono i domini già presenti | gate dominio |
| D-002 | 2026-11-26 | Due fonti: shrink per fonte con `m = 500` e `C` del dump IMDb, poi media pesata per `w = v/(v+m)`. Stessa `C` per IMDb e TMDB. Costanti di codice | utente | Scelta «per-source-bayes» e pacchetto `m500-imdb-c` | gate blend, gate params |
| D-003 | 2026-11-26 | Una fonte: voto grezzo, senza shrink e senza segnaletica di pochi voti | utente | Scelta «raw-no-flag» | gate single-source |
| D-004 | 2026-11-26 | Il voto calcolato alimenta card, scheda, `sort=rating`, `minRating`, prior e feature di livello 3. La scheda mostra anche i due grezzi con i conteggi. `ratingTmdb` in lista resta il grezzo TMDB | utente | Scelta «ui-catalog-reco», più il dettaglio fonti già nella richiesta | gate surfaces |
| D-005 | 2026-11-26 | Il componente voto del prior è `VoteBlended`, senza il secondo shrink `m = 300`. Senza voto si usa la media del pool. Mix 0,7 / 0,3 invariato | utente | Scelta di non shrinkare due volte | gate reco-prior |
| D-006 | 2026-11-26 | Soglia di riallineamento: accodare `RecoBuild` al primo import e quando `\|ΔC\| ≥ 0,05`. Il modello di livello 3 non è persistito, quindi training e inferenza dello stesso build leggono le colonne correnti | utente per il riallineamento; agente per la soglia 0,05 | L'utente ha chiesto il riallineamento sulle variazioni sensibili di `C`. 0,05 punti evita un build extra sul rumore settimanale; il build orario copre il resto | gate reco-prior, testo aggiunto dall'utente; K-004 |
| D-007 | 2026-11-26 | Import completo settimanale più lookup in arricchimento sulla tabella locale. Download o parse fallito: snapshot, tabella e colonne titoli restano quelli dell'ultimo successo | utente | Scelta «weekly-plus-enrichment». Il mantenimento dell'ultimo snapshot è il comportamento sicuro dichiarato in pianificazione e accettato con l'orario | gate freshness, gate schedule |
| D-008 | 2026-11-26 | Cron `0 4 * * 0` con `TimeZoneInfo.Local`. Id `imdb-ratings`. Trigger manuale dagli altri job. Gli altri cron non si spostano di fuso | utente | Scelta domenica 04:00 ora del server | gate schedule |
| D-009 | 2026-11-26 | Fonte pesabile solo con voto non nullo e conteggio `> 0`. Un solo grezzo, anche con conteggio nullo o zero, è il risultato. Due grezzi senza conteggi positivi danno null | agente | Serve un caso deterministico agli estremi, coerente con «non mediare con zero» e con il backfill che deve continuare a mostrare il TMDB | delega sul caso limite, da confermare con il piano |
| D-010 | 2026-11-26 | Migrazione additiva. Backfill `VoteBlended = VoteTmdb` dove `VoteTmdb` non è nullo, così la card non resta vuota prima del primo import | agente | A-003. Il database di sviluppo non si cancella | regola `AGENTS.md`; da confermare con il piano |
| D-011 | 2026-11-26 | La feature nuova è `log10(1+v)/6` sulla somma dei conteggi pesabili, non il peso `v/(v+m)` | agente | L'utente ha lasciato le due opzioni. Il conteggio ha una definizione sola per una o due fonti e non incorpora di nuovo `m`, già presente nel voto calcolato | testo del gate reco-prior |
| D-012 | 2026-11-26 | D-03 viene sostituita: il voto mostrato arriva dal dataset IMDb e da TMDB. OMDb non è una sorgente | utente | La richiesta sceglie `datasets.imdbws.com` e la UI | richiesta 2026-11-26 |
| D-013 | 2026-11-26 | Household prior, embedding, profilo sparso ed esplorazione restano com'è | agente | Il gate nomina il prior di livello 0 e la feature voto del livello 3, non gli altri componenti | perimetro del gate; da confermare con il piano |

---

## 5. Activity log

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-26 | T02…T08 | Righe archiviate | — | ARCHIVE.md §A-04 |
| 2026-11-26 | T09 | Prior senza secondo shrink | DONE | `RecoPriorTests`: bayes 9 e 7 restano 9 e 7. Senza `VoteBlended` il bayes è la media del pool; pool senza voti → 0. Pesi 0,7 / 0,3 invariati. Filtro Core `RecoPriorTests` e `RecoLevel3`: 8 passed |
| 2026-11-26 | T10 | Feature di livello 3 | DONE | Voto = blend/10, assente 0,5. Indici 9 e 10 invariati. Nuova cella `log10(1+v)/6` con tetto 1. `RecoEvalMetricsTests`: 7 passed. Infrastructure su `RecoLevel3`, `RecoTemporalRankingTests`, `RecoBuildServiceTests`, `RecoEvalServiceTests`: 41 passed |
| 2026-11-26 | T11 | D-03 e coerenza dei docs | DONE | D-03 in inglese: voto mostrato dal dataset `title.ratings.tsv.gz` e da TMDB; OMDb non è una sorgente. Il resto di §11.3 resta com'era. Allineati anche FR-MET-04, FR-DET-01, lo schema `Titles` in `docs/technical-spec.md` e le frasi «TMDB prior» in `docs/recommendations.md`. `docs/user/features.md` descriveva già badge e scheda. `docs/developer/architecture.md` non elenca i job |
| 2026-11-26 | T12 | Verifica finale indipendente, tentativo 1 | FAILED | Versione e comandi in §9 e ARCHIVE.md §A-03. Test tutti verdi; R-12 non soddisfatto (F-003); regressioni F-001 e F-002. Remediation TR-08 e nuova verifica TR-09 pianificate |
| 2026-11-26 | T13 | Formula nei docs e riga BR-REC-15 | DONE | `docs/technical-spec.md`: paragrafo «Audience score» sotto gli indici di `Titles`, con fonte pesabile, `m = 500`, `C` dello snapshot, shrink, media per `w`, regole a una fonte e nullo; confrontato riga per riga con `AudienceScore.Blend`. `docs/recommendations.md` §12.2: rimando alla formula e nota «nessun secondo shrink». BR-REC-15: 3 separatori come l'intestazione `\| ID \| Rule \|`, frase ripristinata nella cella. `docs/user/features.md`: una frase sul calcolo del badge. Nessun'altra riga di tabella toccata |
| 2026-11-26 | T14 | RecoBench su voto calcolato e conteggio | DONE | `BenchmarkCatalogInput` legge `VoteBlended`, `Popularity`, `VoteImdb`, `VoteImdbCount`, `VoteTmdb`, `VoteTmdbCount`; `BenchmarkTitle.UsableVoteCount` da `RecoLevel3.UsableVoteCount`; `TemporalRanking` lo passa a `BuildFeatures`. Schemi sintetici di `RecoTemporalRankingTests` e `RecoTemporalBenchmarkTests` estesi con le tre colonne. Nuove asserzioni: titolo con TMDB 5 e blend 7,4 legge 7,4 e conteggio 600; solo TMDB legge 5 e 100. `dotnet build Pickarr.slnx`: 0 warning, 0 errori. `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~RecoTemporal\|FullyQualifiedName~RecoSnapshot\|FullyQualifiedName~RecoPreparation\|FullyQualifiedName~RecoModelExperiment\|FullyQualifiedName~RecoEmbeddingPerformance"`: 45 passed, 0 failed. Nessun file in `src/` toccato |
| 2026-11-26 | T15 | Verifica finale indipendente, tentativo 2 | DONE — PASS | Subagente in contesto separato. Manifest `c56e7771…7d02` stabile. .NET 535/535, Web 151/151, typecheck e lint ok, nessuna modifica EF pendente. R-01…R-13 OK; F-001…F-003 chiusi; nuovo F-004 bassa, non bloccante. ARCHIVE.md §A-05 |
| 2026-11-26 | F-004 | Decisione utente | — | Fix con nuova verifica, non rinvio. PASS di T15 da ripetere |
| 2026-11-26 | T16 | Test del conteggio voti nel livello 3 del benchmark | DONE | Nuovo test `Level3_training_and_scoring_receive_the_usable_vote_count` in `RecoTemporalRankingTests`: 100 esempi di training con feature `min(1, log10(1+v)/6)` per `v = id·100`; scorer sulla feature di conteggio → ordine livello 3 `104, 103, 102, 101`. Prova di mutazione: senza `title.UsableVoteCount` in `BuildFeatures`, dopo `dotnet build` esplicito, il test fallisce sull'asserzione della feature; ripristinato il codice (diff di `TemporalRanking.cs` identico a TR-08). Una prima prova senza build esplicita era passata su binari vecchi e non conta. `dotnet build Pickarr.slnx`: 0 warning, 0 errori. Filtro dei test RecoBench: 46 passed, 0 failed |
| 2026-11-26 | T17 | Verifica finale indipendente, tentativo 3 | DONE — PASS | Subagente separato. Manifest `ba1bd9a4…3fbe` stabile. .NET 536/536, Web 151/151, typecheck e lint ok, nessuna modifica EF pendente. R-01…R-13 OK; F-001…F-004 chiusi; nuovi F-005 e F-006 bassi, non bloccanti. ARCHIVE.md §A-07 |

---

## 6. KPI

Nessun KPI misurato in pianificazione. Il conteggio righe del dump si osserva su `ImdbRatingSnapshot.RowCount` al primo import (K-007), senza baseline precedente.

---

## 7. Checklist archiviazione

| Condizione | Verificata? |
| --- | --- |
| Activity log ha > 10 righe DONE | ☑ 2026-11-26: righe T02…T08 spostate in ARCHIVE.md §A-04 |
| Bootstrap/pacchetto supera il budget orientativo | ☐ |
| ≥ 3 finding RESOLVED/ACCEPTED nella tabella §3 | ☑ 2026-11-26: F-001…F-003 verificati da T15 e spostati in ARCHIVE.md §A-06 |
| Una decisione nuova rende obsolete note precedenti | ☐ |

---

## 8. Tooling

| Aspetto | Convenzione |
| --- | --- |
| Branch | nessuno finché l'utente non chiede un commit o un branch |
| Commit | solo su richiesta esplicita |
| Test Core | `dotnet test tests/Pickarr.Core.Tests --filter <Nome>` |
| Test Infrastructure | `dotnet test tests/Pickarr.Infrastructure.Tests --filter <Nome>` |
| Test Web | da `src/Pickarr.WebApp`: `npm test -- --run`, `npm run typecheck`, `npm run lint` |
| Migrazioni | `dotnet ef` come in AGENT_KB.md §5 del dominio. Fermare l'API se blocca i DLL. Non cancellare `.data` |

---

## 9. Verifica finale indipendente

| Campo | Valore |
| --- | --- |
| Task finale | T17 (tentativo 3), DONE |
| Esito | **PASS** |
| Revisore e sessione | Subagente Cursor `e274c036-c59e-40f0-b8b0-a6d2828b31ca`, contesto nuovo; non ha eseguito TR-01…TR-10 |
| Isolamento effettivo | Contesto separato dall'esecutore di TR-10; mandato scritto dall'esecutore; STATUS e ARCHIVE letti per orientamento, non come prova; bias di modello non escluso |
| Versione verificata | HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` + worktree 67 file, manifest SHA-256 `ba1bd9a4c44f7685e1e56c7f9eaa82ce36924ec2edb485d1dccd4f9ae21a3fbe` |
| Rapporto | ARCHIVE.md §A-07. Tentativo 2 (PASS, superato da TR-10): §A-05 |
| Tentativo precedente / ciclo correttivo | T12, 2026-11-26: **FAIL**. Revisore: agente Cursor in sessione nuova, senza esecuzione di TR-01…TR-06. Isolamento: contesto separato; letti STATUS e log dell'esecutore ma non usati come prova; bias di modello non escluso. Versione: HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` + worktree, manifest SHA-256 `489a90e03471771327728dcb93a195632974c000bb4ffffd84ce4b6f7556334c`. Rapporto: ARCHIVE.md §A-03. Ciclo correttivo: TR-08 (T13, T14), poi TR-09 / T15 |
| Rinvii/rischi approvati | F-005 e F-006 in attesa di decisione dell'utente. Qualsiasi modifica a codice o test dopo la versione sopra rende il PASS da ripetere |
