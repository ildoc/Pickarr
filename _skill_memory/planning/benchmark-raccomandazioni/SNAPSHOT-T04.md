# Snapshot T04 — sola ricognizione, 2026-11-23

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto prima/dopo.
- Manifest incrementale: usare tutte le 32 righe di `SNAPSHOT-T03.md`, sostituendo soltanto le due righe sotto. SHA-256 del manifest base `428C9F8788A33009B9A24FE75DA95F5F405FF853F2890A330D187A5D92F1DA3B`.
- Prima della ricognizione: 32/32 hash T03 corrispondenti. Dopo: 30/30 file ereditati non modificabili corrispondenti; uniche variazioni intenzionali `STATUS.md` e `ANALYSIS.md`, oltre a questo nuovo manifest. Percorsi Git case-colliding F-007 preservati.
- Nessun codice/test/configurazione modificato; `git diff --check` PASS sui file tracciati, soli avvisi preesistenti di normalizzazione LF/CRLF. Nessun test eseguito: modifica esclusivamente amministrativa della KB e ricognizione operativa read-only.
- T04 BLOCKED, non DONE: attesa seconda conferma operativa. Nessuna copia/backup/staging, scrittura sul pod, installazione, lettura contenuti DB/segreti, chiamata provider, API/job/migrazione o arresto. Ricognizione, proposta e limiti: `ANALYSIS.md` §7 «T04 — Ricognizione autorizzata e gate acquisizione».
- Nessun DB o credenziale nel repository o in artifact. Questo manifest non include il proprio hash. Rivalidare anche contesto/pod/UID, spazio e strumenti prima della ripresa.

| File sostituito rispetto a T03 | SHA-256 finale |
| --- | --- |
| `_skill_memory\planning\benchmark-raccomandazioni\ANALYSIS.md` | `CA840E475CE3E0E69D1C5CC38A0EF4346BC88A3064A0DF794F8D427560CCF4F3` |
| `_skill_memory\planning\benchmark-raccomandazioni\STATUS.md` | `2F14ACC618213AEC64B461E504EC2BF9EFF1E9F746869B8177D03FC445FF5B3A` |
