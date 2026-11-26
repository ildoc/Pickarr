# ARCHIVE — voti-imdb

> Documento append-only: non modificare sezioni già scritte.
> Stato vivo → `STATUS.md`
> Analisi tecnica → `ANALYSIS.md`
> Creato: 2026-11-26

---

## Indice delle sezioni

| ID | Data | Argomento/task | Motivazione | Destinazione |
| --- | --- | --- | --- | --- |
| A-01 | 2026-11-26 | Activity log INIT | Il log vivo supera 10 righe DONE | § A-01 |
| A-02 | 2026-11-26 | Activity log T01 | Il log vivo supera 10 righe DONE | § A-02 |
| A-03 | 2026-11-26 | T12 — verifica finale, tentativo 1 | Rapporto della verifica indipendente | § A-03 |
| A-04 | 2026-11-26 | Activity log T02…T08 | Il log vivo supera 10 righe DONE dopo TR-08 | § A-04 |
| A-05 | 2026-11-26 | T15 — verifica finale, tentativo 2 | Rapporto della verifica indipendente | § A-05 |
| A-06 | 2026-11-26 | Findings F-001…F-003 | ≥ 3 finding chiusi e verificati da T15 | § A-06 |
| A-07 | 2026-11-26 | T17 — verifica finale, tentativo 3 | Rapporto della verifica indipendente; F-004 chiuso | § A-07 |

---

## A-01 — Activity log INIT

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-26 | INIT | Creata la KB `voti-imdb` dopo il gate | DONE | ANALYSIS.md, PLAN.md, STATUS.md, AGENT_KB.md |

---

## A-02 — Activity log T01

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-26 | T01 | Formula `AudienceScore` in Core | DONE | `dotnet test tests/Pickarr.Core.Tests --filter FullyQualifiedName~AudienceScoreTests`: 9 passed, 0 failed |

---

## A-03 — T12 verifica finale indipendente, tentativo 1

### Identificazione

