# ARCHIVE — cast-dettaglio

Creato: 2026-11-24. Stato vivo: `STATUS.md`.

## Indice delle sezioni

Nessun elemento archiviato.

## T09 — Verifica finale indipendente

**Esito:** FAIL

**Versione:** HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791` più modifiche staged e worktree correnti.

**Verifiche eseguite:**

- `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~DetailCast|FullyQualifiedName~TitleQueryServiceTests|FullyQualifiedName~AdminJobServiceTests|FullyQualifiedName~JobStateTests|FullyQualifiedName~ScheduleReconcilerTests|FullyQualifiedName~EnrichmentServiceTests|FullyQualifiedName~RecoBuildServiceTests|FullyQualifiedName~RecoQueryServiceTests|FullyQualifiedName~RecoEvalServiceTests"`: 72/72 PASS.
- `dotnet test tests/Pickarr.Integration.Tests --filter "FullyQualifiedName~CatalogApiTests"`: 3/3 PASS.
- Frontend `TitleDialog.rating.test.tsx`: 5/5 PASS.
- Frontend `npm run typecheck`: PASS.
- Frontend `npm run lint`: 0 errori, 11 warning preesistenti.
- Frontend `npm run build`: PASS.

**Finding bloccanti:**

1. `DetailCastService.ProcessBatchAsync` può riprovare continuamente gli stessi titoli falliti e non garantire progresso agli altri record.
2. Un valore `null` da TMDB viene trattato come cast vuoto valido, cancellando dati precedenti e rinviando il rinnovo dopo un 404.
3. Il DbContext conserva modifiche tracciate dopo un errore parziale, con possibile contaminazione dei successivi salvataggi.
4. Le modifiche reco presenti nel worktree alterano `nEff`, pesi e vettori rispetto al comportamento approvato; R-05 non è verificata come invariata sulla versione completa.
5. Gli ID IMDb condivisi non hanno rinnovo dopo il primo fetch assente o 404.
6. Il diff/staging contiene eliminazioni di varianti Git case-colliding non riconciliate; in particolare sono state eliminate varianti `PickArr` presenti in HEAD.

**Richieste parziali:**

- Migrazione cast additiva, compatibilità del campo storico, lettura locale in dettaglio e job manuale/orario sono presenti nel codice.
- Library/Arr e layout mobile sono parzialmente verificati dai test e non hanno una verifica visuale completa.
- I test mirati passano, ma non provano integrazione reale con TMDB/Hangfire/Arr né concorrenza reale.

**Vincolo dati:** nessun database SQLite è stato cancellato, ricreato o sostituito.

**Ciclo correttivo:** creare un nuovo ciclo con ID correttivi separati per i finding 1–6. Nessuna correzione è stata applicata durante T09. Prima della chiusura servono: distinzione 404/cast vuoto/errore temporaneo, progresso con retry differito, isolamento transazionale per titolo, riconciliazione del diff reco/case-collision, rinnovo IMDb e verifica visuale desktop/mobile.

## T10 — Correzione post-T09

**Esito:** PASS sui controlli correttivi applicati.

**Modifiche:**

- `CastFetchAttemptAt` aggiunto al modello `Title` con migrazione additiva `20261125082232_AddCastFetchAttemptAt`.
- `DetailCastService` usa timestamp di tentativo/retry di un'ora e processa titoli separatamente.
- 404 TMDB distingue il titolo assente da un titolo valido con cast vuoto e preserva associazioni/stamp precedenti.
- Eccezioni temporanee e 404 marcano il tentativo senza salvare modifiche parziali nel DbContext.
- IMDb viene rinnovato quando mancante o più vecchio di 30 giorni.
- `TmdbResourceNotFoundException` mantiene il fallback alternativo in enrichment.
- Non sono stati modificati crediti reco, index Git o varianti case-colliding preesistenti.

**Verifiche:**

- Cast/migrazione/job/enrichment/reco: 52/52 PASS.
- Infrastructure completa: 267/269 pass; i due smoke prestazionali falliti nella suite completa sono passati entrambi in rerun isolato.
- Integration: 73/73 PASS.
- Core: 132/132 PASS.
- Frontend dettaglio: 5/5 PASS e typecheck PASS.
- EF: nessun pending model change.

## T12 — Priorità retry cast

**Esito:** PASS per il finding starvation selezione retry.

**Modifica:**

- `DetailCastService` ordina i titoli eleggibili prima per `CastFetchAttemptAt == null`, quindi per timestamp tentativo crescente.
- Un titolo mai tentato precede i retry, indipendentemente da `CastFetchedAt`.
- I retry sono ordinati dal tentativo meno recente al più recente.

**Verifiche:**

- Cast/series/enrichment/job: 29/29 PASS.
- Regressioni backend detail/query/reco/enrichment: 73/73 PASS.
- Integration catalog: 3/3 PASS.
- Core: 132/132 PASS.
- Frontend detail: 5/5 PASS e typecheck PASS.
- EF: nessun pending model change.

**Resta OPEN:**

- Riconciliazione esplicita delle modifiche reco preesistenti rispetto a R-05.
- Riconciliazione Git delle varianti case-colliding e della migrazione nuova con D-04.

## T09-R3 — Verifica indipendente finale sulla versione T12

**Esito:** FALLBACK FAIL

**Correzioni confermate:**

