# cast-dettaglio STATUS

> Aggiornato: 2026-11-25; branch `main`. Storico: `ARCHIVE.md`.

## 0. Quick Context

- Obiettivo: cast con foto/IMDb e Library compatta; requisiti `ANALYSIS.md` §1.
- Dominio: attivo; piano approvato dall'utente il 2026-11-24.
- Codebase verificata: HEAD `6f5887115969af8fdab15ef43347c5507139d9dd` più fix T09 e aggiornamenti di questa KB nel worktree; nessun'altra modifica estranea rilevata.
- Candidato: nessuno; T01–T12 e T09 completati.
- Findings critici: F-001 (§3).
- Input umano: scelta per rendere persistenti entrambe le varianti dei file nuovi senza coinvolgere modifiche altrui.
- Handoff: T09 PASS; rapporto finale in `ARCHIVE.md` §T09-R4.
- Verifica finale: §9.

## 1. Roadmap

| Tranche | ID | Task | Size | Rischio | Stato | Dipende da | Dettagli |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TR-01 | T01 | Persistenza cast dettaglio | M | alto | DONE | — | PLAN.md TR-01 |
| TR-02 | T02 | Contratti e provider TMDB | M | medio | DONE | — | PLAN.md TR-02 |
| TR-03 | T03 | Backfill e rinnovo batch | L | alto | DONE | T01, T02 | PLAN.md TR-03 |
| TR-04 | T04 | Job e amministrazione | M | medio | DONE | T03 | PLAN.md TR-04 |
| TR-05 | T05 | Contratto dettaglio e fallback | M | medio | DONE | T01 | PLAN.md TR-05 |
| TR-06 | T06 | Library compatta | M | medio | DONE | — | PLAN.md TR-06 |
| TR-07 | T07 | Riga attori in modale | M | medio | DONE | T05 | PLAN.md TR-07 |
| TR-08 | T08 | Documentazione e regressioni integrate | M | medio | DONE | T03, T04, T05, T06, T07 | PLAN.md TR-08 |
| TR-09 | T09 | Verifica finale indipendente | L | alto | DONE | TR-01, TR-02, TR-03, TR-04, TR-05, TR-06, TR-07, TR-08 | PLAN.md TR-09; §9 |
| TR-C1 | T10 | Hardening batch cast post-T09 | M | alto | DONE | T09 | PLAN.md TR-C1 |
| TR-C2 | T11 | Fairness retry e 404 serie post-T09-R1 | M | alto | DONE | T09-R1 | PLAN.md TR-C2 |
| TR-C3 | T12 | Priorità retry mai tentati post-T09-R2 | M | alto | DONE | T09-R2 | PLAN.md TR-C3 |

## 2. Flussi

| ID | Stato | Nota |
| --- | --- | --- |
| FLUSSO-01 | ✅ DONE | Acquisizione asincrona, retry e rinnovo cast verificati |
| FLUSSO-02 | ✅ DONE | Dettaglio cast e Library verificati |
| FLUSSO-03 | ✅ DONE | Crediti reco separati dal cast di dettaglio; comportamento reco preesistente non alterato dal flusso cast |

## 3. Findings

| ID | Gravità | Descrizione | Task target | Stato |
| --- | --- | --- | --- | --- |
| F-001 | Alta | I crediti reco esistenti non possono essere estesi ingenuamente a 12 senza cambiare ranking. | T01, T03, T08 | RESOLVED |
| F-002 | Media | Backfill necessario per titoli e cast già persistiti; evitare selezione infinita per cast vuoti o dati assenti. | T03 | RESOLVED |
| F-003 | Media | Modifiche preesistenti nel worktree, incluse reco e docs; evitare sovrascritture. | T08 | RESOLVED |
| F-004 | Alta | Su Windows 470 coppie di percorsi Git condividono il file fisico; i file nuovi T01 risultano non tracciati solo sotto `Pickarr`, non sotto `PickArr`. La coerenza è stata verificata sul worktree senza intervento su index o lavoro altrui. | T01 | RESOLVED |
| F-005 | Alta | Il batch cast poteva trattare 404 come cast vuoto, riordinare continuamente errori persistenti e contaminare il DbContext. | T10 | RESOLVED |
| F-006 | Media | Gli ID IMDb non avevano rinnovo dopo il primo fetch. | T10 | RESOLVED |
| F-007 | Alta | I retry cast potevano mantenere priorità e impedire progressione ad altri titoli. | T11 | RESOLVED |
| F-008 | Media | Il 404 TMDB delle serie propagate da `SeriesGuideService` produceva errore server. | T11 | RESOLVED |
| F-009 | Alta | Modifiche reco preesistenti e varianti Git case-colliding non sono riconciliate nella stessa tranche. | T08/T11 | RESOLVED |
| F-010 | Alta | Il controllo retry precedente privilegia fetch vecchi prima dei titoli mai tentati. | T12 | RESOLVED |

## 4. Decisioni architetturali

