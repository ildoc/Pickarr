# AGENT KB — VOTI IMDB (ADDENDUM)

Addendum dominio `voti-imdb`.
Personalizzazioni comuni in: `_skill_memory/planning/AGENT_KB.md`.
Workflow canonico nella skill `incremental-dev-planning`, non duplicato qui.

Il file comune CONV-04 indica percorsi `backend/` e `frontend/` che in questo repository non esistono. Per questo dominio i comandi sono quelli del §5 e di `AGENTS.md` alla radice. Non modificare il file comune.

---

## 1. File locali — ordine di lettura

1. Questo file (`AGENT_KB.md`)
2. `STATUS.md` §0 — Quick Context
3. `STATUS.md` §1 — sole righe necessarie a riconciliare/selezionare task e dipendenze
4. `PLAN.md` — solo tranche corrente; altri record recuperati per ID su richiesta

---

## 2. Focus operativo

1. Il voto mostrato, l'ordinamento, il filtro e il prior di For you leggono `Title.VoteBlended`. `VoteTmdb` e `VoteImdb` restano i grezzi delle due fonti.
2. La formula è pura, in Core, con `m = 500` e `C` passato dal chiamante. `C` è la media del dump IMDb pesata per i voti, salvata sullo snapshot.
3. Un download o un parse fallito lascia intatti snapshot, tabella `tconst` e colonne dei titoli.
4. La migrazione è additiva. Il database di sviluppo in `.data` non si cancella, non si sostituisce e non si punta a un file vuoto.

---

## 3. Vincoli tecnici non negoziabili

- V-01. Formula e parametri approvati il 2026-11-26: shrink per fonte `(v/(v+m))·R + (m/(v+m))·C`, poi media pesata per `w = v/(v+m)`. `m = 500`. `C` unica, media del dump IMDb pesata per i voti, ricalcolata a ogni import riuscito. Stessa `C` per IMDb e TMDB. Costanti di codice, non chiavi di impostazione. Fonte: ANALYSIS.md §1 R-03, STATUS.md §4 D-002.
- V-02. Con una sola fonte il voto salvato e mostrato è il grezzo, senza shrink e senza segnaletica «pochi voti». Una fonte assente non vale zero. Fonte: D-003.
- V-03. Una fonte entra nel peso solo con voto non nullo e conteggio `> 0`. Se resta un solo voto grezzo, anche con conteggio nullo o zero, `VoteBlended` è quel grezzo. Due voti grezzi e nessun conteggio positivo producono `VoteBlended` nullo. Fonte: D-009.
- V-04. Il prior di livello 0 usa `VoteBlended` come componente voto, senza il secondo shrink `m = 300`. Senza `VoteBlended` il componente vale la media del pool. Il mix con la popolarità resta 0,7 / 0,3. Fonte: D-005.
- V-05. La feature di livello 3 sul voto è `VoteBlended / 10` (assente → 0,5). In coda al vettore si aggiunge `log10(1+v) / 6` sui voti utilizzabili (assente o zero → 0), con tetto 1. `v` è la somma dei conteggi delle fonti che entrano nel peso. Fonte: D-006, D-011.
- V-06. Il modello di livello 3 non è persistito: ogni `RecoBuild` addestra e inferisce sugli stessi valori correnti. Dopo un import, accodare un `RecoBuild` se non esiste una `C` precedente oppure `|C_nuova − C_precedente| ≥ 0,05`. Fonte: D-006.
- V-07. Job `imdb-ratings`, cron `0 4 * * 0`, fuso `TimeZoneInfo.Local`. Trigger manuale da Impostazioni → Job. Fonte: D-008.
- V-08. L'arricchimento, quando conosce `ImdbId`, rilegge la tabella locale. Non scarica di nuovo il dataset. Fonte: D-007.
- V-09. Migrazione solo additiva, con backfill `VoteBlended = VoteTmdb` dove `VoteTmdb` è valorizzato. Vietato cancellare o ricreare il database. Fonte: D-010 e `AGENTS.md` radice.
- V-10. Core senza tipi di Infrastructure. Test del bugfix nello stesso cambio. Docs di prodotto in inglese, nello stesso cambio della superficie che cambia. Fonte: `AGENTS.md` radice e `docs/AGENTS.md`.
- V-11. Il prior di household, gli embedding, il profilo sparso e l'esplorazione restano invariati. Fonte: D-013.

---

## 4. Definition of Done locale

Oltre ai criteri comuni, una tranche `voti-imdb` è chiusa solo se:

- ogni acceptance del task è collegata a un test eseguito o a una lettura del file di migrazione, con comando ed esito in STATUS.md §5
- la migrazione generata, se presente, contiene solo aggiunte di colonne o tabelle e il backfill previsto
- i test toccati del prior e del livello 3 passano insieme al filtro del task

---

## 5. Tooling dominio-specifico

- `dotnet test tests/Pickarr.Core.Tests --filter <Nome>`
- `dotnet test tests/Pickarr.Infrastructure.Tests --filter <Nome>`
- `dotnet test tests/Pickarr.Integration.Tests --filter <Nome>`
- `dotnet ef migrations add <Nome> --project src/Pickarr.Infrastructure --startup-project src/Pickarr.Api --output-dir Persistence/Migrations`
- WebApp, dalla cartella `src/Pickarr.WebApp`: `npm test -- --run`, `npm run typecheck`, `npm run lint`
- Prima di `dotnet build`, `dotnet test` su un progetto che referenzia l'API, o `dotnet ef`: fermare `Pickarr.Api` se tiene i DLL in `src/Pickarr.Api/bin`. Il database resta `.data`.
