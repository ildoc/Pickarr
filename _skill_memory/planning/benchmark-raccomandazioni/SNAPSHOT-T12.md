# Snapshot T12 — 2026-11-24

- HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`; indice staged vuoto al completamento di T12.
- Manifest ereditato: `SNAPSHOT-T11.md`; report T11 privato, hash input/modelli e limiti D-20/D-21 restano invariati.
- T12 DONE: documentate snapshot protetti, uso runner, modelli, interpretazione dei risultati, T11 post-correzioni e limiti cluster.
- Aggiornata la specifica reco per segnali fuori pool, ricerca esatta Simili con fallback sparse e confronto temporale diagnostico.
- Nessun segreto, dato personale, percorso privato reale o hash report privato copiato nelle pubblicazioni.
- Nessuna migrazione EF, reset/sostituzione DB, provider, job live, pod/cluster o benchmark nuovo.
- F-001/F-007 restano OPEN; T13 resta successiva e richiede revisore indipendente.

## Evidenze

| Evidenza | Esito |
| --- | --- |
| Guida recommendation-snapshots | aggiornata e comandi/link controllati |
| Specifica recommendations | BR-REC-04, Simili e limiti benchmark aggiornati |
| Test/API T09-T11 | Core 9 PASS; API 1 PASS; build precedente 0 warning/errori |
| Test Core T12 | 21 PASS |
| `git diff --check` | PASS; soli avvisi CRLF preesistenti |

## File modificati da T12

| File | SHA-256 |
| --- | --- |
| `docs\developer\recommendation-snapshots.md` | `C04388043920208EF0F9B8C673B1A26F21E17A0B4AD045FF7B33D9A3CBFF3024` |
| `docs\recommendations.md` | `4E1A60E03C1EA381F86C569263CBAFB4F9AB10040F6BA4DCE4ED661B4A8D1209` |
