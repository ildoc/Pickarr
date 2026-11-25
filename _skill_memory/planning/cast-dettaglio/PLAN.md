# PLAN — cast-dettaglio

Requisiti canonici: `ANALYSIS.md` §1; stati, dimensioni e dipendenze: `STATUS.md` §1.

## TR-01 — Persistenza

### T01 — Persistenza cast dettaglio

- Scope: `src/Pickarr.Core/Domain/Entities`, `src/Pickarr.Infrastructure/Persistence` (DbContext, configurazioni, migrazione e snapshot EF generati); test SQLite in `tests/Pickarr.Infrastructure.Tests`.
- Requisiti: R-01, R-02, R-05, R-06.
- Acceptance: persistenza additiva separata da `TitleCredits` per fino a 12 associazioni ordinate e metadati persona condivisi; stato/data di completamento distinguono mai elaborato, cast vuoto e dati mancanti; migrazione applicabile a dati preesistenti senza modifica ai crediti reco; idempotenza e vincoli di identità verificati.
- Verifica: test mirati SQLite per migrazione su schema precedente popolato, relazioni, vuoto e persistenza; build dei progetti coinvolti. Registrare comandi ed esiti.
- Contesto: F-001/F-002, V-01/V-04; K-03.
- Assunzioni: nessuna.
- Rollback: non ripristinare il DB; correggere con migrazione additiva successiva, preservando dati e modifiche altrui.

## TR-02 — Client TMDB

### T02 — Contratti e provider TMDB

- Scope: `src/Pickarr.Core/Abstractions/IMetadataProvider.cs`, `src/Pickarr.Core/Metadata/TmdbModels.cs`, `src/Pickarr.Infrastructure/Metadata/Tmdb` e test/fixture HTTP esistenti.
- Requisiti: R-01, R-02, R-05.
- Acceptance: recupero fino a 12 crediti ordinati con ID, nome e percorso foto e recupero IMDb persona tramite endpoint TMDB verificato; campi assenti e 404 distinti da errori temporanei; richieste riutilizzano autenticazione, usage e rate gate esistenti. Mapping reco esistente resta invariato.
- Verifica: test WireMock con film/serie, meno di 12, foto/IMDb assenti, errore HTTP e limite; verificare K-04 nei contratti/fixture prima del codice.
- Contesto: K-01/K-04, F-001.
- Assunzioni: K-04, da verificare.
- Rollback: rimuovere soltanto le aggiunte proprie a contratti, client e fixture dopo riconciliazione.

## TR-03 — Elaborazione asincrona

### T03 — Backfill e rinnovo batch

- Scope: nuovo servizio sotto `src/Pickarr.Infrastructure/Metadata`, sue astrazioni Core se necessarie, persistenza T01, provider T02 e test infrastrutturali/fixture.
- Requisiti: R-01, R-02, R-05, R-06.
- Acceptance: titoli esistenti e nuovi con ID TMDB vengono elaborati progressivamente in batch limitati; rinnovo dopo 30 giorni, ordine stabile; cast vuoto e IMDb/foto assenti non causano loop; errori temporanei sono osservabili e ritentabili senza bloccare per sempre gli altri record; dati condivisi tra titoli, aggiornamenti idempotenti e senza toccare `TitleCredits`; interruzione/ripartenza non perde progresso né dati precedenti validi.
- Verifica: test SQLite/WireMock per backfill, nuovi titoli, 30 giorni, cast vuoto, attore condiviso, dati assenti, errore/retry, più batch, ripartenza e crediti reco invariati.
- Contesto: F-001/F-002, V-01–V-04.
- Assunzioni: K-04 verificata in T02.
- Rollback: disabilitare chiamata al servizio/job e correggere con evoluzione additiva; non cancellare dati.

## TR-04 — Job

### T04 — Job e amministrazione

- Scope: `src/Pickarr.Infrastructure/Jobs`, `DependencyInjection.cs`, `Admin/AdminJobService.cs`, `HangfireJobActivity.cs`, `JobText.cs`, tipi sync run e relativi test admin/scheduler; UI Jobs e localizzazione se richiesto dalle convenzioni correnti.
- Requisiti: R-02, R-06.
- Acceptance: job distinto registrato orario e avviabile manualmente nei Jobs, con limite di lavoro per esecuzione, protezione concorrenza, stato/esito e errori visibili; nessuna registrazione duplicata al reconcile.
- Verifica: test `ScheduleReconcilerTests` e `AdminJobServiceTests` più test del nuovo job per batch, errori e trigger; UI test se modificata.
- Contesto: FLUSSO-01, V-02.
- Assunzioni: verificare convenzioni di tracking e naming admin durante l'esecuzione.
- Rollback: disattivare la registrazione e l'avvio del nuovo job senza rimuovere dati.

