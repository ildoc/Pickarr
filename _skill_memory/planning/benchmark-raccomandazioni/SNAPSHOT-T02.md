# Snapshot T02 — 2026-11-23

- HEAD: `f9bbb2caa21c549ab4239f1810067b7ebcb27791` (invariato).
- Indice staged: vuoto.
- Worktree Windows; hash SHA-256 dei byte locali. Le voci Git `PickArr`/`Pickarr` segnalate dal repository sono gli stessi file fisici su Windows; nessun rename o cleanup è stato tentato.
- `SNAPSHOT-T01.md` iniziale è stato verificato byte-per-byte prima di agire. La tabella mostra hash iniziale T01 → finale T02 per i file ereditati che T02 ha esteso; il report non tracciato preesistente è invariato.

## File ereditati T01

| File | Hash iniziale T01 | Hash finale T02 |
| --- | --- | --- |
| `src/Pickarr.Core/Recommendations/RecoEvalMetrics.cs` | `59BB2BB9A3E507EF8EBA79E90444D927EC3966B2530A177F875CAB38A3579282` | `6605672F2FCCA75A683639862EC598D947A8877D3F45986075094C1336A44211` |
| `src/Pickarr.Core/Recommendations/RecoEvalModels.cs` | `9B74C7B37897EA5500960667F19F57BC5C67653F0EDC3C02C29E7C7A7019B755` | `9B74C7B37897EA5500960667F19F57BC5C67653F0EDC3C02C29E7C7A7019B755` |
| `src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs` | `5AEB08A4D3CB6AF24C7F3766DAA89E7104F1D0DE467750B4C37AB86CA70E1CDB` | `0A69518B5C369BEC967E964DABB45CD9488CF89CD046B793F34520DEDCC84ED2` |
| `tests/Pickarr.Core.Tests/RecoEvalMetricsTests.cs` | `17841D172B35C17AAF44A8FAD96F441A58F7E2E2145D0E576BCA413F3A9E2025` | `2812AF51B3D2D34ACAA66B47B5BD31FE0F836594E1AB4800E01B86204654D0CF` |
| `tests/Pickarr.Infrastructure.Tests/RecoEvalServiceTests.cs` | `9BA2BC7FF3A2655535926700D007901A0156752D0D8DA950D9CFF434B3287287` | `54EC83AD08AA5F813A5BDB49863CF9DB2F06CFDB3968FAE73F95BC8D763CEFD7` |
| `docs/recommendations.md` | `036BC1C591CDDF2F664ACF27DC2057E4BE205E87DDF4A55E2E9B25C27BA2673B` | `E9F4335A19BEB68001570612A1852BC640DD29865D229E08558B88F12AD022B3` |
| `report-analisi-raccomandazioni.md` (preesistente, non tracciato) | `C2EA1F96A7C63A07649F68C2851FE4E61CE53DEF57DC860619E44DD2F4D4E3DC` | `C2EA1F96A7C63A07649F68C2851FE4E61CE53DEF57DC860619E44DD2F4D4E3DC` |

## Altri file di codice, test e documentazione finali

| File | SHA-256 finale |
| --- | --- |
| `src/Pickarr.Core/Abstractions/IRecoLevel3Model.cs` | `428A51DCC606D428CE1FF6E60FDFBC10A8CC757D1C8D5628F8FE6A034CCB5754` |
| `src/Pickarr.Core/Recommendations/RecoLevel3.cs` | `D6701D0A9069137748831203D184C114241689B908D2A98A96B3594AB8B6442F` |
| `src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs` | `558D5D44753F70343E4EDD4D23C352D4549002D45A16CCC2D91153892238E617` |
| `src/Pickarr.Infrastructure/Metadata/Reco/RecoLevel3Model.cs` | `01CF714B6C1103D7066C9052AC468DEBC7264C44A4785E2E4F7BE34DC650016A` |
| `src/Pickarr.Infrastructure/Metadata/Reco/RecoLevel3Training.cs` (nuovo) | `0AE1BE959A934D5FD23CBEBC9ACA87D5E462AAAB002E11268E9097E750C8AC3E` |
| `tests/Pickarr.Infrastructure.Tests/RecoBuildServiceTests.cs` | `4050071EAA8B616D1DB4B83CF051480BC8AF16DEC199B8A6E041821CA5B91AEC` |
| `tests/Pickarr.Infrastructure.Tests/RecoLevel3ModelTests.cs` | `93EBE731E3CB38A26690A6D15260BA5D8831FCDD19236132EBFBE01ACFCF92F7` |

## Knowledge base locale

| File | SHA-256 finale |
| --- | --- |
| `_skill_memory/planning/benchmark-raccomandazioni/AGENT_KB.md` | `A29E80ECE2A3F61B2093ADA555A1F60FBEBC09447EE787D28EE576E8550E6595` |
| `_skill_memory/planning/benchmark-raccomandazioni/ANALYSIS.md` | `4974520A051D17BB95DFF145AA768B68044B7790CB52F70D48D2F12E260556D7` |
| `_skill_memory/planning/benchmark-raccomandazioni/ARCHIVE.md` | `E2789664DBBD2D731E5E7F3E8C85DE4A79E4EC6A1F7209D1F3FEDCE69AD0B62E` |
| `_skill_memory/planning/benchmark-raccomandazioni/BASELINE.md` | `0B056C9FDCBDC5DE9AF1B88C77701B949CD47E3E371549A3F52F0E8CFEAA44D8` |
| `_skill_memory/planning/benchmark-raccomandazioni/PLAN.md` | `B96CC72BC672C05E609FDC00A4F5E727265C9181AA898BB8013D02F42F51F69B` |
| `_skill_memory/planning/benchmark-raccomandazioni/README.md` | `36582A1F00AE9759DB4298233F3C664BB12D08FBFF5C3CAB2A37563859B821A9` |
| `_skill_memory/planning/benchmark-raccomandazioni/SNAPSHOT-T01.md` | `B1C24C6AD9B42C8CBB4038626B1E45F6DD541E87ACFEB6B1C6896A43833A9363` |
| `_skill_memory/planning/benchmark-raccomandazioni/STATUS.md` | `024337774D5521C80817D3ACB9560652EF0473B370B0AE6EC0477C146D58847C` |

`SNAPSHOT-T02.md` è il nuovo manifest e non include il proprio hash per evitare una referenza ricorsiva; la sua SHA-256 va letta dal worktree. Il report radice preesistente è rimasto invariato. Evidenze test, format e `git diff --check`: `ANALYSIS.md` §7 e `ARCHIVE.md` §A-002.
