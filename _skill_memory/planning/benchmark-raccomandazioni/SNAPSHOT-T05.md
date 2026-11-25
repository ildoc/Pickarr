# Snapshot T05 — 2026-11-23

- HEAD iniziale/finale `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto. T05 DONE, nessun T06/T07/T08 avviato.
- Manifest completo SHA-256 dei byte filesystem Windows, comprende nuovi ed ereditati. Freshness iniziale:33/33 hash T03+override T04-FINAL corrispondenti. Finale:25/25 file ereditati fuori scope T05 invariati; solo8 file ereditati autorizzati modificati (runner/helper, csproj test,2 guide,3 KB). Aggiunti runner, fixture SQL, test e questo manifest. Questo file non include il proprio hash.
- F-007: percorsi Git `PickArr`/`Pickarr` collidono sugli stessi file fisici Windows; tabella usa `Pickarr`, nessuna rinomina/pulizia. Solution/progetto utility e tutte le modifiche T01/T02 preservati; csproj test aggiunge soltanto copia della fixture. Nessun package o restore.
- Prove `ANALYSIS.md` §7 T05 E05-01/02/03:15 test PASS (7 snapshot+8 preparazione), build0 warning/errori, format runner/test exit0, `git diff --check` PASS con avvisi CRLF ereditati. CLI reale su fixture: report byte-identici e input SHA invariato; zero workspace sintetici residui. DLL Debug verificata SHA-256 `FB3AFB81B079EE12A7FB839C866E3F0F75C2DE7EC3A848D4BEEE3EF6BE148E76` (artefatto build, non file sorgente da preservare).
- Report esclusivamente diagnostico, qualità null, seed riservato/non utilizzato. Directory private fidate e root protette esplicite necessarie; nessuna garanzia contro alias/hardlink/race avversarie. Nessun accesso cluster/DB sviluppatore/snapshot privato, app/job/migrazione o provider HTTP; nessun benchmark reale/T13.
- Checklist archiviazione verificata: meno di10 task DONE, budget stimato senza tokenizer non superato, nessuna decisione rende obsolete note pregresse; trigger finding conclusi soddisfatto, nuovo F-004 archiviato in `ARCHIVE.md` §A-003. Prossimo T06 in nuovo contesto dopo autorizzazione, non avviato. Nessun commit.

| File | SHA-256 finale |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\AGENT_KB.md` | `A29E80ECE2A3F61B2093ADA555A1F60FBEBC09447EE787D28EE576E8550E6595` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `FEE572B5885921D912C144B0CA588AC8A8F9FDC2F8A7FA91B3BF96AE0F1D5037` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `128A0F90C50D63B66075F650E513517327975B4E66D8614268D96C2BEC3D1DD7` |
| `_skill_memory\planning\benchmark-raccomandazioni\BASELINE.md` | `0B056C9FDCBDC5DE9AF1B88C77701B949CD47E3E371549A3F52F0E8CFEAA44D8` |
| `_skill_memory\planning\benchmark-raccomandazioni\PLAN.md` | `B96CC72BC672C05E609FDC00A4F5E727265C9181AA898BB8013D02F42F51F69B` |
| `_skill_memory\planning\benchmark-raccomandazioni\README.md` | `36582A1F00AE9759DB4298233F3C664BB12D08FBFF5C3CAB2A37563859B821A9` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T01.md` | `B1C24C6AD9B42C8CBB4038626B1E45F6DD541E87ACFEB6B1C6896A43833A9363` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T02.md` | `244D889BA0EB80E130E7DCA39D72353F891FFA3C349E66359A3B350F66A88C4A` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T03.md` | `428C9F8788A33009B9A24FE75DA95F5F405FF853F2890A330D187A5D92F1DA3B` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T04-FINAL.md` | `675832B399038B92A1ADBEEA36226A4FC56FBD4FA3D02575B9098E8CD8F98659` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T04.md` | `9FEEC9099679758AB079293F131D170BB877104B052AB3ECC97AEA87EC51974C` |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `BE60B0D6FE0C9DAE88E9F28DBA992D5150527953353ADB1CB0FE83255E15620D` |
| `docs\developer\README.md` | `7E445E859F1B2749EE5C093FD0800CD8ACF69FF878D5BAC3CABFAC995D8890EE` |
| `docs\developer\recommendation-snapshots.md` | `6592A1B10F0E292E33825B80364DCC3D7EFFB3011D61943DD6DC79CDB7A748B7` |
| `docs\README.md` | `F54136E0AAD8163220D8AA51DEED09841ABB59105BB74C1269C666BE05D44ABE` |
| `docs\recommendations.md` | `E9F4335A19BEB68001570612A1852BC640DD29865D229E08558B88F12AD022B3` |
| `Pickarr.slnx` | `00034D6EC7886FCABD3A5814787309DBBDDCF23ADE578AE5F957BB17499A22E9` |
| `report-analisi-raccomandazioni.md` | `C2EA1F96A7C63A07649F68C2851FE4E61CE53DEF57DC860619E44DD2F4D4E3DC` |
| `src\Pickarr.Core\Abstractions\IRecoLevel3Model.cs` | `428A51DCC606D428CE1FF6E60FDFBC10A8CC757D1C8D5628F8FE6A034CCB5754` |
| `src\Pickarr.Core\Recommendations\RecoEvalMetrics.cs` | `6605672F2FCCA75A683639862EC598D947A8877D3F45986075094C1336A44211` |
| `src\Pickarr.Core\Recommendations\RecoEvalModels.cs` | `9B74C7B37897EA5500960667F19F57BC5C67653F0EDC3C02C29E7C7A7019B755` |
| `src\Pickarr.Core\Recommendations\RecoLevel3.cs` | `D6701D0A9069137748831203D184C114241689B908D2A98A96B3594AB8B6442F` |
| `src\Pickarr.Infrastructure\Catalog\RecoBuildService.cs` | `558D5D44753F70343E4EDD4D23C352D4549002D45A16CCC2D91153892238E617` |
| `src\Pickarr.Infrastructure\Catalog\RecoEvalService.cs` | `0A69518B5C369BEC967E964DABB45CD9488CF89CD046B793F34520DEDCC84ED2` |
| `src\Pickarr.Infrastructure\Metadata\Reco\RecoLevel3Model.cs` | `01CF714B6C1103D7066C9052AC468DEBC7264C44A4785E2E4F7BE34DC650016A` |
| `src\Pickarr.Infrastructure\Metadata\Reco\RecoLevel3Training.cs` | `0AE1BE959A934D5FD23CBEBC9ACA87D5E462AAAB002E11268E9097E750C8AC3E` |
| `tests\Pickarr.Core.Tests\RecoEvalMetricsTests.cs` | `2812AF51B3D2D34ACAA66B47B5BD31FE0F836594E1AB4800E01B86204654D0CF` |
| `tests\Pickarr.Infrastructure.Tests\Pickarr.Infrastructure.Tests.csproj` | `437E4FAE6EDFB4C79BFEF28609D0915FDF10E9CA98006CA750C9F1C0047BA784` |
| `tests\Pickarr.Infrastructure.Tests\RecoBuildServiceTests.cs` | `4050071EAA8B616D1DB4B83CF051480BC8AF16DEC199B8A6E041821CA5B91AEC` |
| `tests\Pickarr.Infrastructure.Tests\RecoEvalServiceTests.cs` | `54EC83AD08AA5F813A5BDB49863CF9DB2F06CFDB3968FAE73F95BC8D763CEFD7` |
| `tests\Pickarr.Infrastructure.Tests\RecoLevel3ModelTests.cs` | `93EBE731E3CB38A26690A6D15260BA5D8831FCDD19236132EBFBE01ACFCF92F7` |
| `tests\Pickarr.Infrastructure.Tests\RecoPreparationTests.cs` | `D3F98948B09D34A40C95013CA047562AD1A0AFD0D65C1E2BA90778936B43F92F` |
| `tests\Pickarr.Infrastructure.Tests\RecoSnapshotTests.cs` | `97D014A968BB083B3017096A598380B7E904C4A5B4DE4A055F5591C3F8779159` |
| `tools\Pickarr.RecoBench\fixtures\preparation.sql` | `FCD88656371DDDF767973BAF905BD9E635B4003366C622925E2E7ED61B9D4E06` |
| `tools\Pickarr.RecoBench\Pickarr.RecoBench.csproj` | `C7919D00E207967D0A5F5B9568E74201602138D8FDCA14EAF23BD164A7E63DB9` |
| `tools\Pickarr.RecoBench\PreparationRunner.cs` | `C5D790A4E35DAFB12AD07641C9E6C4E34D1366CB8A5C68FF35D0829E8AFE4225` |
| `tools\Pickarr.RecoBench\Program.cs` | `41F13B4053DCC33683C2FC6074D93DED36187AD8C5AF034392EE9686C9255E7A` |
| `tools\Pickarr.RecoBench\Snapshot.cs` | `32C00DFD6BED00DB16973F696FCF43F681D886C15F564884C0D3A5C2DE2E46C8` |
