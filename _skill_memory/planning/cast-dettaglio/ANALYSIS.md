# ANALYSIS — cast-dettaglio

> Assessment: 2026-11-24, Copilot SDK; branch `main`.

## 1. Contesto e perimetro

### 1.1 Richiesta e requisiti

Richiesta originale: «nella modale di dettaglio bisogna aggiungere una riga con le foto e i nomi degli attori principali, con link alle loro pagine imdb. i dati possono essere recuperati in batch da un nuovo job e popolati in maniera asincrona. nella modale di dettaglio il box della library con i pulsanti di aggiunta a sonarr/radarr prende molto spazio e si può ridurre il tutto inserendolo sotto i pulsanti love/like/dislike/ecc.»

| ID | Requisito e risultato osservabile | Task | Fonte |
| --- | --- | --- | --- |
| R-01 | Mostrare fino a 12 attori principali in ordine, con nome, foto e link IMDb quando noto; foto mancante con segnaposto, ID mancante senza link. | T01–T03, T05, T07 | Richiesta e risposta utente 2026-11-24 |
| R-02 | Nuovo job asincrono orario e manuale, batch limitati, backfill titoli esistenti e rinnovo dopo 30 giorni; dati condivisi e chiamate TMDB controllate. | T01–T04 | Risposta utente 2026-11-24 |
| R-03 | Non bloccare l'apertura: prima del backfill mostrare i nomi disponibili con segnaposto e senza link; dati aggiornati alla riapertura/ricarica, senza polling. | T05, T07 | Risposta utente 2026-11-24 |
| R-04 | Library compatta sotto azioni personali, mantenendo stato, refresh, aggiunta, eliminazione e gestione stagioni; rispettare configurazione Arr e browse-only. | T06 | Richiesta e risposta utente 2026-11-24 |
| R-05 | Segnali e comportamento delle raccomandazioni invariati: primi cinque crediti cast come prima. | T01–T03, T08 | Risposta utente 2026-11-24 |
| R-06 | Evoluzione SQLite senza perdita di dati; aggiornare guide e specifiche pertinenti, verificare regressioni e integrazione indipendentemente. | T01–T09 | Istruzioni repository e skill |

### 1.2 Fonti analizzate

- `src/Pickarr.WebApp/src/features/title/TitleDialog.tsx`, `TitleDialog.rating.test.tsx`, `src/Pickarr.WebApp/src/api/types.ts`.
- `src/Pickarr.Core/Catalog/TitleQueryModels.cs`, `TmdbImageUrls.cs`, `src/Pickarr.Core/Metadata/TmdbModels.cs`, `src/Pickarr.Core/Domain/Entities/Person.cs`, `TitleCredit.cs`.
- `src/Pickarr.Infrastructure/Catalog/TitleQueryService.cs`, `RecoBuildService.cs`, `RecoQueryService.cs`, `RecoEvalService.cs`; `src/Pickarr.Infrastructure/Metadata/EnrichmentService.cs`, `Tmdb/TmdbMetadataProvider.cs`, `TmdbJsonModels.cs`, `TmdbRateGate.cs`.
- `src/Pickarr.Infrastructure/Jobs/EnrichmentJob.cs`, `MetadataJobRun.cs`, `ScheduleReconciler.cs`; ricerche in job admin e test pertinenti.
- `docs/AGENTS.md`, ricerche mirate in `docs/functional-spec.md`, `docs/technical-spec.md`, `docs/recommendations.md`, `docs/user/features.md`, `docs/developer/architecture.md`.

### 1.3 Stato as-is

Il dettaglio espone `Cast` come nomi da `Title.CastJson`; la modale li visualizza come testo. TMDB fornisce già crediti titolo, ma la persistenza strutturata di arricchimento ne salva cinque per il cast. `Person` contiene solo ID TMDB e nome. I tre servizi reco leggono tutti i crediti cast persistiti. Il job enrichment è orario; refresh metadati è giornaliero.

## 2. Componenti principali

