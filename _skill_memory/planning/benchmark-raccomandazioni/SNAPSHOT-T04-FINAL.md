# Snapshot T04 finale — 2026-11-23

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto. T04 DONE dopo seconde autorizzazioni esplicite; T05 non avviato.
- Manifest incrementale: applicare la tabella seguente sopra le 32 righe di `SNAPSHOT-T03.md` (hash del manifest base `428C9F8788A33009B9A24FE75DA95F5F405FF853F2890A330D187A5D92F1DA3B`). Le sei righe esistenti sono sostituite, la riga del checkpoint T04 è aggiunta. Non applicare poi gli override del checkpoint ricognizione.
- Freshness iniziale: 32/32 hash T03/T04 riconciliati. Finale: tutti i file ereditati fuori dallo scope approvato invariati; solo helper conteggi, relativo test/guida, STATUS/ANALYSIS e questo nuovo manifest modificati dalla ripresa. Checkpoint ricognizione `SNAPSHOT-T04.md` preservato. Nessuna pulizia/rename delle collisioni case F-007.
- Verifiche: sette test `RecoSnapshotTests` PASS; format sui tre C# modificati exit0 (warning caricamento workspace), `git diff --check` PASS (avvisi LF/CRLF ereditati). Nessuna suite completa, benchmark o verifica T13 eseguita.
- Acquisizione privata159444992 byte, SHA-256 `B748506542CE9664951B0D61601FEAA0D55B19B52133BD69CF7B2E5997EDBB43`; integrità/FK/conteggi/hash trasferimento/ACL/read-only/standalone PASS. Origine/copia:33894 titoli,1 profilo,318 rating nella finestra osservata; non prova di identità byte/logica del live. Dettagli autorizzazioni, limiti e percorsi privati: `ANALYSIS.md` §7 T04.
- DB e receipt sono esclusivamente nella destinazione privata fuori repository, nessun artifact pubblico. Solo staging creato ripulito. Nessuna SQL scrittura/checkpoint all'origine, installazione, modifica risorse, restart, API/job/migrazione o chiamata provider.
- Rivalidare questo manifest e hash/ACL della copia privata prima del futuro uso; non ricollegarsi al cluster senza nuova autorizzazione. Questo manifest non include il proprio hash.

| File | SHA-256 finale |
| --- | --- |
| `tools\Pickarr.RecoBench\Snapshot.cs` | `E1C78E92ED6D5E4D54983BA30B8C70ACE3727861971CDC7619B2D067FB5F3E0A` |
| `tools\Pickarr.RecoBench\Program.cs` | `49AAC99870787A63870B841FC8584846207302962D1FADCA24CC4B01DCC537E9` |
| `tests\Pickarr.Infrastructure.Tests\RecoSnapshotTests.cs` | `97D014A968BB083B3017096A598380B7E904C4A5B4DE4A055F5591C3F8779159` |
| `docs\developer\recommendation-snapshots.md` | `C3346320CFB5D1C1C7E21605E82D0A818CA8AD0EAB8610C442793D886FC25844` |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `8575DB47C5D970FD3ED7800398E86062BD6CD4CCAE6BFD0862CD90624A72AA07` |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `E9E3756B571BB304BED9826DCEDE1B924509A5B341EA4467248DCC6B2FB71489` |
| `_skill_memory\planning\benchmark-raccomandazioni\SNAPSHOT-T04.md` | `9FEEC9099679758AB079293F131D170BB877104B052AB3ECC97AEA87EC51974C` |
