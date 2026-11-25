# AGENT KB — cast-dettaglio

Personalizzazioni comuni: `_skill_memory/planning/AGENT_KB.md`; workflow nella skill `incremental-dev-planning`.

## 1. Ordine lettura

Comune → questo addendum → STATUS.md §0 e righe §1 pertinenti → PLAN.md tranche selezionata → record per ID quando necessari.

## 2. Focus operativo

- Backfill e rinnovo progressivi senza richieste di rete all'apertura della modale.
- Mantenere invariati reco, dati locali e operazioni Arr.
- Riconciliare il worktree preesistente prima di toccare file condivisi.

## 3. Vincoli obbligatori

| ID | Vincolo | Fonte |
| --- | --- | --- |
| V-01 | Non cancellare, ricreare o sostituire database; migrazioni additive con snapshot EF generato. | Istruzioni repository; ANALYSIS.md R-06 |
| V-02 | Job distinto, orario e manuale, batch limitati, backfill e rinnovo dopo 30 giorni; errori osservabili e retry senza starvation. | ANALYSIS.md R-02, STATUS.md D-02 |
| V-03 | Fino a 12 attori ordinati, nome sempre disponibile, segnaposto se manca foto, link solo se IMDb noto; nomi esistenti durante backfill, nessun polling. | ANALYSIS.md R-01/R-03, STATUS.md D-01 |
| V-04 | Raccomandazioni e segnali dei primi cinque crediti invariati; non modificare `TitleCredits` per aggiungere altri attori della modale. | ANALYSIS.md R-05, STATUS.md D-02/D-03 |
| V-05 | Library sotto le azioni personali conservando stato, refresh, add/delete, stagioni, accessibilità e assenza per Arr non configurato. | ANALYSIS.md R-04, STATUS.md D-02 |
| V-06 | Mantenere il campo API cast a nomi per compatibilità, aggiungendo dati strutturati; nessuna rottura di consumer esistenti. | STATUS.md D-03 |
| V-07 | Aggiornare guide e specifiche pertinenti in inglese; KB in italiano. | `docs/AGENTS.md`; ANALYSIS.md R-06 |
| V-08 | Nessun task prima di approvazione piano; contesto separato per ogni tranche e verifica finale indipendente. | Skill |
| V-09 | Su Windows mantenere coerenti entrambe le varianti Git case-colliding interessate e verificare il diff, senza modificare index o lavoro altrui. | STATUS.md D-04/F-004 |
