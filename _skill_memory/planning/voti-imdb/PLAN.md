# PLAN — voti-imdb

Dettagli canonici di scope e acceptance. Stato, dipendenze e size solo in STATUS.md §1.

## TR-01 — Formula e schema

### T01 — Formula pura del voto

- Scope: nuovo tipo in `src/Pickarr.Core` (funzione di blend, costante `m = 500`); test nuovo in `tests/Pickarr.Core.Tests`. Nessun I/O, nessun tipo di Infrastructure.
- Requisiti: ANALYSIS.md §1 R-03, R-04; formula in ANALYSIS.md §6.2.
- Acceptance:
  - Con `C = 7`, IMDb 8,0 / 500 voti e TMDB 6,0 / 500 voti il risultato è 7,0.
  - Solo IMDb 9,1 / 12 voti → 9,1. Solo TMDB 7,4 / 80 voti → 7,4.
  - IMDb 9,0 / 0 voti e TMDB 8,0 / 100 voti → 8,0.
  - Due voti presenti e entrambi i conteggi nulli o zero → null. Nessun voto → null.
  - Un solo voto presente con conteggio nullo o zero → quel voto.
  - `m` usato nel calcolo è 500. `C` è un argomento, non una costante interna.
- Verifica: `dotnet test tests/Pickarr.Core.Tests --filter FullyQualifiedName~AudienceScoreTests` (adattare il nome se la classe ha un altro nome, e riportarlo nel log).
- Contesto: D-002, D-003, D-009, V-01, V-02, V-03. Flusso nessuno finché non è collegata.
- Assunzioni: nessuna.
- Rollback: rimuovere il tipo e il test aggiunti da questo task.

### T02 — Migrazione additiva e backfill

- Scope: `Title`; entità `ImdbTitleRating` e `ImdbRatingSnapshot` in Core; `DbSet` in `PickArrDbContext`; configuration EF; migrazione generata in `src/Pickarr.Infrastructure/Persistence/Migrations` incluso lo snapshot. Backfill SQL nella migrazione.
- Requisiti: R-11; schema in ANALYSIS.md §6.3.
- Acceptance:
  - `Title` guadagna `VoteImdb`, `VoteImdbCount`, `VoteBlended`.
  - `ImdbTitleRating` ha PK `Tconst` (max 16). `ImdbRatingSnapshot` è una riga di metadati con `MeanVote`, `RowCount`, `ImportedAt` e URL.
  - Indice su `Title.VoteBlended`.
  - `Up` contiene create/add column e `UPDATE Titles SET VoteBlended = VoteTmdb WHERE VoteTmdb IS NOT NULL`. Nessuna istruzione di drop o delete di tabelle esistenti.
  - Il file di database in `.data` non viene cancellato né sostituito per generare la migrazione.
- Verifica: lettura del metodo `Up` della migrazione generata. `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~TitleQueryServiceTests` dopo la migrazione, per vedere che il modello EF si applica al SQLite di test. Fermare `Pickarr.Api` se blocca i DLL prima di `dotnet ef`.
- Contesto: D-010, V-09, A-003, K-001.
- Assunzioni: nessuna.
- Rollback: rimuovere la migrazione e lo snapshot generati insieme, con `dotnet ef migrations remove` sullo stesso startup project, solo se la migrazione non è stata applicata al database di sviluppo. Se è già applicata, aggiungere una migrazione successiva: non cancellare `.data`.

## TR-02 — Import settimanale

### T03 — Download, parse e sostituzione dello snapshot

