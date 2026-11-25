# BASELINE — benchmark-raccomandazioni

Snapshot immutabile del 2026-11-23, branch `main`, HEAD `f9bbb2caa21c549ab4239f1810067b7ebcb27791`. Dati di produzione non ispezionati.

## 1. Componenti

| Componente | Responsabilità | Fonte |
| --- | --- | --- |
| `RecoEvalService` | Valutazione rating e gate L3 | `src/Pickarr.Infrastructure/Catalog/RecoEvalService.cs` |
| `RecoBuildService` | Profilo e classificazione | `src/Pickarr.Infrastructure/Catalog/RecoBuildService.cs` |
| `RecoQueryService` | Filtri, simili e reranking | `src/Pickarr.Infrastructure/Catalog/RecoQueryService.cs` |
| `RecoOnnxEmbedder` | Embedding ONNX | `src/Pickarr.Infrastructure/Metadata/Reco/RecoOnnxEmbedder.cs` |

## 2. Flussi as-is

- FLUSSO-01: valutazione manuale su rating admin del pool; pubblica report e stato L3; negativi inclusi nei fold come target di retrieval.
- FLUSSO-02: fatti di segnale globali, vettori e embedding limitati al pool, ranking per profilo pubblicato in DB.
- FLUSSO-03: simili nel pool abilitato con preselezione locale fino a 120 e fusione TMDB.
- FLUSSO-04: nessun progetto benchmark trovato tramite glob `**/*Benchmark*`; nessuno snapshot prodotto.

## 3. Metriche

| Metrica | Baseline | Metodo |
| --- | --- | --- |
| Dimensione embedding modello corrente | 384 float per titolo | `RecoModelCatalog.EmbeddingDimensions` |
| Fold massimi valutazione corrente | 50 | `RecoEvalMetrics.MaxFolds` |
| Cap candidati simili | 120 | `RecoQueryService.NarrowSimilarCandidatesAsync` |
| Titoli e valutazioni produzione | Circa 33.000 e 190, dichiarazione utente; non misurati | Da verificare su snapshot autorizzato |
| Accuratezza, tempo, memoria | Non misurati | T08 |

## 4. Note

Il report originale è non tracciato: `report-analisi-raccomandazioni.md`, SHA-256 `C2EA1F96A7C63A07649F68C2851FE4E61CE53DEF57DC860619E44DD2F4D4E3DC`. Nessun test o job è stato eseguito per questo assessment.