| Componente | Ruolo | File | Accoppiamento |
| --- | --- | --- | --- |
| Modale | Dati dettaglio, valutazioni e comandi Arr | `src/Pickarr.WebApp/src/features/title/TitleDialog.tsx` | API e React Query |
| Query dettaglio | Cast JSON, immagini, dati personali e Arr | `src/Pickarr.Infrastructure/Catalog/TitleQueryService.cs` | SQLite e DTO Core |
| TMDB/enrichment | Recupero crediti, persistenza prime cinque persone | `src/Pickarr.Infrastructure/Metadata/Tmdb/TmdbMetadataProvider.cs`, `EnrichmentService.cs` | Rete, rate gate e SQLite |
| Reco | Usa crediti strutturati cast | `src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs`, `RecoQueryService.cs`, `RecoEvalService.cs` | `TitleCredits` |
| Scheduler/admin | Esecuzione e gestione job | `src/Pickarr.Infrastructure/Jobs/ScheduleReconciler.cs`, `Admin/AdminJobService.cs` | Hangfire e sync run |

## 3. Flussi individuati

### FLUSSO-01 — Metadati e cast

- Trigger: enrichment/refresh; il provider richiede il titolo con credits, poi il servizio salva i nomi e i primi cinque crediti.
- Dipendenze: TMDB, rate gate, database; il backfill dei titoli esistenti richiede elaborazione esplicita, anche per titoli con meno di 12 attori.
- Test esistenti: `tests/Pickarr.Infrastructure.Tests/EnrichmentServiceTests.cs`.

### FLUSSO-02 — Dettaglio e Library

- Trigger: apertura modale e `fetchTitle`; `TitleQueryService.GetTitleAsync` costruisce DTO, il componente mostra azioni personali e sezione Arr.
- Dipendenze: API, React Query, configurazione Sonarr/Radarr, stato stagioni.
- Test esistenti: `TitleQueryServiceTests.cs`, `TitleDialog.rating.test.tsx`.

### FLUSSO-03 — Raccomandazioni

- Trigger: build, query ed evaluation; i crediti cast strutturati contribuiscono ai vettori.
- Rischio: aggiungere sette crediti alla stessa relazione altera le raccomandazioni.
- Test esistenti: `RecoBuildServiceTests.cs` e relativi test query/eval.

## 4. Dipendenze e vincoli tecnici

| Dipendenza | Tipo | Vincolo | Impatto |
| --- | --- | --- | --- |
| TMDB | Servizio | V-02, V-03 | Usare client e rate gate esistenti; gestire errori e disponibilità parziale. |
| SQLite/EF | Persistenza | V-01 | Migrazione additiva; preservare dati esistenti. |
| Hangfire/admin | Infrastruttura | V-02 | Job osservabile, schedulato e lanciabile manualmente. |
| Reco | Prodotto | V-04 | Non cambiare feature, ranking o dati di input esistenti. |
| Sonarr/Radarr | Integrazione | V-05 | Nessuna regressione funzionale o nel browse-only. |

## 5. Problematiche e tech debt rilevati

| ID | Gravità | Descrizione | Impatto |
| --- | --- | --- | --- |
| A-001 | Alta | `TitleCredits` alimenta direttamente le raccomandazioni e contiene i primi cinque cast. | Un'estensione ingenua a 12 cambia il ranking contro R-05. |
| A-002 | Media | `CastJson` non contiene identificativi persona, foto o IMDb; i crediti strutturati non coprono 12 persone. | Occorrono backfill e modello separato senza perdere i nomi esistenti. |
| A-003 | Media | Il worktree contiene modifiche preesistenti anche a reco e documentazione. | Riconciliare prima di ogni tranche, preservando il lavoro altrui. |

## 6. Architettura target

### 6.1 Principi

Persistenza condivisa e separata dai segnali reco; dettaglio disponibile anche a job incompleto; acquisizione idempotente, progressiva e limitata con esiti espliciti. Nessun recupero di rete sincrono all'apertura.

### 6.2 Pattern