- Scope: servizio di import in Infrastructure (HTTP + gzip + TSV + scrittura EF); fixture gzip minima in `tests/fixtures`; test Infrastructure con WireMock o file locale. Nessuna chiamata di rete reale nei test.
- Requisiti: R-01, R-10; FLUSSO-01; K-007.
- Acceptance:
  - URL fisso `https://datasets.imdbws.com/title.ratings.tsv.gz`.
  - Il parse avviene prima della transazione di scrittura.
  - Header diverso da `tconst`, `averageRating`, `numVotes`, gzip non valido, oppure zero righe valide: eccezione e database invariato (tabella, snapshot e titoli).
  - Import valido: la tabella contiene solo le righe nuove; `MeanVote` è `Σ(R·v)/Σ(v)`; `RowCount` e `ImportedAt` aggiornati.
  - Righe malformate si saltano. Se dopo lo skip non resta nessuna riga valida, scatta il rifiuto sopra.
- Verifica: `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~ImdbRatingImportTests` (nome effettivo nel log).
- Contesto: D-007, V-01, K-007.
- Assunzioni: K-007. Il test della fixture è il controllo. Non promuovere a fatto la dimensione del file reale finché `RowCount` non è osservato.
- Rollback: rimuovere servizio, fixture e test di questo task. Non toccare il database di sviluppo.

### T04 — Allineamento ai titoli

- Scope: passo di apply nello stesso servizio di import, oppure tipo dedicato chiamato dall'import. Test Infrastructure con dump piccolo e pochi `Title`.
- Requisiti: R-02, R-03.
- Acceptance:
  - `ImdbId` trovato nel dump: `VoteImdb` e `VoteImdbCount` copiati, `VoteBlended` uguale alla funzione di T01 con la `C` dello snapshot.
  - `ImdbId` assente dal dump nuovo: `VoteImdb` e `VoteImdbCount` a null, poi ricalcolo.
  - Titolo senza `ImdbId` e con solo TMDB: `VoteBlended` secondo la regola a una fonte.
  - `VoteTmdb` e `VoteTmdbCount` non vengono modificati da questo passo.
  - Il ricalcolo copre i titoli che hanno `ImdbId` oppure un voto TMDB, a batch, dopo il commit della tabella dump.
- Verifica: stesso filtro di test di T03, oppure un filtro dedicato citato nel log, con i casi di match, `tconst` rimosso e solo TMDB.
- Contesto: D-002, D-009, FLUSSO-01. Dipende dalla funzione di T01 e dalle tabelle di T02.
- Assunzioni: nessuna.
- Rollback: ripristinare il servizio di import allo stato precedente a questo task, lasciando intatte formula e migrazione.

### T05 — Job, orario, trigger e riallineamento del build

