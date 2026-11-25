# Snapshot T06c — 2026-11-23

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice vuoto; T06a–c e T06 DONE; T07 non avviato; T13 NON ESEGUITA. F-001 resta OPEN per evaluator/gate non temporali; F-007 OPEN senza rename o cleanup.
- Manifest ereditati in ordine: `SNAPSHOT-T05.md` SHA-256 `8318CB2521E2A6A9DDFEFC68BC4DAF448B02FF19FFB69CA29B0088C8A67F0640`; `SNAPSHOT-T06.md` SHA-256 `EA72BF568C9438E32D48DD801BD6930CDD06A764D7084642141E812F8E7A3425`; `SNAPSHOT-T06a.md` SHA-256 `C54A990B62C493218BBC946C0C1E566525FE414815FF377EA9FE7C3C05F21A3B`; `SNAPSHOT-T06b.md` SHA-256 `F28A26819E5E073A8B7EC56A1B2B417EAEF525F65088C66F2F74EB657FB8014B`. Applicare T05, override T06a, T06b e poi quelli sotto; T06 è riferimento amministrativo. 44/44 file ereditati verificati inizialmente; 41/41 fuori dai tre file ereditati modificati invariati prima di aggiornare ANALYSIS/ARCHIVE.
- Override e nuovi file SHA-256 dei byte filesystem Windows:

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `C57DB67EFB7D229676211A62ADA735461C8978A58514B54C8EFC4B28CB3006C8` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `850549DF0F6F96068513E989430F01DC9C56BA5516763D47088D8E63AAC1A08A` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `03E51ED4ABD6C22559A03EDF478C95F8CC900FA02FF8E163CD8DDD44F0D40B8B` |
| `docs\developer\recommendation-snapshots.md` | `49EB10621590DEAFA2653F06C9897B0A7C28B8A965E4A9758587F98DBA1EB28E` |
| `tools\Pickarr.RecoBench\Program.cs` | `FBD328ABB0C2FDD1F15D27570D85761BE91F6B9AB63BF1EB0F09C409F28EA404` |
| `tools\Pickarr.RecoBench\BenchmarkRunner.cs` | `8027FA92BB217F0B26F1BE1ABDC6B548BCC3A3C1E92A8B69B3CD33B8F665E6C1` |
| `tools\Pickarr.RecoBench\TemporalBenchmark.cs` | `2C632420D1E1887DEDE10B33FA9479A8F71530AC4502249B9D1220200FD6EEDB` |
| `tests\Pickarr.Infrastructure.Tests\RecoTemporalBenchmarkTests.cs` | `B6818C8B0060AF36439E855DAD7784F57826851A5D39F1FB3A787E2169D70F45` |

- Nessun hash del manifest stesso. Acceptance, comandi effettivi, limiti e prove `ANALYSIS.md` §7 T06c; checklist STATUS §7 verificata, F-009 archiviato in `ARCHIVE.md` §A-005. Nessun dato reale, T07, T13 o commit.