Un modello di cast per dettaglio, distinto da `TitleCredits`, mantiene ordine e fino a 12 associazioni per titolo e dati persona riutilizzabili. Un job Hangfire usa il client TMDB esistente per popolarlo e rinnovarlo; l'API aggiunge dati strutturati mantenendo il campo nomi per compatibilità. La UI usa il nuovo campo e ripiega sui nomi storici. Dettagli operativi e conferma delle API esterne sono in PLAN.md.

### 6.3 Alternative

| Alternativa | Vantaggio | Motivo esclusione |
| --- | --- | --- |
| Estendere direttamente `TitleCredits` a 12 | Riutilizzo semplice | Cambia il segnale reco; R-05. |
| Chiamata TMDB dalla modale | Dati immediati | Blocca/lega il dettaglio alla rete e contrasta R-02/R-03. |

## 7. Scoperte post-assessment

| Data | Sezione | Scoperta | Impatto |
| --- | --- | --- | --- |
| 2026-11-24 | §4 | `git ls-tree -r HEAD src tests` rileva 470 gruppi di file tracciati con percorsi distinti solo per maiuscole; blob HEAD uguali ma Windows li sovrappone fisicamente. | F-004: T01 bloccato; schema e snapshot coinvolti, scelta umana necessaria prima di modificare codice. |
| 2026-11-24 | §4 | Utente autorizza a procedere mantenendo coerenti le varianti tracciate e verificando il diff. | STATUS.md D-04; preservare index e lavoro altrui. |
| 2026-11-24 | §4 | I file nuovi T01 risultano in `git status --untracked-files=all` solo nella variante `Pickarr`; le modifiche a file già tracciati risultano in entrambe. Nel frattempo alcuni file reco estranei sono cambiati. | F-004: T01 bloccato; non agire su index o modifiche altrui senza scelta esplicita. |

## 8. Fatti e ipotesi riusabili

| ID | Tipo | Informazione | Fonte e versione | Rivalidare |
| --- | --- | --- | --- | --- |
| K-01 | FACT | `CastNames` prende 12 nomi; `MapMovieCredits`/`MapTvCredits` prendono 5 persone. | `TmdbMetadataProvider.cs`, HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791` | Cambio mapping o enrichment. |
| K-02 | FACT | I tre servizi reco leggono i crediti cast senza limite nella query. | `RecoBuildService`, `RecoQueryService`, `RecoEvalService`; snapshot S-01 | Cambio query o worktree. |
| K-03 | FACT | `Person` non ha foto o IMDb; il dettaglio usa `CastJson`. | `Person.cs`, `TitleQueryService.GetTitleAsync`; HEAD sopra | Cambio modello o query. |
| K-04 | HYPOTHESIS | Credits titolo forniscono profilo foto e lookup persona fornisce IMDb; verificare contratti effettivi e fixture prima di implementare. | Provider TMDB esistente; verifica endpoint e fixture in T02 | Cambio API TMDB o DTO. |
| K-05 | DECISION | Fallback, 12 attori, job orario/30 giorni, reco invariata e Library compatta. | STATUS.md §4 D-01/D-02; risposte utente 2026-11-24 | Modifica requisiti approvata. |

### S-01 — Snapshot locale iniziale

Ambito: `src`, `tests`, `docs`, `Directory.Packages.props`, `Pickarr.slnx`; HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`. SHA-256 del testo UTF-8 delle righe di `git diff --no-ext-diff --no-textconv --binary`, unite con LF: unstaged `DFE72799A62DD63C8F136A558F521F76F368D58797A9CDDF21F49A76098902C3`; stessa operazione con `--cached`: `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`. Nessuna freshness precedente verificabile.

File non tracciati nello stesso ambito, SHA-256 dei byte:

