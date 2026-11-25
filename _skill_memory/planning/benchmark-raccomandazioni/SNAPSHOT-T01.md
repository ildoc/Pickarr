# Snapshot T01 — 2026-11-23

HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto. Hash SHA-256 dei byte del worktree Windows, non dei blob Git normalizzati:

| File | SHA-256 |
| --- | --- |
| `src/Pickarr.Core/Recommendations/RecoEvalMetrics.cs` | `59BB2BB9A3E507EF8EBA79E90444D927EC3966B2530A177F875CAB38A3579282` |
| `src/Pickarr.Core/Recommendations/RecoEvalModels.cs` | `9B74C7B37897EA5500960667F19F57BC5C67653F0EDC3C02C29E7C7A7019B755` |
| `src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs` | `5AEB08A4D3CB6AF24C7F3766DAA89E7104F1D0DE467750B4C37AB86CA70E1CDB` |
| `tests/Pickarr.Core.Tests/RecoEvalMetricsTests.cs` | `17841D172B35C17AAF44A8FAD96F441A58F7E2E2145D0E576BCA413F3A9E2025` |
| `tests/Pickarr.Infrastructure.Tests/RecoEvalServiceTests.cs` | `9BA2BC7FF3A2655535926700D007901A0156752D0D8DA950D9CFF434B3287287` |
| `docs/recommendations.md` | `036BC1C591CDDF2F664ACF27DC2057E4BE205E87DDF4A55E2E9B25C27BA2673B` |
| `report-analisi-raccomandazioni.md` non tracciato preesistente | `C2EA1F96A7C63A07649F68C2851FE4E61CE53DEF57DC860619E44DD2F4D4E3DC` |

Ognuno dei cinque file C# ha anche una voce Git preesistente con percorso `PickArr` invece di `Pickarr`; su Windows le coppie puntano agli stessi byte e compaiono entrambe nel diff. I blob iniziali delle coppie erano identici. La KB non tracciata è amministrativa e non inclusa nel riferimento tecnico. Nessun altro file tracciato risulta modificato.