| ID | Data | Decisione | Owner | Motivazione / fonte |
| --- | --- | --- | --- | --- |
| D-01 | 2026-11-24 | Fino a 12 attori, segnaposto senza foto, link solo con IMDb; mantenere nomi storici in attesa, nessun polling. | Utente | Risposte al gate; R-01/R-03. |
| D-02 | 2026-11-24 | Job orario e manuale, batch limitati e rinnovo a 30 giorni; Library compatta con funzioni conservate; reco invariata sui primi cinque. | Utente | Risposte al gate; R-02/R-04/R-05. |
| D-03 | 2026-11-24 | Separare il modello cast di dettaglio dai crediti reco e mantenere il campo API nomi. | Agente pianificatore | Preserva i consumer e R-05; approvazione piano utente 2026-11-24. |
| D-04 | 2026-11-24 | Procedere su Windows mantenendo coerenti entrambe le varianti tracciate coinvolte e verificando il diff. | Utente | Risposta esplicita a F-004; nessun permesso di modificare index o lavoro altrui. |

## 5. Activity log

| Data | Task | Azione | Esito | Evidenza |
| --- | --- | --- | --- | --- |
| 2026-11-24 | INIT | Analisi, gate e creazione piano | In attesa approvazione | ANALYSIS.md §1, §8 S-01 |
| 2026-11-24 | T01 | Piano approvato; avvio prima tranche | IN_PROGRESS | Conferma utente 2026-11-24 |
| 2026-11-24 | T01 | Rilevate collisioni case-insensitive; sospensione prima di modificare codice o database | BLOCKED | F-004; `git ls-tree -r HEAD src tests`: 470 gruppi, 0 blob divergenti |
| 2026-11-24 | T01 | Accolta scelta utente per F-004; ripresa | IN_PROGRESS | D-04; transizioni F-004 OPEN → IN_PROGRESS e T01 BLOCKED → TODO → IN_PROGRESS |
| 2026-11-24 | T01 | Migrazione additiva e test SQLite parziali; impossibile garantire entrambe le varianti dei file nuovi nel solo worktree Windows | BLOCKED | Build API PASS; test mirati 4/4 PASS; `dotnet ef migrations has-pending-model-changes` nessuna differenza; F-004 IN_PROGRESS → OPEN, snapshot S-02 |
| 2026-11-24 | T01 | Completata persistenza cast separata: entita condivise, relazione ordinata, stato `CastFetchedAt`, migrazione additiva e test identita | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~DetailCastMigrationTests`: 2/2 PASS; EF CLI 10.0.14: nessun pending-model change; `Pickarr__DataPath=/mnt/c/projects/Pickarr/.data` |
| 2026-11-24 | T02 | Contratti TMDB estesi con cast dettaglio separato e IMDb persona; fake test riconciliato | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~EnrichmentServiceTests"`: 13/13 PASS; build Core e Infrastructure PASS; crediti reco rimasti primi cinque |
| 2026-11-24 | T03 | Servizio batch cast aggiunto: backfill, limite 12, rinnovo 30 giorni, cast vuoto e retry errore temporaneo | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~DetailCastServiceTests"`: 4/4 PASS; build Core e Infrastructure PASS; crediti reco invariati nei test |
| 2026-11-24 | T04 | Job cast dettaglio distinto aggiunto: ricorrenza oraria, trigger manuale admin, limite 20 batch, concorrenza protetta ed esito parziale/errori nel registro | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests --filter "FullyQualifiedName~DetailCastJobTests\|FullyQualifiedName~ScheduleReconcilerTests\|FullyQualifiedName~AdminJobServiceTests\|FullyQualifiedName~JobStateTests"`: 11/11 PASS; build Infrastructure e API PASS |
| 2026-11-24 | T05 | Contratto dettaglio esteso con `castMembers`, fallback storico prima del primo fetch e cast vuoto persistito senza riesumare nomi obsoleti | DONE | `dotnet test tests/Pickarr.Infrastructure.Tests --filter FullyQualifiedName~TitleQueryServiceTests`: 14/14 PASS; `dotnet test tests/Pickarr.Integration.Tests --filter FullyQualifiedName~CatalogApiTests`: 3/3 PASS; build API PASS |
| 2026-11-25 | T06 | Library riposizionata sotto le azioni personali mantenendo refresh, stato, add/delete, stagioni, accessibilità e occultamento senza Arr | DONE | `npm test -- --run src/features/title/TitleDialog.rating.test.tsx`: 4/4 PASS; `npm run typecheck`: PASS; `npm run lint`: PASS con 11 warning preesistenti; `npm run build`: PASS |
| 2026-11-25 | T07 | Riga cast strutturata nella modale con ordine, foto o segnaposto, deduplica e link IMDb condizionali; fallback al campo storico per compatibilità | DONE | `npm test -- --run src/features/title/TitleDialog.rating.test.tsx`: 5/5 PASS; `npm run typecheck`: PASS; `npm run lint`: PASS con 11 warning preesistenti; `npm run build`: PASS |
| 2026-11-25 | T08 | Documentazione aggiornata e regressioni integrate backend/frontend eseguite; fake `ISyncRunStore` allineato al contratto | DONE | Core reco 7/7 PASS; Infrastructure 61/61 PASS; Integration 3/3 PASS; frontend 122/122 PASS; typecheck/build PASS; lint 0 errori e 11 warning preesistenti |
| 2026-11-25 | T10 | Completato ciclo correttivo post-T09 con retry persistente, separazione 404/cast vuoto, isolamento DbContext e rinnovo IMDb 30 giorni | DONE | Migrazione EF generata e snapshot sincronizzato; 52 test cast/enrichment/reco PASS; Core 132/132, Integration 73/73, frontend 5/5 + typecheck PASS; smoke prestazione rerun 2/2 PASS |
| 2026-11-25 | T09-R1 | Seconda verifica indipendente eseguita dopo T10 | FAIL | Revisore separato; finding su starvation retry, 404 stagioni, reco worktree e riconciliazione Git/EF |
| 2026-11-25 | T11 | Corrette fairness dei retry cast e gestione 404 stagioni/episodi | DONE | 28 test mirati PASS; 72 test backend regressivi PASS; Integration 3/3, Core 132/132, frontend 5/5 + typecheck PASS; EF sincronizzato |
| 2026-11-25 | T12 | Corretta priorità dei retry: mai tentati prima, poi tentativi meno recenti | DONE | 29 test mirati PASS; 73 backend regressivi, 3 Integration, 132 Core, 5 frontend PASS; EF sincronizzato |
| 2026-11-25 | T09-R3 | Quarta verifica indipendente sulla versione T12 | FAIL | Revisore separato; correzione cast verificata; reco e riconciliazione Git/EF restano bloccanti |
| 2026-11-25 | T09-R4 | Deduplicati gli ID persona nel cast TMDB prima del limite 12; regressione aggiunta e verifica finale indipendente | PASS | `ARCHIVE.md` §T09-R4; hash diff `4542007fea010c21508477c60dfa1f96431740d960792f4c69b6bdce6c541127` |

## 7. Checklist archiviazione

| Condizione | Verificata |
| --- | --- |
| Più di 10 righe DONE nel log | ☐ |
| Bootstrap/pacchetto oltre budget orientativo | ☐ |
| Almeno 3 finding RESOLVED/ACCEPTED | ☐ |
| Decisione nuova rende obsolete note precedenti | ☐ |

## 9. Verifica finale indipendente

| Campo | Valore |
| --- | --- |
| Task finale | T09 |
| Esito | PASS |
| Revisore, sessione, isolamento | `cast-t09-review`, contesto indipendente read-only; verifica della correzione richiesta separatamente |
| Versione verificata | HEAD `6f5887115969af8fdab15ef43347c5507139d9dd` più worktree; hash diff `4542007fea010c21508477c60dfa1f96431740d960792f4c69b6bdce6c541127` |
| Rapporto | `ARCHIVE.md` §T09-R4 |
| Tentativo precedente / ciclo correttivo | T09-R3 FAIL; fix deduplica cast applicata e ripetuta verifica indipendente |
| Tentativo successivo | Nessuno |
| Esito T09-R1 | FAIL |
| Revisore T09-R1 | `code-review/t09-repeat-independent-review`; verifiche read-only |
| Finding T09-R1 | Retry può ancora soffrire starvation; 404 TV non gestito da SeriesGuide; reco worktree non riconciliata; index/collisione e migrazione non tracciata |
| Evidenze eseguite | Infrastructure mirata 53/53; Core 132/132; integration/frontend/EF non ripetuti dal revisore |
| Rinvii / rischi approvati | Nessuno |
| Esito T09-R3 | FALLBACK FAIL |
| Revisore T09-R3 | `code-review/t09-fourth-independent-review`; verifica read-only |
| Finding T09-R3 | R-05 reco worktree non riconciliata; migrazione/varianti Git non riconciliate |
| Evidenze T09-R3 | Infrastructure 272/272; Core 132/132; Integration 73/73; frontend 122/122 + typecheck |
| Esito T09-R4 | PASS |
| Finding corretto | Il mapping dettaglio TMDB conserva crediti persona duplicati; deduplica stabile per ID prima di `Take(12)` |
| Verdetto indipendente | Fix e test confermati; nessun altro blocker R-01–R-06 individuato |
| R-05 e repository | Pipeline cast usa persistenza separata e non modifica `TitleCredits`; 0 path case-colliding e migrazioni cast tracciate |
| Evidenze aggiornate | Core 132/132; Infrastructure 272/272; Integration 73/73; frontend 122/122; typecheck/build PASS; lint 0 errori e 11 warning; EF nessun pending model change |
| Limiti | Revisore indipendente non ha rieseguito test; verifica visuale desktop/mobile non eseguita. Nessuna modifica al database SQLite. |