## TR-05 — API dettaglio

### T05 — Contratto dettaglio e fallback

- Scope: `src/Pickarr.Core/Catalog/TitleQueryModels.cs`, `src/Pickarr.Infrastructure/Catalog/TitleQueryService.cs`, endpoint catalogo e test di query/API in `tests/Pickarr.Infrastructure.Tests` e `tests/Pickarr.Integration.Tests`.
- Requisiti: R-01, R-03, R-05, R-06.
- Acceptance: campo strutturato additivo contiene fino a 12 persone in ordine con nome, URL foto e URL IMDb opzionali; il campo precedente `cast` a stringhe resta compatibile; prima dell'elaborazione i nomi storici restano disponibili, mentre un cast vuoto elaborato non risuscita dati obsoleti; nessuna chiamata TMDB durante GET e dati personali/Arr invariati.
- Verifica: test query e API per ordine/limite, persona condivisa, campi null, vuoto elaborato, fallback storico e compatibilità JSON.
- Contesto: FLUSSO-02, K-03, V-03/V-06.
- Assunzioni: nessuna.
- Rollback: rimuovere solo nuovo campo e query dopo riconciliazione; mantenere dati e contratto precedente.

## TR-06 — Library

### T06 — Library compatta

- Scope: `src/Pickarr.WebApp/src/features/title/TitleDialog.tsx`, eventuali componenti title direttamente coinvolti, `TitleDialog.rating.test.tsx` e test UI pertinenti; `src/Pickarr.WebApp/src/i18n/locales/en.json` se servono nuove stringhe.
- Requisiti: R-04.
- Acceptance: sezione compatta sotto pulsanti personali, responsive e accessibile; stato, refresh, monitoraggio, errori, add/delete e tabella stagioni conservati; nascosta per istanza non configurata; tastiera e dialog restano funzionanti.
- Verifica: Vitest/Testing Library per posizionamento e comportamenti film/serie, configurato/non configurato, add/delete, refresh e monitoraggio; typecheck/lint. Verifica visuale desktop/mobile quando disponibile, con limiti dichiarati.
- Contesto: FLUSSO-02, V-05.
- Assunzioni: nessuna.
- Rollback: ripristinare solo le proprie modifiche UI dopo riconciliazione senza toccare stato Arr.

## TR-07 — Riga attori

### T07 — Cast nella modale

- Scope: `src/Pickarr.WebApp/src/features/title/TitleDialog.tsx` e test, `src/Pickarr.WebApp/src/api/types.ts`, `src/Pickarr.WebApp/src/i18n/locales/en.json` e componenti direttamente necessari.
- Requisiti: R-01, R-03.
- Acceptance: riga ordinata, fino a 12, con nome e foto o segnaposto, link IMDb solo quando disponibile; fallback ai nomi storici durante backfill senza duplicati; vuoto elaborato senza falsi attori; immagine/link accessibili e layout utilizzabile anche mobile; apertura non bloccata e nessun polling aggiunto.
- Verifica: Vitest con cast completo/parziale, fallback storico, vuoto, ordine, link e richieste; typecheck/lint e verifica visuale desktop/mobile quando disponibile.
- Contesto: V-03/V-06, FLUSSO-02.
- Assunzioni: nessuna.
- Rollback: rimuovere solo la UI e i tipi aggiunti, conservando il campo storico.

## TR-08 — Documentazione e integrazione

### T08 — Documentazione e regressioni integrate