- Scope: job Hangfire nuovo; `ScheduleReconciler`; `SyncRunKinds`; `AdminJobService` (elenco, trigger, filtro run); `HangfireJobActivity` se mappa il nome del job; `DependencyInjection`; `ScheduleReconcilerTests`; riga job in `docs/technical-spec.md`.
- Requisiti: R-08 (solo l'enqueue), R-12 (solo la riga job), R-13.
- Acceptance:
  - Id `imdb-ratings`, kind `ImdbRatings`, cron `0 4 * * 0`, timezone `TimeZoneInfo.Local`. Gli altri cron invariati.
  - Esecuzione non concorrente, stesso schema di run di `MetadataRefreshJob` (manuale vs schedulato).
  - `POST /api/v1/admin/jobs/imdb-ratings/trigger` accoda il job. Chiave sconosciuta resta 400.
  - A import riuscito, se non c'era `C` oppure `|C_nuova − C_precedente| ≥ 0,05`, un `RecoBuild` viene accodato una volta. Sotto soglia non parte un build extra da questo job.
  - Il fallimento di T03 non accoda il build e non segna il run come riuscito.
  - La spec tecnica elenca il job, la sorgente URL e la cadenza.
- Verifica: `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~ScheduleReconcilerTests` e il filtro dei test admin/import che coprono trigger e soglia `C`. Citare i filtri nel log.
- Contesto: D-006, D-008, V-06, V-07, FLUSSO-01.
- Assunzioni: nessuna.
- Rollback: togliere registrazione, kind e mapping del job. Le tabelle restano.

## TR-03 — Arricchimento

### T06 — Lookup locale durante l'arricchimento

- Scope: `EnrichmentService.ApplyDetails` e il collaboratore che legge `ImdbTitleRating` / snapshot e scrive le colonne voto; `EnrichmentServiceTests`.
- Requisiti: R-09, R-03, R-02 per il singolo titolo appena arricchito.
- Acceptance:
  - Dopo `ApplyDetails`, se `ImdbId` è nel dump locale, il titolo ha voto IMDb, conteggio e `VoteBlended` della formula.
  - Dump vuoto o `ImdbId` assente: `VoteImdb` nullo e `VoteBlended` dalla sola fonte TMDB quando il voto TMDB c'è.
  - Il test non esegue HTTP verso `datasets.imdbws.com`.
  - `VoteTmdb` resta quello restituito da TMDB.
- Verifica: `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~EnrichmentServiceTests`.
- Contesto: D-007, V-08, FLUSSO-02.
- Assunzioni: nessuna.
- Rollback: ripristinare `ApplyDetails` e i test toccati; formula e job restano.

## TR-04 — Catalogo e UI

### T07 — Query, filtro e contratti API

- Scope: `TitleQueryService`; `TitleListItemDto` e `TitleRatingsDto` in `TitleQueryModels.cs`; test Infrastructure della query; test Integration se un caso esistente asserisce `ratingTmdb` o `ratings.tmdb` come unico voto; esempio JSON in `docs/technical-spec.md`.
- Requisiti: R-05 (contratto), R-06, R-12 (solo l'esempio API).
- Acceptance:
  - `minRating` confronta `VoteBlended`. Un titolo con `VoteBlended` nullo non passa.
  - `sort=rating` ordina per `VoteBlended`, con lo stesso trattamento dei nulli che oggi ha `VoteTmdb` su SQLite.
  - La lista espone `ratingScore` = `VoteBlended` e continua a esporre `ratingTmdb` = `VoteTmdb`.
  - Il dettaglio espone `ratings.score`, `ratings.imdb`, `ratings.imdbVotes`, `ratings.tmdb`, `ratings.tmdbVotes`.
  - La spec tecnica mostra il JSON nuovo.
- Verifica: `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~TitleQueryServiceTests` e, se si tocca un test API, il filtro Integration corrispondente.
- Contesto: D-004, FLUSSO-03, K-002.
- Assunzioni: nessuna.
- Rollback: ripristinare DTO, query e test di questo task.

### T08 — Card, scheda e copy

- Scope: `TitleCard.tsx`, `TitleDialog.tsx`, `src/Pickarr.WebApp/src/api/types.ts`, `src/Pickarr.WebApp/src/i18n/locales/en.json`, test di card, dialog e delle fixture che costruiscono il tipo lista/dettaglio, `docs/user/features.md`.
- Requisiti: R-04, R-05, R-12 (guida utente).
- Acceptance:
  - Il badge della card è `ratingScore` a un decimale. Con `ratingScore` nullo il badge non c'è.
  - La scheda mostra il voto calcolato come numero del titolo e, sotto, IMDb e TMDB con il conteggio voti quando il valore c'è.
  - Nessuna stringa di pochi voti.
  - Le stringhe nuove sono chiavi di `en.json`.
  - La guida utente dice che il voto in card è la media pesata IMDb/TMDB e che la scheda mostra le due fonti.
- Verifica: da `src/Pickarr.WebApp`, `npm test -- --run` sui test toccati, poi `npm run typecheck` e `npm run lint`.
- Contesto: D-003, D-004, FLUSSO-05.
- Assunzioni: nessuna.
- Rollback: ripristinare i file UI, le chiavi i18n e la guida toccati da questo task.

## TR-05 — Raccomandazioni

### T09 — Prior senza secondo shrink

- Scope: `RecoPrior`, `RecoTitleStats`, chiamate in `RecoBuildService` e `RecoEvalService`, `tests/Pickarr.Core.Tests/RecoPriorTests.cs`, sezione §12.2 di `docs/recommendations.md`.
- Requisiti: R-07, R-12 (solo §12.2).
- Acceptance:
  - Con voti calcolati 9 e 7 nel pool, i bayes di quei titoli sono 9 e 7, non una media tirata verso il centro con `m = 300`.
  - Un titolo senza `VoteBlended` ha bayes uguale alla media dei `VoteBlended` presenti nel pool.
  - Pool senza alcun `VoteBlended`: bayes 0, come oggi con pool senza voti TMDB.
  - Popolarità e pesi 0,7 / 0,3 invariati.
  - `RecoPrior` non legge `VoteTmdb` né `VoteTmdbCount`.
  - §12.2 descrive il componente voto come `VoteBlended` e il caso senza voto.
- Verifica: `dotnet test tests/Pickarr.Core.Tests --filter FullyQualifiedName~RecoPriorTests`.
- Contesto: D-005, V-04, K-003, FLUSSO-04.
- Assunzioni: nessuna.
- Rollback: ripristinare `RecoPrior`, i call site e §12.2.

### T10 — Feature di livello 3

- Scope: `RecoLevel3.BuildFeatures` e gli indici; `RecoLevel3Training` / `RecoLevel3TrainingTitle`; call site in `RecoBuildService` e `RecoEvalService`; test `RecoLevel3ModelTests`, `RecoEvalMetricsTests`, `RecoTemporalRankingTests` e gli altri compilati che chiamano `BuildFeatures`; `docs/recommendations.md` sulla feature di livello 3 (sezione che elenca year, vote, popularity).
- Requisiti: R-08, R-12 (feature di livello 3).
- Acceptance:
  - L'argomento voto di `BuildFeatures` è il voto calcolato: valore/10, assente → 0,5. Gli indici già esistenti, incluso `VoteFeatureIndex` e `PopularityFeatureIndex`, restano gli stessi numeri.
  - Nuovo indice in coda. Feature = `log10(1+v)/6` con tetto 1, dove `v` è la somma dei conteggi pesabili. `v` assente o 0 → 0.
  - `FeatureCount` e `ScalarFeatureCount` includono la nuova cella. `VectorType` usa `FeatureCount`.
  - Training e score della stessa build leggono `VoteBlended` e i conteggi correnti.
  - La doc elenca le due feature e dice che un cambio di formula o di `C` è raccolto dal build successivo, perché il modello non è persistito; l'import accoda un build subito solo oltre la soglia di D-006.
- Verifica: `dotnet test tests/Pickarr.Core.Tests --filter FullyQualifiedName~RecoLevel3` e `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~RecoLevel3`. Se un test eval fuori da quei filtri non compila, includerlo e citare il filtro nel log.
- Contesto: D-006, D-011, V-05, V-06, K-004, K-005, FLUSSO-04.
- Assunzioni: nessuna.
- Rollback: ripristinare vettore, training, call site, test e paragrafo di doc di questo task.

## TR-06 — Allineamento specifiche

### T11 — D-03 e coerenza dei docs

- Scope: `docs/functional-spec.md` riga D-03; controllo che `docs/technical-spec.md`, `docs/recommendations.md` e `docs/user/features.md` non si contraddicano sui punti già scritti in T05, T07, T08 e T10. `docs/developer/architecture.md` solo se elenca i job e omette `imdb-ratings`.
- Requisiti: R-12.
- Acceptance:
  - D-03, in inglese, dice che il voto di pubblico mostrato è il voto calcolato dal dataset IMDb `title.ratings.tsv.gz` e dal voto TMDB, e che OMDb non è una sorgente.
  - Il resto della tabella §11.3 non viene tradotto né riscritto (A-001).
  - Una rilettura dei quattro documenti non lascia «solo voto TMDB» come comportamento attuale.
- Verifica: lettura dei punti citati, evidenza nel log con i percorsi. Nessun test automatico.
- Contesto: D-012, A-001, K-006.
- Assunzioni: nessuna.
- Rollback: ripristinare solo le righe di spec toccate da questo task.

## TR-07 — Verifica finale indipendente

### T12 — Verifica finale indipendente

- Scope: perimetro di T01–T11, test, migrazione, docs e requisiti di ANALYSIS.md §1 e §7.
- Requisiti: ANALYSIS.md §1 e modifiche approvate in §7.
- Acceptance: criteri obbligatori verificati sulla versione identificata, nessun difetto bloccante, rinvii approvati espliciti, rapporto indipendente PASS referenziato in STATUS.md §9.
- Verifica: protocollo `references/final-verification.md` della skill. Revisore in contesto separato, senza correzioni a codice, test o piano. Se il contesto separato non c'è: handoff a una nuova sessione, task bloccato, niente autoverifica.
- Evidenze: rapporto in ARCHIVE.md; riepilogo in STATUS.md §9.

## TR-08 — Remediation della verifica T12

Origine: rapporto T12 tentativo 1, ARCHIVE.md §A-03, esito FAIL.

### T13 — Formula nei docs e riga BR-REC-15

- Scope: `docs/technical-spec.md` (paragrafo sotto la tabella job, accanto a `ImdbRatingsJob`, oppure sezione dello schema `Titles`); `docs/recommendations.md` riga BR-REC-15 e, se serve, un rimando alla formula in §12.2; `docs/user/features.md` solo se serve una frase in più sul calcolo. Nessun codice né test.
- Requisiti: R-12. Findings F-003, F-002.
- Acceptance:
  - La spec tecnica, in inglese, descrive: fonte pesabile solo con voto e conteggio `> 0`; `w = v/(v+m)` con `m = 500`; shrink per fonte `w·R + (1 − w)·C`; `C` = media del dump IMDb pesata per i voti, salvata a ogni import riuscito e uguale per IMDb e TMDB; media delle due curve pesata per `w`; una sola fonte pesabile, oppure un solo voto grezzo, → voto grezzo; due grezzi senza conteggi positivi o nessun voto → nullo. Il testo coincide con `AudienceScore.Blend`.
  - BR-REC-15 torna a due celle. La frase «A rating rebuilds the voting profile and others still under `N0`.» sta nella cella Rule.
  - Nessuna altra riga di tabella cambia numero di celle.
- Verifica: lettura dei punti citati confrontata con `src/Pickarr.Core/Metadata/AudienceScore.cs`; conteggio dei `|` sulla riga BR-REC-15 uguale all'intestazione `| ID | Rule |`. Evidenza nel log con percorsi.
- Contesto: R-12, D-002, D-003, D-009, A-03 F-002 e F-003.
- Assunzioni: nessuna.
- Rollback: ripristinare solo le righe di docs toccate da questo task.

### T14 — RecoBench allineato a voto calcolato e conteggio

- Scope: `tools/Pickarr.RecoBench/BenchmarkCatalogInput.cs` (query `Titles` e costruzione di `RecoTitleStats`), `tools/Pickarr.RecoBench/TemporalRanking.cs` (chiamata a `BuildFeatures`) e il tipo che porta le stats del titolo nel benchmark se serve un campo per il conteggio. Test esistenti del benchmark se presenti in `tests/`.
- Requisiti: coerenza con R-07 e R-08 nel tool di valutazione. Finding F-001.
- Acceptance:
  - La query legge `VoteBlended`, `VoteImdb`, `VoteImdbCount`, `VoteTmdb`, `VoteTmdbCount`, `Popularity` per nome o con indici corretti; `RecoTitleStats.VoteBlended` riceve `VoteBlended`.
  - `TemporalRanking` passa `usableVotes` calcolato con `RecoLevel3.UsableVoteCount` sugli stessi quattro valori.
  - Nessun cambio a `src/` o alle costanti di Core.
  - `dotnet build Pickarr.slnx` riesce; i test che referenziano `Pickarr.RecoBench` passano.
- Verifica: `dotnet build Pickarr.slnx` e `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~RecoTemporal|FullyQualifiedName~RecoSnapshot|FullyQualifiedName~RecoPreparation|FullyQualifiedName~RecoModelExperiment|FullyQualifiedName~RecoEmbeddingPerformance"`: sono i test che referenziano `Pickarr.RecoBench` al 2026-11-26. Citare nel log ogni altro filtro eseguito.
- Contesto: D-005, D-011, V-04, V-05, A-03 F-001.
- Assunzioni: il benchmark legge un file SQLite già migrato con `AddImdbRatings`. Se si apre un database non migrato, la query fallisce: è il comportamento atteso, non si aggiungono fallback.
- Rollback: ripristinare i due file del tool.

## TR-09 — Verifica finale indipendente, tentativo 2

### T15 — Verifica finale indipendente

- Scope: perimetro di T01–T11 e T13–T14, test, migrazione, docs, requisiti di ANALYSIS.md §1 e §7, findings F-001…F-003.
- Requisiti: ANALYSIS.md §1 e modifiche approvate in §7.
- Acceptance: criteri obbligatori verificati sulla nuova versione identificata, F-001…F-003 chiusi con evidenza, nessuna regressione rispetto al rapporto A-03, nessun difetto bloccante, rinvii approvati espliciti, rapporto indipendente PASS referenziato in STATUS.md §9.
- Verifica: protocollo `references/final-verification.md` della skill. Revisore in contesto separato da chi esegue TR-08. Non riusa prove o versione del tentativo 1.
- Evidenze: rapporto in ARCHIVE.md; riepilogo in STATUS.md §9.

## TR-10 — Remediation di F-004

Origine: rapporto T15, ARCHIVE.md §A-05. L'utente ha scelto il fix con nuova verifica il 2026-11-26. Il PASS di T15 è da ripetere.

### T16 — Test del conteggio voti nel livello 3 del benchmark

- Scope: `tests/Pickarr.Infrastructure.Tests/RecoTemporalRankingTests.cs`. Nessun cambio a `src/` o `tools/`.
- Requisiti: coerenza di R-08 nel tool di valutazione. Finding F-004.
- Acceptance:
  - Un test fa passare `TemporalRanking.Score` con un modello di livello 3 finto e verifica che ogni esempio di training abbia, all'indice `VoteCountFeatureIndex`, `min(1, log10(1+v)/6)` con il `UsableVoteCount` del titolo.
  - Lo stesso test verifica il percorso di scoring: con uno scorer che restituisce quella feature, l'ordine del livello 3 segue il conteggio.
  - Togliendo `title.UsableVoteCount` dalla chiamata a `BuildFeatures`, il test fallisce.
- Verifica: `dotnet test tests/Pickarr.Infrastructure.Tests` con il filtro dei test che referenziano `Pickarr.RecoBench` (vedi T14); prova di mutazione con build esplicita, poi ripristino.
- Contesto: F-004, V-05.
- Assunzioni: nessuna.
- Rollback: rimuovere il test e il modello finto aggiunti.

## TR-11 — Verifica finale indipendente, tentativo 3

### T17 — Verifica finale indipendente

- Scope: perimetro di T01–T11, T13–T14 e T16; requisiti di ANALYSIS.md §1 e §7; findings F-001…F-004.
- Requisiti: ANALYSIS.md §1 e modifiche approvate in §7.
- Acceptance: criteri obbligatori verificati sulla nuova versione identificata, F-004 chiuso con evidenza, nessuna regressione rispetto ad §A-05, nessun difetto bloccante, rinvii approvati espliciti, rapporto PASS referenziato in STATUS.md §9.
- Verifica: protocollo `references/final-verification.md`. Revisore in contesto separato da chi esegue TR-10. Non riusa prove o versione dei tentativi 1 e 2.
- Evidenze: rapporto in ARCHIVE.md; riepilogo in STATUS.md §9.
