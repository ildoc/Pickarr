# Snapshot T10 — 2026-11-24

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto al completamento di T10.
- Manifest ereditato: `SNAPSHOT-T09.md`; override D-20/D-21 e limiti cluster restano invariati.
- T10 DONE: `RecoQueryService` confronta il query embedding esattamente contro tutti i vettori del pool enabled.
- Rimossa la preselezione dei candidati a 120; i candidati senza embedding utilizzano fallback sparse.
- Merge deterministico: niente duplicati, esclusioni preservate, ID ordinati come tie-break, TMDB merge conservato.
- Nessuna migrazione EF, cancellazione/reset/sostituzione database, chiamata provider, job live, accesso pod/cluster o snapshot privato.
- F-003 RESOLVED; F-001/F-007 restano OPEN. T11 successiva.

## Evidenze

| Evidenza | Esito |
| --- | --- |
| Core KNN/sparse tests | 8 PASS |
| Integration similar API test | 1 PASS |
| Infrastructure build | 0 warning, 0 errori |
| `git diff --check` | PASS; soli avvisi CRLF preesistenti |

## File modificati da T10

| File | SHA-256 |
| --- | --- |
| `src\Pickarr.Core\Recommendations\RecoKnn.cs` | `FAF716F121D480730275C08B57254E2EFD05F32D01986BE823845142BB7FDE3D` |
| `src\Pickarr.Infrastructure\Catalog\RecoQueryService.cs` | `4CD704173D0C6483D119622C1C7448777757503F6D205B1BCF53AF95BF03C76D` |
| `tests\Pickarr.Core.Tests\RecoKnnTests.cs` | `BCE98DEE880738FEF5CAB43B00B71C0779177F98895E2E15652EEFAEB314FD10` |
| `docs\recommendations.md` | `BB777249C4047B853D8C165FF1D3DAD1957B9EE2F5B96E7E8BDF99402886CD92` |