- `DetailCastService` ordina i titoli senza tentativo prima dei retry e i retry per tentativo meno recente.
- 404 cast resta distinto da cast vuoto e preserva dati precedenti.
- IMDb viene rinnovato dopo 30 giorni.
- 404 stagioni ed episodi sono gestiti.
- Migrazione e snapshot sono coerenti nel worktree; il test migration passa.

**Finding bloccanti residui:**

1. Le modifiche reco preesistenti nel worktree cambiano feature, vettori, `nEff` e ranking rispetto a HEAD; R-05 non è riconciliata come invariante.
2. L'index/worktree non è riconciliato con le varianti case-colliding: migrazione e designer `AddCastFetchAttemptAt` risultano non tracciati.

**Verifiche:**

- Infrastructure: 272/272 PASS.
- Core: 132/132 PASS.
- Integration: 73/73 PASS.
- Frontend: 122/122 PASS e typecheck PASS.
- Nessun database SQLite cancellato o sostituito.
- Nessun commit, modifica all'index o correzione applicata dal revisore.

## T09-R1 — Seconda verifica indipendente

**Esito:** FAIL

**Versione:** HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791` più modifiche staged e worktree correnti.

**Finding bloccanti:**

1. I titoli in errore possono mantenere priorità dopo il gate di un'ora e impedire il backfill progressivo di altri titoli.
2. `TmdbResourceNotFoundException` propagata da `GetTvAsync` non è gestita da `SeriesGuideService` per le stagioni non trovate.
3. Le modifiche reco preesistenti nel worktree cambiano calcolo o pesi rispetto a HEAD e non sono riconciliate come invarianza approvata R-05.
4. Il diff/staging elimina varianti case-colliding `PickArr`, mentre la migrazione e il designer nuovi non sono tracciati.

**Verifiche verificabili dal revisore:**

- Infrastructure mirata su binari esistenti: 53/53 PASS.
- Core: 132/132 PASS.
- Database SQLite dei test: in memoria.

**Limiti dichiarati:**

- Integration e frontend non rieseguiti dal revisore.
- EF pending-model check non rieseguito nel contesto separato.
- Nessuna verifica visuale desktop/mobile.
- Nessuna modifica o cancellazione SQLite applicata.

## T11 — Fairness retry e 404 serie

**Esito:** PASS sui finding 1–2 di T09-R1; F-009 resta OPEN.

**Modifiche:**

- `DetailCastService` ordina i retry per `CastFetchAttemptAt` crescente, dopo i titoli mai tentati, evitando che gli stessi titoli recenti restino sempre in testa.
- `SeriesGuideService.ListSeasonsAsync` cattura `TmdbResourceNotFoundException` e restituisce una lista vuota.
- `SeriesGuideService.ListEpisodesAsync` cattura lo stesso 404 per i percorsi stagionali ed restituisce una lista vuota.

**Test:**

- Cast/series/enrichment/job: 28/28 PASS.
- Regressioni detail/query/reco/enrichment: 72/72 PASS.
- Integration catalog: 3/3 PASS.
- Core: 132/132 PASS.
- Frontend detail: 5/5 PASS e typecheck PASS.
- EF: nessun pending model change.

**Limiti non modificati:**

- Le modifiche reco preesistenti nel worktree non sono state sovrascritte o approvate in questa tranche.
- Le collisioni case-insensitive e l'index Git non sono stati modificati; D-04 resta valido.

## T09-R4 — Verifica finale dopo deduplica cast

**Esito:** PASS

**Versione:** HEAD `6f5887115969af8fdab15ef43347c5507139d9dd` più le sole modifiche T09 a `TmdbMetadataProvider` e `EnrichmentServiceTests`; hash SHA-256 del diff binario: `4542007fea010c21508477c60dfa1f96431740d960792f4c69b6bdce6c541127`.

**Correzione applicata:**

- `MapDetailCast` ordina i crediti, deduplica per ID persona mantenendo la prima occorrenza e applica poi il limite di 12.
- Il test WireMock include un duplicato prima del dodicesimo attore distinto e asserisce ordine e 12 ID univoci.
- Il difetto bloccava il salvataggio per il vincolo chiave `(TitleId, PersonId)` e causava retry del titolo.

**Verifica indipendente:**

- Il revisore `cast-t09-review` ha confermato il fix e non ha rilevato altri blocker R-01–R-06.
- La verifica è read-only e non ha eseguito test; la suite è stata eseguita separatamente.
- R-05 è circoscritto al cast: `DetailCastService` usa relazioni dedicate e il test verifica che i `TitleCredits` reco rimangano intatti. Le modifiche reco già committed su HEAD sono distinte e documentate, non introdotte dalla tranche cast.
- HEAD contiene zero gruppi di path case-colliding; le migrazioni cast e i relativi designer sono tracciati.

**Verifiche eseguite:**

- `dotnet test tests/Pickarr.Infrastructure.Tests --no-restore --filter FullyQualifiedName~EnrichmentServiceTests`: 13/13 PASS.
- `dotnet test Pickarr.slnx --no-restore`: Core 132/132, Infrastructure 272/272, Integration 73/73 PASS.
- Frontend: 122/122 test, typecheck e build PASS; lint 0 errori e 11 warning preesistenti.
- EF CLI 10.0.14: nessun pending model change.
- `git diff --check`: PASS; nessuna modifica applicata al database SQLite.

**Limiti:** nessuna verifica visuale desktop/mobile; nessun test eseguito dal revisore indipendente.
