# Snapshot T09 — 2026-11-24

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto al completamento di T09.
- Manifest ereditato: `SNAPSHOT-T08.md`; override D-20/D-21 e limiti cluster restano invariati.
- T09 DONE: segnali fuori pool esclusi da `N_eff` e da tutte le componenti sparse/kNN/TMDB/L3; candidati restritti al pool esistente.
- Nessuna migrazione EF, cancellazione/reset/sostituzione database, chiamata provider, job live, accesso pod/cluster o benchmark privato.
- F-002 RESOLVED; F-001/F-003/F-007 restano OPEN; T10 non avviata.

## Evidenze

| Evidenza | Esito |
| --- | --- |
| Core reco tests | 16 PASS |
| Infrastructure RecoBuildService tests | 17 PASS |
| `git diff --check` | PASS; soli avvisi CRLF preesistenti |

## File modificati da T09

| File | SHA-256 |
| --- | --- |
| `src\Pickarr.Core\Recommendations\RecoSignals.cs` | `962A0D1C0599E9BF82A598E4952FE2C92263480A89EC3D6A436C470ED3F18AF4` |
| `src\Pickarr.Infrastructure\Catalog\RecoBuildService.cs` | `4FFDB21959AEA4708E0A6BC9AFB3F4B52ACDCA3D482B93CC2D673B72994A4789` |
| `tests\Pickarr.Core.Tests\RecoSignalsTests.cs` | `01C04C4E4F9A4CA32830894706DC2B7C6903EEA0643BC7CABC7B256FB52F075C` |
| `tests\Pickarr.Infrastructure.Tests\RecoBuildServiceTests.cs` | `A32AF02AD943A96E9723E3A7395541D9DB18BE2C1A9D4C71D7ED97A6FD392719` |
| `docs\recommendations.md` | `F303B808ECC1FAF556BC70CD7CE1BA64151E1779BE650ECB28B27C6B4BC6D63D` |
