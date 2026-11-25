# Snapshot T07 — 2026-11-23

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice vuoto; T01–T07 DONE; T08 non avviato; T13 NON ESEGUITA. F-001 resta OPEN per rappresentatività residua evaluator/gate; F-007 OPEN senza rename o cleanup.
- Manifest ereditati in ordine: `SNAPSHOT-T05.md` SHA-256 `8318CB2521E2A6A9DDFEFC68BC4DAF448B02FF19FFB69CA29B0088C8A67F0640`; `SNAPSHOT-T06.md` SHA-256 `EA72BF568C9438E32D48DD801BD6930CDD06A764D7084642141E812F8E7A3425`; `SNAPSHOT-T06a.md` SHA-256 `C54A990B62C493218BBC946C0C1E566525FE414815FF377EA9FE7C3C05F21A3B`; `SNAPSHOT-T06b.md` SHA-256 `F28A26819E5E073A8B7EC56A1B2B417EAEF525F65088C66F2F74EB657FB8014B`; `SNAPSHOT-T06c.md` SHA-256 `3D929B85D867C056B62AE85A28EFC1EB15BAA5D7D4DD9CEA0E43400AC8F112CA`. Applicare i manifest ereditati in ordine e poi gli override sotto; T06 è riferimento amministrativo.
- Freshness: HEAD e indice verificati prima del lavoro; 44 hash ereditati T06c corrispondenti. Dei tre file ereditati toccati intenzionalmente (runner `Program.cs`, csproj runner, guida), tutti sono elencati nell'override; i 41 rimanenti erano invariati. Nessun altro contenuto preesistente è stato ripristinato o rimosso.
- Override e nuovi file SHA-256 dei byte filesystem Windows:

| File | SHA-256 |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `75A3970992A75B3E5D836104AE891CF62EFCC6B2A8C7722E25D7E5640147C37E` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `8A97F09CC227BDA30811D9B84693CFD8C7EE559604B96C13F835DC4577643630` |
| `_skill_memory\planning\benchmark-raccomandazioni\ARCHIVE.md` | `793CA5266C7A43EBD6884B82D4A0292F5824693564386D52BC106F6DC1E8AB9C` |
| `Directory.Packages.props` | `17F3FEACF6A68C937B42FA3ABBA303BB6A754B6BD18F5EAB42A6B971ABA02EE5` |
| `tools\Pickarr.RecoBench\Pickarr.RecoBench.csproj` | `F4A3DF4420296B32D6F898EFC22A915B424006FCDADE40BE48ED5C95EB1AA53A` |
| `tools\Pickarr.RecoBench\RecoBenchmarkModelCatalog.cs` | `935DFA038B456EF9C22A02DFB39DE48A96B21BB438776019A61719BA4EF32008` |
| `tools\Pickarr.RecoBench\MultilingualMiniLmEmbedder.cs` | `44E3E5A5EF1EF3578B3FFD6346CAFE2025D79518E499550F1D86CCFD35D825F9` |
| `tools\Pickarr.RecoBench\RecoModelSelfTestRunner.cs` | `37DEC0CFD105126721CDBEBD6092D12BF649C628EB678FDCAA3D18C8C738B571` |
| `tools\Pickarr.RecoBench\Program.cs` | `CE3848647F6CB1393A813348D557C67C7C15D8F960E7FDDA1D3A4D29329CF3AC` |
| `tests\Pickarr.Infrastructure.Tests\RecoModelExperimentTests.cs` | `8A5E49CBBB38A1083128D2B11F6AD5C9E2A4A072B340A02A8781CE93AE610782` |
| `docs\developer\recommendation-snapshots.md` | `C978E059F0766F48171AE1A3A6DC4A86F12CA58C20C89C83EE573659BF7D4010` |

- Artefatti di verifica fissati: modello corrente ONNX `AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1`, vocab WordPiece `07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3`; candidato multilingual ONNX `66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC`, tokenizer JSON `B60B6B43406A48BF3638526314F3D232D97058BC93472FF2DE930D43686FA441`. Entrambi 384 dimensioni; massimo sequenza 256 e 128 rispettivamente. Le revisioni e i dettagli di tokenizzazione sono in `ANALYSIS.md` §7 T07 e nella guida.
- Acceptance e verifiche: self-test reale su sei testi sintetici multilingui, golden token ID e hash verificati; output 384 dimensioni, L2 unitario e cosine ripetuto 1. Build runner PASS; test `RecoModelExperimentTests` 5 PASS; filtro Infrastructure `FullyQualifiedName~Reco` 87 PASS, 0 skipped; format e `git diff --check` PASS. Nessun download nei test standard. Nessuna valutazione di qualità/performance o promozione del modello.
- La directory temporanea di test conteneva esclusivamente i due ONNX pubblici, `vocab.txt` e `tokenizer.json`; rimossa dopo verifica. Nessun testo utente, DB, cluster, API provider o servizio esterno coinvolto nel self-test. Nessun commit.
- Evidenze, comandi e limiti: `ANALYSIS.md` §7 T07; D-17 in `STATUS.md`; decisione archiviata in `ARCHIVE.md` §A-006. Nessun hash del manifest stesso.