| Campo | Valore |
| --- | --- |
| Data | 2026-11-26 |
| Revisore e sessione | Agente Cursor in una sessione nuova, aperta per TR-07. Non ha eseguito TR-01…TR-06 |
| Materiali ereditati | `AGENTS.md` radice; KB `voti-imdb` (README, AGENT_KB, STATUS, PLAN, ANALYSIS, ARCHIVE); KB comune; protocollo `references/final-verification.md` della skill |
| Isolamento effettivo | Contesto separato dall'esecutore. Il revisore ha letto STATUS e activity log dell'esecutore, ma non li ha usati come prova: ogni criterio è stato ricontrollato su codice, test e comandi. Non si esclude un bias di modello: esecutore e revisore possono essere della stessa famiglia |
| Versione verificata | HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` su `main` più worktree non committato: 50 file modificati e 16 file nuovi, esclusa `_skill_memory/`. Manifest SHA-256 `489a90e03471771327728dcb93a195632974c000bb4ffffd84ce4b6f7556334c`, calcolato sulla lista ordinata `sha256  path` di `git diff --name-only HEAD` più `git ls-files --others --exclude-standard` senza `_skill_memory/` |
| Modifiche durante la revisione | Nessuna a codice, test, configurazione o piano |

### Comandi ed esiti

| Comando | Esito |
| --- | --- |
| `dotnet test Pickarr.slnx` | Core 151/151, Infrastructure 311/311, Integration 73/73. Build della soluzione, incluso `tools/Pickarr.RecoBench`, riuscita |
| `npm test -- --run` (WebApp) | 41 file, 151/151 |
| `npm run typecheck` | ok |
| `npm run lint` | 0 errori. 5 warning `set-state-in-effect` in `AddToArrDialog.tsx` e `SettingsPage.tsx`, file non toccati dal dominio: preesistenti |
| `dotnet ef migrations has-pending-model-changes` (startup `Pickarr.Api`) | «No changes have been made to the model since the last migration» |
| `dotnet ef migrations list --no-connect` | Ultima migrazione `20261126171739_AddImdbRatings`, dopo `20261125101540_AddArrTitleSlug` |
| Download reale di `title.ratings.tsv.gz` in una cartella temporanea, poi cancellato. Database non toccato | 8.777.638 byte. Header `tconst\taverageRating\tnumVotes`. 1.736.750 righe, 0 malformate. Media pesata per i voti 7,225. K-007 confermata sul file reale |

### Requisiti

| Req | Esito | Evidenza |
| --- | --- | --- |
| R-01 | OK | `ImdbRatingImporter.ImportAsync`: parse completo prima di `BeginTransactionAsync`; delete + insert + snapshot nella stessa transazione. `ImdbRatingImportTests` su sostituzione, header errato, zero righe, somma voti zero |
| R-02 | OK | `ApplyToTitlesAsync` dopo il commit, a batch da 200; `tconst` sparito → colonne IMDb nulle; colonne TMDB non scritte. `ApplyStoredRatingAsync` in arricchimento. Test import e `EnrichmentServiceTests` |
| R-03 | OK | `AudienceScore.Blend`, `MinimumVotes = 500`, `C` argomento. Caso asimmetrico 6,15 vincola `m` |
| R-04 | OK | Fonte singola → grezzo. Nessuna chiave i18n di «pochi voti»: `en.json` aggiunge solo `title.ratingImdb` e `title.ratingVotes` |
| R-05 | OK | `TitleCard` su `ratingScore.toFixed(1)`; `TitleDialog` numero del titolo da `ratings.score` e righe IMDb/TMDB con conteggi. Test `TitleCard.score` e `TitleDialog.rating` |
| R-06 | OK | `TitleQueryService`: `Where(VoteBlended >= minRating)`, sort su `VoteBlended`. Test su filtro e ordine asc/desc con nullo |
| R-07 | OK | `RecoPrior`: `bayesRaw = VoteBlended ?? c`, `VoteDamping` rimosso, pesi 0,7 / 0,3. `RecoTitleStats` non ha più campi TMDB |
| R-08 | OK | `BuildFeatures`: voto/10, indice nuovo `HashBuckets + 11`, `log10(1+v)/6` con tetto. `RecoBuildService` e `RecoEvalService` passano `VoteBlended` e `UsableVoteCount` in training e score. Soglia `ImdbRatingsJob.ShouldRebuildRecommendations` e test del job |
| R-09 | OK | `EnrichmentService` chiama `ApplyStoredRatingAsync` subito dopo `ApplyDetails`, unico punto che scrive `VoteTmdb`. Test verifica nessuna richiesta a `datasets.imdbws.com` |
| R-10 | OK | Eccezione di rete prima dell'import (test job). Gzip non valido e parse rifiutato senza scritture (test import) |
| R-11 | OK | `Up`: 3 `AddColumn`, `UPDATE … WHERE VoteTmdb IS NOT NULL`, 2 `CreateTable`, 1 `CreateIndex`. Nessun drop in `Up`. `ImdbRatingMigrationTests` migra da `AddArrTitleSlug` e verifica il backfill |
| R-12 | **NON SODDISFATTO** | Vedi F-003. Dataset, job e voto mostrato sono descritti; la formula no |
| R-13 | OK | `ScheduleReconciler`: `0 4 * * 0`, `TimeZoneInfo.Local`; trigger in `AdminJobService`, mapping in `HangfireJobActivity`. Test schedule e admin |

Vincoli: V-01…V-10 rispettati nel codice letto. V-11: `RecoHousehold`, embedding, profilo sparso ed esplorazione non compaiono nel diff.

### Findings

| ID | Gravità | Bloccante | Tipo | Posizione | Descrizione |
| --- | --- | --- | --- | --- | --- |
| F-001 | Media | No | Regressione di integrazione | `tools/Pickarr.RecoBench/BenchmarkCatalogInput.cs` righe 76 e 90; `tools/Pickarr.RecoBench/TemporalRanking.cs` riga 237 | La query legge ancora `VoteTmdb, VoteTmdbCount` e passa la colonna 5 (`VoteTmdb`) come `RecoTitleStats.VoteBlended`. Il prior del benchmark diventa il TMDB grezzo senza shrink, diverso sia dal vecchio sia dal nuovo comportamento di produzione. `TemporalRanking` non passa `usableVotes`, quindi la feature di conteggio vale sempre 0. Il tool compila, ma misura un modello diverso da quello in produzione |
| F-002 | Bassa | No | Regressione di documentazione | `docs/recommendations.md` riga 236, BR-REC-15 | La riga ha un separatore `|` in più in una tabella a due colonne. In GFM la cella in eccesso si scarta: la frase «A rating rebuilds the voting profile and others still under `N0`.» sparisce dal render. Prima della modifica era nella stessa cella |
| F-003 | Media | Sì | Requisito R-12 non soddisfatto | `docs/technical-spec.md`, `docs/recommendations.md`, `docs/functional-spec.md`, `docs/user/features.md` | R-12 chiede che i documenti descrivano la formula. Nessun file sotto `docs/` riporta lo shrink per fonte, `m = 500`, `C` come media del dump IMDb pesata per i voti, la media pesata per `w = v/(v+m)`, la regola della fonte singola o il caso «due grezzi senza conteggi → nullo». La guida utente dice solo «weighted IMDb and TMDB score» |

Osservazioni non bloccanti, senza finding:

- `title.ratingVotes` riceve `count` come stringa formattata. Con un solo voto il testo è «1 votes». Nessun criterio lo copre.
- L'import inserisce circa 1,7 milioni di righe via EF a blocchi da 2.000 dentro una transazione: il lock di scrittura SQLite dura quanto l'inserimento. Rischio già dichiarato in ANALYSIS.md FLUSSO-01 e mitigato dall'orario. Durata reale non misurata.
- `.data/Pickarr.db` non è stato aperto né migrato dalla revisione.

### Limiti

- Nessuna esecuzione end-to-end del job contro il file reale su un database: il parser è stato confrontato col file reale solo per header, righe e media, con uno script separato.
- UI non osservata in browser; la verifica UI si basa sui test Vitest e sulla lettura dei componenti.

### Verdetto

**FAIL.** R-12 è un requisito obbligatorio e la formula non è documentata (F-003). F-001 e F-002 sono regressioni introdotte dal cambio, non bloccanti da sole, da correggere nello stesso ciclo. Il resto dei requisiti è verificato su codice e test.

---

## A-04 — Activity log T02…T08

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-26 | T02 | Migrazione `20261126171739_AddImdbRatings` con backfill | DONE | `Up` solo AddColumn/CreateTable/CreateIndex più `UPDATE Titles SET VoteBlended = VoteTmdb`. `dotnet ef migrations has-pending-model-changes`: no changes. `dotnet test tests/Pickarr.Infrastructure.Tests` sui filtri `ImdbRatingMigrationTests` e `TitleQueryServiceTests`: 16 passed, 0 failed. DB di sviluppo non toccato |
| 2026-11-26 | T03 | Parse gzip/TSV e sostituzione dello snapshot | DONE | `ImdbRatingImportTests`: header errato, gzip non valido, zero righe utilizzabili e somma voti zero lasciano il dump precedente. Import valido sostituisce le righe e scrive `C`. Comando unico con T04 e T05: 25 passed, 0 failed |
| 2026-11-26 | T04 | Allineamento voti sui titoli | DONE | Stessi test: match `tt0000001` → IMDb 8 / 500 e blend 7 con TMDB 6 / 500 invariato; `tconst` assente azzerato; solo TMDB resta il grezzo |
| 2026-11-26 | T05 | Job `imdb-ratings`, cron locale e trigger | DONE | `ScheduleReconcilerTests`: cron `0 4 * * 0`, fuso `TimeZoneInfo.Local`. `AdminJobServiceTests`: elenco e trigger. `ImdbRatingsJobTests`: primo import o delta ≥ 0,05 accoda un `RecoBuild`; sotto soglia no; download fallito segna il run Failed e non accoda |
| 2026-11-26 | T06 | Lookup IMDb durante l'arricchimento | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests` sui filtri `EnrichmentServiceTests` e `ImdbRatingImportTests`: 25 passed, 0 failed. Con riga locale, blend della formula e TMDB invariato. Senza riga o senza `ImdbId`, `VoteImdb` nullo e `VoteBlended` uguale al TMDB. Nessuna richiesta a `datasets.imdbws.com` |
| 2026-11-26 | T07 | Filtro, sort e contratti | DONE | `TitleQueryServiceTests`: 17 passed, 0 failed. `minRating` 7 esclude un TMDB 9 con blend 6 e un blend nullo. Sort rating asc mette il nullo prima, desc lo mette ultimo. Lista e dettaglio espongono score e le due fonti. Anche `RecommendationItemDto` ha `ratingScore`, perché la card è la stessa |
| 2026-11-26 | T08 | Card, scheda e copy | DONE | `TitleCard.score.test.tsx` e `TitleDialog.rating.test.tsx`: 14 passed. Badge `7.4` dal blend, nascosto se nullo. Scheda: numero, IMDb 8.0 con 500 voti, TMDB 6.2 con 80 voti; fonte assente omessa. `npm run typecheck` e `npm run lint` ok. Il server su localhost:5173 non era avviato |

