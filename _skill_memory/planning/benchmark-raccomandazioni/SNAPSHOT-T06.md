# Snapshot T06 — 2026-11-23

- HEAD iniziale/finale `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice vuoto. T06 IN_PROGRESS sospeso, nessun codice/test modificato, nessuna misura eseguita.
- Manifest ereditato `SNAPSHOT-T05.md`: SHA-256 `8318CB2521E2A6A9DDFEFC68BC4DAF448B02FF19FFB69CA29B0088C8A67F0640`; 38/38 hash verificati prima dell'avvio. I 35 file ereditati diversi dai tre override sotto mantengono gli hash specificati in tale manifest. F-007 invariato; nessun rename/cleanup.
- Override SHA-256 byte filesystem Windows:

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `3D8CC1B39977DCA268ED7124F083A91BA9BFE8744295A69DBAB57BF4651F5C68` |
| `_skill_memory\planning\benchmark-raccomandazioni\PLAN.md` | `58B2ACDF8A661E9AF775505E0E4BC6EC453096D63495405210012A2BC1988303` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `6B6477AA41934C5BB8522F4AD12B593FD58DCFCA5B518DEE43EC07ACFCA7076E` |

- Questo manifest non include il proprio hash. `git diff --check` PASS con avvisi CRLF ereditati. Checklist archiviazione STATUS §7 verificata, nessuna nuova chiusura da archiviare; F-009 OPEN. Scomposizione proposta in PLAN TR-06; conferma pendente.
