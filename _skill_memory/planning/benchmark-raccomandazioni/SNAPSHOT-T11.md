# Snapshot T11 — 2026-11-24

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto al completamento di T11.
- Manifest ereditato: `SNAPSHOT-T10.md`; override D-20/D-21 restano invariati.
- Input T04 invariato: SHA-256 `B748506542CE9664951B0D61601FEAA0D55B19B52133BD69CF7B2E5997EDBB43`, ReadOnly.
- Riesecuzione T11: seed 42, osservato T04 `2026-11-23T11:42:42Z`, 33.894 pool titoli, 369 eventi, 318 rating, 2 ripetizioni.
- Report privato: `T11-model-comparison-post-t09-t10-20261124.json`, SHA-256 `8E8ABB2DE8BD2713FE7724E85FE46318B28FA0C266F9D0E3BCD16F88539550E`.
- Risultato: metrica temporale identica a T08 per entrambi i modelli: embedding Recall@10 test/validation 0, combined Recall@10 test 1/69. Nessun miglioramento o regressione dichiarato e nessuna promozione.
- Recall Simili: oracle brute force oltre il vecchio limite 120 e test API verificati.
- Nessuna migrazione EF, reset/sostituzione DB, provider, job live, cluster/pod o modifica snapshot.
- F-001/F-007 restano OPEN; F-002/F-003 RESOLVED; T12 successiva.

## Evidenze

| Evidenza | Esito |
| --- | --- |
| Core KNN/sparse/oracle tests | 9 PASS |
| Integration Similar test | 1 PASS |
| `git diff --check` | PASS; soli avvisi CRLF preesistenti |
| Report T11 | completed_not_certified_sufficient |

## Performance locali

| Modello | Titoli/s campione | p50/p95 ms | Ranking p50/p95 ms | Picco working set |
| --- | ---: | ---: | ---: | ---: |
| all-MiniLM-L6-v2@q8 | 84.81 | 10.218/23.211 | 4983/5816 | 624 MiB |
| multilingual MiniLM-L12-v2 | 28.14 | 32.920/66.938 | 4480/4736 | 965.64 MiB |