---

## A-05 — T15 verifica finale indipendente, tentativo 2

### Identificazione

| Campo | Valore |
| --- | --- |
| Data | 2026-11-26 |
| Revisore e sessione | Subagente Cursor `aa0753a2-d9f6-4589-a017-12b5e835609b`, contesto nuovo, avviato dalla sessione che ha eseguito T12 e TR-08. Non ha eseguito TR-01…TR-08 |
| Materiali ereditati | Mandato scritto dall'orchestratore (requisiti, vincoli, versione, findings F-001…F-003, comandi). Letti: `AGENTS.md`, ANALYSIS.md §1 §6.2 §7, AGENT_KB.md §3, PLAN.md, protocollo `final-verification.md`; anche STATUS.md e ARCHIVE.md §A-03, usati per orientamento e non come prova |
| Isolamento effettivo | Contesto separato da chi ha scritto TR-08. Il mandato viene dall'esecutore di TR-08, quindi l'inquadramento dei findings è ereditato. Bias di modello non escluso |
| Versione verificata | HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` + worktree di 67 file, esclusa `_skill_memory/`. Manifest SHA-256 `c56e777162b0b0fe85de3d63190da140b4933dada2fe04153b78c5996ca57d02`, uguale all'inizio e alla fine della revisione. Stesso metodo di calcolo di §A-03 |
| Modifiche durante la revisione | Nessuna. `.data/` non aperto |

### Comandi ed esiti

| Comando | Esito |
| --- | --- |
| `dotnet test Pickarr.slnx` | Core 151/151, Infrastructure 311/311, Integration 73/73 |
| `npm test -- --run` | 41 file, 151/151 |
| `npm run typecheck` | exit 0 |
| `npm run lint` | exit 0, 0 errori, 10 warning in file non toccati dal dominio (preesistenti) |
| `dotnet ef migrations has-pending-model-changes` | nessuna modifica pendente |
| `dotnet ef migrations list --no-connect` | ultima `20261126171739_AddImdbRatings` |
| Lettura di `Up` di `AddImdbRatings` | 3 `AddColumn`, backfill `WHERE "VoteTmdb" IS NOT NULL`, 2 `CreateTable`, 1 `CreateIndex`; drop solo in `Down` |

### Requisiti e findings precedenti

R-01…R-13: tutti OK, con evidenza su codice e test (dettaglio nel rapporto del subagente, sintetizzato qui). V-01…V-11 rispettati. Casi limite di §6.2 e D-009 coerenti tra codice, test e docs. Catena import → titoli → query/UI → prior/livello 3 → bench su `VoteBlended` e `UsableVoteCount`.

- F-001 chiuso: query RecoBench con indici 5–10 controllati a mano; `TemporalRanking` passa `UsableVoteCount`; asserzioni 7,4/600 e 5/100.
- F-002 chiuso: BR-REC-15 e intestazione hanno 3 `|`; controllo automatico di tutte le tabelle dei quattro docs toccati, nessuna riga con celle diverse dall'intestazione.
- F-003 chiuso: paragrafo «Audience score» in `docs/technical-spec.md` coincide con `AudienceScore.Blend`; §12.2 di `recommendations.md` rimanda alla formula.

### Nuovi findings

| ID | Gravità | Bloccante | Tipo | Posizione | Descrizione |
| --- | --- | --- | --- | --- | --- |
| F-004 | Bassa | No | Lacuna di test introdotta da TR-08 | `tools/Pickarr.RecoBench/TemporalRanking.cs` 239–242; `tests/Pickarr.Infrastructure.Tests/RecoTemporalRankingTests.cs` 169–174 | Nessun test controlla che `TemporalRanking` passi `UsableVoteCount` a `BuildFeatures`. Il codice oggi lo passa; una rimozione futura non farebbe fallire test. Nessun difetto funzionale |

Osservazione: 10 warning di lint invece dei 5 citati in §A-03, tutti in file fuori dal dominio. Non è una regressione del dominio.

### Limiti

- Dataset IMDb reale non riscaricato; K-007 confermata in §A-03.
- UI non osservata in browser.
- Migrazione non applicata a un database reale, per vincolo su `.data`; backfill verificato su SQLite in memoria.

### Verdetto

**PASS.** Criteri obbligatori verificati sulla versione identificata, F-001…F-003 chiusi, nessuna regressione rispetto ad §A-03, nessun difetto bloccante. F-004 non blocca la chiusura.

---

## A-06 — Findings F-001…F-003

| ID | Gravità | Descrizione | Task | Stato |
| --- | --- | --- | --- | --- |
| F-001 | Media | RecoBench passava `VoteTmdb` come `VoteBlended` e non passava `usableVotes` | T14 | RESOLVED, verificato da T15 (§A-05) |
| F-002 | Bassa | BR-REC-15 in `docs/recommendations.md` aveva una cella in più | T13 | RESOLVED, verificato da T15 (§A-05) |
| F-003 | Media | R-12: formula non descritta nei docs | T13 | RESOLVED, verificato da T15 (§A-05) |

---

## A-07 — T17 verifica finale indipendente, tentativo 3

### Identificazione

| Campo | Valore |
| --- | --- |
| Data | 2026-11-26 |
| Revisore e sessione | Subagente Cursor `e274c036-c59e-40f0-b8b0-a6d2828b31ca`, contesto nuovo; non ha eseguito TR-01…TR-10 |
| Materiali ereditati | Mandato dell'orchestratore (che ha eseguito TR-08 e TR-10). Letti `AGENTS.md`, ANALYSIS.md, AGENT_KB.md, STATUS.md, PLAN.md, ARCHIVE.md §A-03…§A-06, protocollo. STATUS e ARCHIVE solo per orientamento. `docs/AGENTS.md` non letto |
| Isolamento effettivo | Contesto separato dall'esecutore di TR-10; inquadramento dei findings ereditato dal mandato; bias di modello non escluso |
| Versione verificata | HEAD `e759d3c9aabee2a451fdb912088634eed180c0f0` + worktree 67 file, esclusa `_skill_memory/`. Manifest SHA-256 `ba1bd9a4c44f7685e1e56c7f9eaa82ce36924ec2edb485d1dccd4f9ae21a3fbe`, uguale a inizio e fine |
| Modifiche durante la revisione | Nessuna. `.data/` non aperto; `dotnet ef` con `Pickarr__DataPath` su una cartella temporanea, dove non è stato creato alcun database |

### Comandi ed esiti

| Comando | Esito |
| --- | --- |
| `dotnet test Pickarr.slnx` | Core 151/151, Infrastructure 312/312, Integration 73/73 |
| `npm test -- --run` | 41 file, 151/151 |
| `npm run typecheck` | exit 0 |
| `npm run lint` | exit 0, 0 errori, 10 warning in file fuori dal dominio |
| `dotnet ef migrations has-pending-model-changes` | nessuna modifica pendente |
| Lettura di `Up` di `AddImdbRatings` | solo additivo più backfill |
| Controllo celle delle tabelle nei 4 docs toccati | 0 righe con celle diverse dall'intestazione |

### Esito

R-01…R-13 OK con evidenza su codice e test; V-01…V-11 rispettati; casi limite di §6.2 e D-009 coperti da `AudienceScoreTests`. F-001…F-003 restano chiusi. F-004 chiuso: il diff di `TemporalRanking.cs` contiene solo le modifiche previste e il nuovo test fallirebbe senza `title.UsableVoteCount` (ragionamento sul codice; la prova di mutazione dell'esecutore è nel log T16).

### Nuovi findings

| ID | Gravità | Bloccante | Tipo | Posizione | Descrizione |
| --- | --- | --- | --- | --- | --- |
| F-005 | Bassa | No | Lacuna di test introdotta dal cambio | `RecoBuildService.cs` 264–268 e 321–332; `RecoEvalService.cs` 121–125 e 451–458; `RecoLevel3Training.cs` 85–92 | Nessun test verifica che training e scoring del livello 3 di produzione ricevano `UsableVoteCount` e `VoteBlended`; le fixture hanno `VoteBlended = VoteTmdb`. Codice corretto oggi |
| F-006 | Bassa | No | Difetto cosmetico della nuova funzione | `RunsTable.tsx` 116; `en.json` `admin.kind`; `runStats.ts` 43–58; `ImdbRatingsJob.cs` 41 | Nello storico run il tipo appare come «ImdbRatings» senza chiave `admin.kind.ImdbRatings` (come già `DetailCast`); le statistiche mostrano «0 credits · N pages» con N = righe del dump |

Osservazioni: dopo un fallimento di `ApplyToTitlesAsync` successivo al commit, lo snapshot ha già la nuova `C` e il rebuild immediato può saltare; lo copre il build orario (D-006). «1 votes» con un solo voto.

### Limiti

Nessuna prova di mutazione da parte del revisore; dataset reale non riscaricato; migrazione non applicata a un database reale; UI non vista in browser.

### Verdetto

**PASS.** Criteri obbligatori verificati sulla versione `ba1bd9a4…3fbe`, F-001…F-004 chiusi, nessuna regressione rispetto ad §A-05. F-005 e F-006 non bloccanti: da correggere o rinviare esplicitamente prima di chiudere il dominio.

| ID | Gravità | Descrizione | Task | Stato |
| --- | --- | --- | --- | --- |
| F-004 | Bassa | Nessun test sul passaggio di `UsableVoteCount` in `TemporalRanking` | T16 | RESOLVED, verificato da T17 |