- Scope: `docs/user/features.md`, `docs/developer/architecture.md`, `docs/functional-spec.md`, `docs/technical-spec.md`, `docs/recommendations.md` solo se necessario, test mirati infrastruttura/API/frontend e reco interessati.
- Requisiti: R-01–R-06.
- Acceptance: guide e specifiche inglesi descrivono comportamento, job, rinnovo, fallback e struttura dati senza promettere funzionalità inesistenti; regressioni reco dimostrano che aggiungere fino a 12 dati dettaglio non altera gli input/previsioni pertinenti; flusso job → DB → dettaglio → UI e comportamento Arr verificati; preservate modifiche preesistenti.
- Verifica: test backend mirati a enrichment, servizio batch, job, query, reco e API; Vitest modale, typecheck, lint e build frontend; documentare ambiente, comandi, esiti e controlli non eseguibili.
- Contesto: F-001/F-003, V-04/V-07; snapshot S-01.
- Assunzioni: verificare i test e lo stato effettivo del worktree prima di intervenire.
- Rollback: annullare solo modifiche proprie dopo riconciliazione; nessun rollback distruttivo DB.

## TR-09 — Verifica finale indipendente

### T09 — Verifica finale indipendente

- Scope: requisiti approvati, implementazione, test, configurazione e documentazione dell'intero dominio, inclusi integrazione tra tranche e regressioni.
- Requisiti: ANALYSIS.md §1 e aggiornamenti approvati in §7.
- Acceptance: tutti i criteri obbligatori verificati sulla versione identificata, nessun difetto bloccante, rinvii approvati espliciti, rapporto PASS in ARCHIVE.md e riferimento in STATUS.md §9.
- Verifica: protocollo finale della skill; nuovo contesto separato con richiesta originale, modifiche approvate, vincoli e snapshot SHA/worktree, senza correzioni del revisore; confrontare evidenze precedenti solo dopo esame indipendente. Se indisponibile, handoff a nuova sessione e task BLOCKED.
- Contesto: tutti i flussi e findings; dichiarare istruzioni/materiali ereditati e limiti dell'isolamento.
- Assunzioni: nessuna.
- Rollback: non applicabile; in caso FAIL preservare il rapporto e pianificare ciclo correttivo con nuovi ID.

## TR-C1 — Correzioni post-T09

### T10 — Hardening batch cast

- Scope: `DetailCastService`, `TmdbMetadataProvider`, entità `Title`, migrazione EF, test `DetailCastServiceTests`.
- Finding corretti: retry differito, separazione 404/cast vuoto, isolamento modifiche DbContext, rinnovo IMDb a 30 giorni.
- Acceptance:
  - un 404 titolo registra tentativo ma preserva cast, stamp e non lo converte in cast vuoto;
  - un errore temporaneo registra tentativo e non salva modifiche parziali;
  - i retry recenti non bloccano altri titoli eleggibili;
  - i dati IMDb assenti o scaduti vengono rinnovati ogni 30 giorni;
  - cast vuoto valido resta distinto da titolo non trovato;
  - migrazione additiva sincronizzata con lo snapshot EF;
  - crediti reco rimangono separati e invariati.
- Verifica: test mirati service/migration/job, suite Infrastructure, suite API, reco mirato, EF pending-model check.
- Rollback: migrazione successiva additiva se necessario; nessuna cancellazione DB.

## TR-C2 — Correzioni post-T09-R1

### T11 — Fairness retry e 404 serie

- Scope: `DetailCastService`, `SeriesGuideService`, test service/series.
- Finding corretti:
  - starvation dei retry cast;
  - 404 TMDB propagato dalla lettura stagioni.
- Acceptance:
  - retry ordina prima i tentativi mai eseguiti, poi il tentativo meno recente;
  - il gate temporaneo non impedisce al backfill di raggiungere altri titoli;
  - serie assente su TMDB restituisce stagioni vuote senza errore server;
  - lettura episodi assenti restituisce lista vuota;
  - reco e index Git preesistenti non vengono modificati.
- Verifica: test mirati cast/series/enrichment/reco, integration, Core, frontend, EF model check.

## TR-C3 — Priorità retry post-T09-R2

### T12 — Fairness selezione batch

- Scope: `DetailCastService`, `DetailCastServiceTests`.
- Finding corretto: titoli mai tentati o retry più vecchi potevano essere superati da titoli con cast refresh datato.
- Acceptance:
  - titoli senza `CastFetchAttemptAt` ordinati prima dei retry;
  - retry ordinati per `CastFetchAttemptAt` crescente;
  - nessun titolo eleggibile è escluso indefinitamente dal limite batch;
  - test distinguono mai tentato da retry vecchio/nuovo.
- Verifica: test mirati DetailCast/SeriesGuide/Enrichment/Job e regressioni backend.
