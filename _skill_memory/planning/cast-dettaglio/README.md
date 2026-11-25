# Knowledge Base — cast-dettaglio

Questa KB supporta il lavoro incrementale; il workflow canonico è nella skill `incremental-dev-planning`.

| Documento | Ruolo |
| --- | --- |
| `AGENT_KB.md` | Vincoli e focus del dominio |
| `ANALYSIS.md` | Requisiti canonici, analisi e snapshot |
| `STATUS.md` | Stato, dipendenze, log e verifica finale |
| `PLAN.md` | Scope, acceptance e verifiche per task |
| `ARCHIVE.md` | Storico e rapporti indipendenti |

Per riprendere: leggere il file comune `_skill_memory/planning/AGENT_KB.md`, poi addendum, STATUS §0 e sole righe §1 pertinenti, quindi la tranche di PLAN e i record necessari per ID. Confrontare HEAD e snapshot locale prima di lavorare.

Il piano richiede approvazione prima dell'esecuzione. Ogni tranche richiede un contesto separato dalla precedente; la verifica finale richiede un revisore indipendente in contesto separato e PASS sulla versione corrente. In assenza di isolamento effettivo serve una nuova sessione. Log e decisioni sono append-only; non eliminare storico o dati SQLite.
