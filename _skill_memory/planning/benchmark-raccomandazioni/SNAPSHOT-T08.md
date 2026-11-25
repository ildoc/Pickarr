# Snapshot T08 — 2026-11-23

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto al completamento dei run locali. TR-08 DONE sul piano modificato da D-21: confronto full-pool locale completato e ripetuto, misure cluster esplicitamente rinunciate e non eseguite. T13 NON ESEGUITA.
- Manifest ereditato: `SNAPSHOT-T07.md`, SHA-256 `A89366239B23F5D1F2DBF7E10A61BFFB773CA9B14941090C306431E1A751623E`; applicarlo prima degli override seguenti. I cambi preesistenti T01–T07, inclusi i percorsi case-distinct, sono preservati; nessun commit.
- Manifest del primo run capped e dei file allora introdotti (storico; override D-20 in fondo):

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `BDC75805DA116EADE32FC2E50B5000CFD66148122D9AB7734590B12E95CD3B67` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `D967A7475A9EFD801DF4500C7054C9D7859225E4F7757BC0305F983E33F4654B` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `EB0348D99753A39E4AD0A198BCD4D220259722B1E3CCEF99FEEA8F3056216CA1` |
| `tools\Pickarr.RecoBench\BenchmarkTextInput.cs` | `C91E7927011B5B88FDD67166EBAD363179D037DFF565952443F56E4C540FC35D` |
| `tools\Pickarr.RecoBench\EmbeddingPerformance.cs` | `5C3892C8E236FCD4B6584AA96991244D634BF3C54C99E3E69B23ADE33F6097DD` |
| `tools\Pickarr.RecoBench\ModelComparisonRunner.cs` | `EA20ECC85236E24F61557805D82D854C08EB55E9FB718635D35FC3FCF353E153` |
| `tools\Pickarr.RecoBench\Program.cs` | `E4B879E90D2BE9D45E8F3A00A181712390587F4E0C9C60AABD99DE23D8B73A1F` |
| `tests\Pickarr.Infrastructure.Tests\RecoTemporalBenchmarkTests.cs` | `179210BA4CCE67579D6FE2085ECC6D57161F792D8FF37A765DC3909D5EBE3AA3` |
| `tests\Pickarr.Infrastructure.Tests\RecoEmbeddingPerformanceTests.cs` | `D82337F5B13168168A9C2BBB521A15A006785D6F79D6526471B4350AB8B282B8` |
| `docs\developer\recommendation-snapshots.md` | `BE139F52EA4937974EFC9203B1C457EA7B231AF020B846819DECA03FAF7BD2F6` |

- Input autorizzato: `C:\Users\fgiomi\PickarrPrivate\TR04-20261123\input\Pickarr.snapshot.db`, SHA-256 `B748506542CE9664951B0D61601FEAA0D55B19B52133BD69CF7B2E5997EDBB43`, attributo ReadOnly verificato prima e dopo. Report privato: `C:\Users\fgiomi\PickarrPrivate\TR04-20261123\output\T08-model-comparison.json`, SHA-256 `8EFAFE1E144C28B897BE27FE63827055B3B7A467977C78E283FDB7FA206B7C23`; ACL soltanto operatore/SYSTEM.
- Il report è stato prodotto prima del controllo di completezza qualità aggiunto al runner finale: JSON `code.assemblySha256` `7C8BACD82D1AA736B258813560CC38664E5F026A5E1D4E9252AFE9D3EFBC194A`, `status=completed_not_certified_sufficient`; le metriche riportano però embedding `embedding_coverage_incomplete` per entrambi i modelli. Il report è stato lasciato immutato; il runner corrente ora esige metriche embedding complete prima di exit 0.
- Misure, conteggi, interpretazione, verifiche e limiti: `ANALYSIS.md` §7 T08; archive parziale A-007; F-011 OPEN. Test mirati 17 PASS, format runner/test PASS, `git diff --check` PASS. I quattro artefatti modello temporanei fissati sono stati verificati e rimossi dopo il report.

## Override D-20 — confronto locale full-pool