| File | SHA-256 |
| --- | --- |
| `docs/developer/recommendation-snapshots.md` | FCED7ACA7414C2E6FB305A2AE9AE2A0A7FCAD5B9DBD46B3C9E7551C1D503F9B3 |
| `src/Pickarr.Infrastructure/Metadata/Reco/RecoLevel3Training.cs` | 0AE1BE959A934D5FD23CBEBC9ACA87D5E462AAAB002E11268E9097E750C8AC3E |
| `tests/Pickarr.Infrastructure.Tests/RecoEmbeddingPerformanceTests.cs` | D82337F5B13168168A9C2BBB521A15A006785D6F79D6526471B4350AB8B282B8 |
| `tests/Pickarr.Infrastructure.Tests/RecoModelExperimentTests.cs` | 8A5E49CBBB38A1083128D2B11F6AD5C9E2A4A072B340A02A8781CE93AE610782 |
| `tests/Pickarr.Infrastructure.Tests/RecoPreparationTests.cs` | D3F98948B09D34A40C95013CA047562AD1A0AFD0D65C1E2BA90778936B43F92F |
| `tests/Pickarr.Infrastructure.Tests/RecoSnapshotTests.cs` | 97D014A968BB083B3017096A598380B7E904C4A5B4DE4A055F5591C3F8779159 |
| `tests/Pickarr.Infrastructure.Tests/RecoTemporalBenchmarkTests.cs` | 6AC3DD27288902D1354E96E25F1CDDDDABA939D5A7C31FD60628445E5623C8F2 |
| `tests/Pickarr.Infrastructure.Tests/RecoTemporalHistoryTests.cs` | 1B1378A469BB36A2108577A12395D53E77E35555E3F231459A9EEC6B30EE8BB9 |
| `tests/Pickarr.Infrastructure.Tests/RecoTemporalRankingTests.cs` | 53CA3EE36532647C5DCAB34AFFDBB3470288CCA39B91B448F76CC6F4682B91C9 |

### S-02 — Snapshot locale dopo il lavoro parziale T01

Stesso ambito e metodo di S-01, HEAD invariato. Diff unstaged SHA-256 `DB532B1AA18F2D028DBC5A7B8B0EC42E5F6D67B189E14FB8F59A2F78A7D233AF`; staged `E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855`. I file non tracciati di S-01 mantengono lo stesso hash salvo i seguenti; la tabella aggiunge anche i nuovi file rilevati:

| File | SHA-256 |
| --- | --- |
| `docs/developer/recommendation-snapshots.md` | E3C49E8E7F98A33FF41B85CFA8B51B8F425DE263324A585A63F3F5E4CF739D1D |
| `src/Pickarr.Infrastructure/Metadata/Reco/RecoLevel3Training.cs` | EFE5CB064BA59EB504D7812566B7E56B9F061130F73CAA58F18804F175A05109 |
| `tests/Pickarr.Infrastructure.Tests/RecoSimilarServiceTests.cs` | 54776A0F7A3055CBD81478042CD523A16FBA7737E1A2C2B2B2092384889EE572 |
| `src/Pickarr.Core/Domain/Entities/CastPerson.cs` | D0D0A17531DA5C77DCA0ECD01930C9ABC8534A2BAA87840ED59597D25EB7AE3E |
| `src/Pickarr.Core/Domain/Entities/TitleCastMember.cs` | 34EB59469D852A36347739DFB83B9243BEC293A65CD81AA1F67C6A4BD87DA982 |
| `src/Pickarr.Infrastructure/Persistence/Configurations/CastPersonConfiguration.cs` | A7F6B9082F181BA203969E674D765AE7DD1C2A46B8B6B81ABC8376F70EE7DA8B |
| `src/Pickarr.Infrastructure/Persistence/Migrations/20261124160708_AddDetailCast.Designer.cs` | 16FA510E310DEBCF8E27E47325606E1C2E60E5F4E3412E90F2C4A5EF5BEF504B |
| `src/Pickarr.Infrastructure/Persistence/Migrations/20261124160708_AddDetailCast.cs` | E6655BAB6216AF246A81A4A279B59AB009B75ED2C1F90EE614531D03F18320A8 |
| `tests/Pickarr.Infrastructure.Tests/DetailCastMigrationTests.cs` | 812FA47EF940B5434228141B116E19EC72FEBFCCC84C6146DED52B5FD8217308 |
