# AGENT KB — benchmark-raccomandazioni

Personalizzazioni comuni: `_skill_memory/planning/AGENT_KB.md`. Workflow: skill `incremental-dev-planning`.

## 1. Ordine di lettura

1. KB comune e questo addendum.
2. `STATUS.md` §0 e righe §1 pertinenti.
3. `PLAN.md` sola tranche selezionata; altri record per ID.

## 2. Focus operativo

- Correggere la valutazione prima di trarre conclusioni sui modelli.
- Benchmark e snapshot isolati; separare qualità, recall e prestazioni.
- Una tranche per contesto separato; mai sostituire automaticamente il modello di produzione.

## 3. Vincoli obbligatori

- V-01: mai eliminare, resettare o sostituire il database dev/prod; preservare dati di catalogo, rating, preferenze e quota. Migrazioni solo data-preserving con test. Fonte: richiesta utente; root `AGENTS.md`; ANALYSIS R-01.
- V-02: prima di qualsiasi accesso o operazione su cluster/pod chiedere una nuova conferma operativa con procedura e impatto; copiare tramite backup SQLite coerente con WAL su destinazione nuova protetta. Se impossibile, BLOCKED. Fonte: gate D-04; PLAN T04.
- V-03: niente benchmark sul pod/database live; misure cluster solo ambiente isolato con conferma separata; evitare chiamate a Watchmode/TMDB. Fonte: D-04/D-05; R-01/R-07.
- V-04: snapshot e output fuori repository e separati, input sola lettura, nessuna divulgazione di segreti/dati personali o invio a terzi; non eseguire job/app/migrazioni sulla copia di input. Fonte: R-01–03; report §6; root `AGENTS.md`.
- V-05: Core senza EF/HTTP/I/O; conservare membership, filtri, profili e API salvo correzioni approvate. Nessuna espansione candidati fuori pool né cambio `RemovedAt` implicito. Fonte: root `AGENTS.md`; R-05/R-06/R-08.
- V-06: GitHub Actions `.github/workflows/ci.yml`, progetti `src/` e `tests/` prevalgono qui sulle convenzioni comuni obsolete CONV-02/04, senza modificare la KB comune. Fonte: gate D-03; `docs/developer/README.md`.
- V-07: test standard sintetici senza dati privati e senza download esterno obbligatorio; nessun commit/push senza richiesta. Fonte: root `AGENTS.md`; R-02/R-03.
- V-08: nessuna affermazione di miglioramento non misurata; documentare campioni piccoli, limiti storici, ambiente e varianti. Fonte: R-04/R-07.

## 4. Definition of Done locale

Per task che toccano dati o benchmark: evidenza di isolamento input/output, assenza di modifiche all'origine e test con dati sintetici oltre alla verifica funzionale. Se una prova obbligatoria manca, task BLOCKED.
