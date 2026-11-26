# Knowledge Base — voti-imdb

Guida rapida alla KB in `_skill_memory/planning/voti-imdb/`. Il workflow completo è nella
skill `incremental-dev-planning`; questo README indica dove trovare le informazioni.

Dominio: import settimanale dei rating IMDb, voto calcolato e uso di quel voto in catalogo e raccomandazioni.

## Mappa dei documenti

| File | Fonte canonica per |
| --- | --- |
| `AGENT_KB.md` (radice e dominio) | Personalizzazioni e vincoli obbligatori |
| `STATUS.md` | Stato, dipendenze, findings, decisioni e log |
| `PLAN.md` | Scope, requisiti e acceptance dei task, raggruppati per tranche |
| `ANALYSIS.md` | Requisiti e conoscenza tecnica; nuove scoperte in §7 |
| `ARCHIVE.md` | Storico ed evidenze concluse |

Non c'è `BASELINE.md`: in pianificazione non ci sono KPI misurati sul database.

## Ripresa del lavoro

Leggere gli addendum `AGENT_KB.md`, poi `STATUS.md` §0 e le sole righe di §1 pertinenti.
Aprire in `PLAN.md` la tranche selezionata e recuperare requisiti, findings, decisioni ed
evidenze tramite ID. Non caricare tutta la KB né duplicare le fonti canoniche.

Per ogni task: eseguire nello scope approvato, verificare i criteri e registrare stato ed
evidenze in `STATUS.md`. Solo quando tutti i task della tranche sono conclusi, controllare
i trigger di archiviazione, aggiornare la vista §0 e fare handoff a un contesto separato
per la tranche successiva.

La tranche finale è una verifica indipendente sulla versione corrente. Dopo PASS valido,
chiudere il dominio; dopo FAIL preservare il rapporto e pianificare remediation e nuova
verifica. Senza PASS indipendente non chiudere.

## Creazione o integrazione

Scope e acceptance di questo dominio sono già nel `PLAN.md`. L'esecuzione parte solo dopo
la conferma del piano. Non estendere questo dominio da un'altra richiesta: una richiesta
nuova crea un dominio nuovo, salvo richiesta esplicita di integrazione.