- D-20 autorizza il confronto locale completo sul solo input T04, non cluster/pod/live. Snapshot osservato al `2026-11-23T11:42:42Z`, ReadOnly e fingerprint invariato. Due report nuovi sono conservati fuori repository con ACL operatore/SYSTEM; il report capped storico non è stato sovrascritto. I report non sono copiati in repository.
- Entrambi i modelli hanno embedding freschi verificati per tutti i 33.894 titoli pool in entrambi i run. Stesso seed 42, protocollo, cutoff, artefatti/hash e metriche: qualità/campioni identici tra report. Recall@10 embedding = 0 in validation/test per entrambi; combined test Recall@10 = 1/69. Risultato descrittivo, 1 profilo, senza promozione o conclusione di superiorità.
- Prestazioni, limiti, conteggi e AUC: `ANALYSIS.md` §7 T08. Modelli pubblici fissati conservati privatamente dopo hash/self-test PASS; nessun testo del DB è uscito dall'host. Input/output e file modello con ACL verificata; nessun accesso a provider, API, job o cluster.
- Test T08 aggiornati: 18 PASS; format mirato PASS; `git diff --check` PASS con soli avvisi CRLF preesistenti. F-011 RESOLVED; F-001 OPEN. T08 locale completato, TR-08 ancora BLOCKED per autorizzazione/ambiente cluster; T13 NON ESEGUITA.
- Hash SHA-256 degli override D-20 (byte filesystem Windows):

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `8F6981F2EF4F5CEB656E13D46730C0333212DE25DDCEAAF93142077A4D54F367` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `3FAF2E99DA1176EE249013E250FBA3079EE996B89E2A41F7F04A62DED885B73D` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `8F4E0D4E0C944D0D46012806CC6C9885285F7F1DAE891617E4E1740C1E21F7F3` |
| `tools\Pickarr.RecoBench\ModelComparisonRunner.cs` | `B93AF4B8A30D18838ACF2F37EBCECAD5ED58328961C6C92ED002097F9441669B` |
| `tests\Pickarr.Infrastructure.Tests\RecoTemporalBenchmarkTests.cs` | `6AC3DD27288902D1354E96E25F1CDDDDABA939D5A7C31FD60628445E5623C8F2` |
| `docs\developer\recommendation-snapshots.md` | `FCED7ACA7414C2E6FB305A2AE9AE2A0A7FCAD5B9DBD46B3C9E7551C1D503F9B3` |

## Override D-21 — chiusura con limite approvato

- Il 2026-11-24 l'utente ha esplicitamente rinunciato alle misure in ambiente simile al cluster per TR-08, accettando che prestazioni e consumi di produzione restino non misurati. La provenienza del database non rende equivalenti le prestazioni degli ambienti. La precedente descrizione BLOCKED in questo manifest e in A-008 è storica.
- TR-08 è DONE sul piano modificato; F-001 resta OPEN e la verifica indipendente T13 NON ESEGUITA. Nessuna promozione modello, nessun accesso cluster o database in questo aggiornamento. Futuri accessi cluster richiedono approvazione operativa separata.
- Hash dei sei file elencati nell'override D-20 verificati prima di questo aggiornamento; verificati anche i due report privati, SHA-256 `8936E0B379F609C89A9FEFAEF74DDEE5AC60D43E582F8B67B1284A34900CAB5B` e `CA81EF1F205A629298D5F9B6D1EE9169DBBFB670173D0FF353E776E5C23A2BAD`. Nessun test o benchmark rieseguito per questa modifica solo documentale.
- Override D-21, SHA-256 dei byte filesystem Windows:

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `3AB44B24385CD7E4CE30831C64B615DA4A30008157EB9BADD68DFACF37387D53` |
| `_skill_memory\planning\benchmark-raccomandazioni\PLAN.md` | `B0C926F4C0334C61EBA7E31A68AC8C864F48BEB8D33F6057DF75FABC94CE81F8` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `C82B267070CCFBEC358F7677C73488A64EB8005988B0C126CD7DD8343EA47442` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `1D16BDDE20851D82A5E24C5755D7A0F187AEBB00079EB209550CC066DC676774` |
