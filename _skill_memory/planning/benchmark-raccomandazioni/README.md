# Knowledge base — benchmark-raccomandazioni

Workflow canonico: skill `incremental-dev-planning`; questa guida è adattata dal relativo template.

## Documenti

| File | Ruolo |
| --- | --- |
| `AGENT_KB.md` | Vincoli e focus del dominio |
| `ANALYSIS.md` | Requisiti, flussi, fatti e ipotesi; §7 append-only |
| `STATUS.md` | Stato, dipendenze, decisioni e log |
| `PLAN.md` | Scope e acceptance canonici per tranche |
| `BASELINE.md` | Snapshot iniziale immutabile |
| `ARCHIVE.md` | Storico e rapporti di verifica indipendente |

## Ripresa

Leggere KB comune, addendum e `STATUS.md` §0; riconciliare stato e freshness, quindi consultare `PLAN.md` solo per la tranche selezionata. Eseguire una tranche per contesto separato e registrare evidenze prima dell'handoff. Verificare l'approvazione del piano in `STATUS.md` e seguire i vincoli di protezione dati anche sui benchmark.

## Chiusura

La verifica finale usa un revisore in contesto separato, su versione identificata, e richiede PASS indipendente. In caso FAIL conservare il rapporto e pianificare correzioni e nuova verifica; INCONCLUSIVE blocca la chiusura. Rinvii richiedono approvazione esplicita.
