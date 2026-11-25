# Recommendation benchmark snapshots

The utility provides snapshot acquisition, diagnostics and an isolated temporal benchmark
proxy. Never run the application, jobs or migrations against the snapshot.
Do not use a live database as benchmark input.

## Safety and authorization

- Obtain separate operational approval before inspecting or accessing a cluster, pod,
  volume or production database. Establish the source, tool availability, permissions,
  free space, transfer and destination protection first. This guide does not authorize
  such access or installation.
- Keep snapshots and results in separate, access-restricted directories **outside the
  repository** and outside all data directories. A snapshot contains private data and
  potentially encrypted secrets. Do not publish, log, or send it to a third party.
- Use a trusted local filesystem, private directories controlled by the operator and no
  concurrent process replacing paths or modifying the snapshot. Protect against other
  users with OS permissions; the utility does not set ACLs or provide a security sandbox.
- Pass the absolute repository root and every other protected directory as arguments.
  The utility rejects outputs in those directories or in the source directory, existing
  output files and SQLite sidecars, and symlinks/reparse points in supplied paths. Path
  checks are not a defense against concurrent path replacement, hard-link aliases,
  bind mounts, short-name aliases or other filesystem aliases. Verify the actual
  storage and keep it isolated; do not rely on these checks as a complete boundary.
- A live writer may continue committing. SQLite's online backup captures a consistent
  committed state, not necessarily the latest state at completion. SQLite may use or
  update WAL shared-memory/locking sidecars during a read; the utility does not write
  SQL to, checkpoint, migrate, replace, or delete the source database. Never compare
  live file bytes as proof that an actively written database was unchanged.

## Create and verify a snapshot

Use the already-built .NET utility on an authorized host with access to the live SQLite
database, its WAL and shared-memory files. The destination parent must already exist;
the destination and its `-wal`, `-shm`, and `-journal` names must not exist. Arguments
below are placeholders, not actual paths:

```text
dotnet run --project tools/Pickarr.RecoBench --no-build --no-restore -- snapshot <absolute-source-db> <absolute-new-snapshot-db> <absolute-repository-root> <absolute-data-directory> [other-protected-directories...]
```

It opens the source read-only, uses SQLite online backup, makes the copy standalone,
then checks `integrity_check`, `foreign_key_check`, and counts of `Titles`, `Profiles`
and `Ratings` on the copy. Only aggregate counts are printed. A failed operation
reports a sanitized error and removes its newly created destination; it never
overwrites an existing destination. Check the exit status. Missing tables, permissions,
space, integrity or foreign-key failures are errors, not a usable snapshot. Do not
paste counts or output into public logs if their disclosure is inappropriate.

For acquisition and transfer, verify the copy again at its destination before use,
and compare aggregate source/copy counts only with a documented concurrent-write
window. A matching count is not proof of byte-for-byte equality. Do not inspect
settings or secret values. With separate source-access approval, use
`counts <absolute-source-db>` to read only the three aggregate counts in one
read-only, deferred transaction. This command does not run integrity checks, read
settings, checkpoint or validate a snapshot; it sees committed data at its own
transaction boundary. Record counts before and after acquisition without claiming
that an active writer has stopped or that equal counts prove identical data.
To verify a transferred standalone snapshot, use
`verify <absolute-snapshot-db> <absolute-new-output-path> <absolute-repository-root> [other-protected-directories...]`
in place of the `snapshot` arguments above. The output path is checked but not created;
its parent must exist and differ from the snapshot directory. If a safe procedure is
unavailable, stop.

## Preparation diagnostics

Build the utility without starting the API:

```text
dotnet build tools/Pickarr.RecoBench/Pickarr.RecoBench.csproj --no-restore
dotnet run --project tools/Pickarr.RecoBench --no-build --no-restore -- prepare <absolute-snapshot-db> <absolute-new-report-json> <integer-seed> <absolute-repository-root> [other-protected-directories...]
```

Use only a closed, standalone snapshot, never a live database. Input and output must
be explicit absolute paths, in separate private directories outside the repository
and every protected data directory. Supply the actual repository root and all other
protected directories. The runner does not infer missing protected roots or set permissions.
It reuses the read-only/integrity/foreign-key/sidecar checks, revalidates paths at
report creation, and exclusively creates the report (`CreateNew`); existing files
are never overwritten. It keeps a read-only input handle and checks its SHA-256
before and after diagnostics. These checks do not remove the filesystem alias/race
limitations above, particularly on Unix where file sharing is not a mandatory lock.

The versioned JSON report contains:

- `kind=preparation_diagnostics`, status, and **null** `qualityMetrics`.
- Assembly informational version (including source revision when provided by the
  build), assembly SHA-256 (also distinguishes local uncommitted builds), protocol
  configuration, explicit seed and its usage. The seed is recorded but not consumed:
  no sampling, training or randomized algorithm runs yet.
- Opaque SHA-256 of the entire standalone input file, not just aggregate counts.
  Hashing reads bytes without interpreting settings or secrets; no names, IDs,
  rating rows, paths or secret values are included in the report.
- Aggregate title/profile/current-rating counts, distinct rated-title/profile counts
  and fractions, positive (`Love`/`Like`), negative (`Dislike`/`NotInterested`/
  `not_interested`, case insensitive), unknown-label counts, and count of ratings
  with a SQLite-parseable timestamp. Unknown labels are not treated as positive.
  Coverage fractions describe current ratings, not historical events or embeddings.
  A zero denominator is reported as null.
- Runtime, OS description, process architecture and logical processor count.

Reports are byte-identical for the same input bytes, executable, seed and reported
environment. They deliberately omit timestamps, elapsed time, machine/user names
and absolute paths. Aggregates and fingerprints may still be private: retain reports
under the same access restrictions; do not automatically publish them.

| Exit code | Meaning |
| --- | --- |
| `0` | Diagnostics written; **not** a quality benchmark or statistical sufficiency claim. |
| `1` | Sanitized path, SQLite/schema/integrity, permission or I/O error. Do not consume a report from a failed run. An interrupted/write-failed report may be partial; choose a fresh path for retry. |
| `2` | Invalid arguments (including missing input/output/seed/protected root). |
| `3` | Diagnostic report written with `insufficient_sample`: no titles, profiles or recognized current ratings. Not a successful benchmark. |

Even a nonempty sample is **not** certified sufficient for evaluation. The `prepare`
command does not run temporal splits, ranking, model evaluation, historical
reconstruction or embedding coverage. No quality metrics or performance improvements
can be inferred from its report.

## Temporal building blocks (not a benchmark)

`TemporalInput.Read` consumes a query-only connection, normally obtained from
`Snapshot.OpenBenchmark`, and an explicit UTC snapshot observation time. It reads
`RatingEvents` and current `Ratings` in one deferred read transaction; SQLite
timestamps without an offset are interpreted as UTC. Invalid data and events after
the observation time are errors. These in-memory records contain private IDs and
ratings and must not be logged or serialized into public reports. The caller must
still apply the snapshot/path/hash protections above.

`TemporalHistory` implements `temporal-v1`: per profile, sort distinct event times;
fewer than three means no split. Otherwise allocate `clamp(floor(0.6*n),1,n-2)`
time groups to train, `clamp(floor(0.2*n),1,n-train-1)` to validation and the rest
to test. All events at a timestamp stay together. The seed is not used. Freeze both
cutoffs for comparisons; recalculating a split after changing future events may change
the cutoffs. Replay uses only events strictly before the chosen cutoff across **all**
profiles, including household features. A set updates the observed value, null deletes,
and conflicting values for one title/profile at the same time yield ambiguous state
until a later unambiguous event. Do not treat unknown or ambiguous state as a rating.

Current ratings are available only at their explicit observation time, never at
`RatedAt` or an earlier cutoff, and are not used to fill missing history. Event
retention, cascades and title merges can leave incomplete or remapped histories; even
matching current state does not establish completeness. Current favorites, watched
and library state do not reconstruct their past; catalog, settings, metadata and
embeddings also lack complete history.

`BenchmarkCatalogInput.Read` loads the current catalog on a query-only connection,
selecting one explicit embedding model ID and dimension. Availability membership
includes removed rows, matching `RecoMembership`. `TemporalRanking.Score` accepts
the catalog, events, profile, frozen train cutoff and scoring cutoff. It returns
full eligible-pool rankings for prior, sparse, embedding, combined, sparse-similarity
MMR and optional level 3, with unavailable reasons and embedding coverage. Rated
and ambiguous titles at the cutoff are excluded; deleted ratings are not. Missing
historical watched/library state is not applied. Ranking IDs and examples are private.

This is a **proxy**, not a faithful historical production ranking: current membership,
metadata and vectors are approximate; implicit signals, watched/library exclusions,
TMDB graph, production settings and exploration are absent. Defaults are used, and
the combined proxy imputes a neutral embedding score for missing vectors. Standalone
sparse or embedding rankings are unavailable when their required coverage or profile
is missing; model ID, dimension, finite values and unit norm are checked. The model
ID does not prove model artifact version or that stored vectors match current text.
The MMR variant uses sparse similarity, not the production similarity lookup.

For level 3, supply an `IRecoLevel3Model` explicitly. Its examples are built only
from the train prefix, with each example's profile constructed strictly before its
own event and its own title excluded. At a test cutoff, validation events can enter
the profile as past signals but never the training examples. The variant is not
selected or promoted by this API: any selection must use validation data, never test
labels. Insufficient training returns an unavailable reason.

## Temporal benchmark proxy

```text
dotnet run --project tools/Pickarr.RecoBench --no-build --no-restore -- benchmark <absolute-snapshot-db> <absolute-new-report-json> <integer-seed> <snapshot-observed-at-UTC-O> <absolute-repository-root> [other-protected-directories...]
```

The observation time is required explicitly, for example
`2025-01-11T00:00:00.0000000+00:00`; it must not precede any event or current
rating. It is the snapshot's actual observation time, not the benchmark execution
time. Use the same trusted standalone input, private output and protected-directory
rules as preparation. A read-only input handle and SHA-256 before and after the
reads protect repeatability under those filesystem assumptions. Invalid schema or
data fails rather than falling back.

The report contains only aggregates, code/build hash, input fingerprint, UTC
observation time, fixed default model ID and dimensions, seed, protocol and
environment; it never includes profiles, titles, settings, paths or individual
ratings. Aggregates and hashes are still private. The seed is reserved and unused.
The model ID identifies stored vectors, not their artifact hash or freshness.
Different cutoff instants for each profile come from `temporal-v1`; validation and
test both use the same frozen train cutoff, while validation events may become
past profile signals at the test cutoff. No variant is selected or promoted.
The CLI deliberately does not inject a level-3 trainer and reports that reason;
the separate sparse-similarity MMR rerank is an unselected proxy variant.

For each fold, the last event for an eligible title determines its label; deletes
and conflicting simultaneous values are counted but not scored. Titles rated or
ambiguous before the cutoff are ineligible; an absent rating is **not** a negative.
Recall@10 and Hit@10 rank the entire eligible pool, ordered by score descending,
then title ID ascending for ties (MMR uses its actual returned order). The denominator
for recall is the number of eligible Love/Like titles in that fold. Positive versus
Dislike and positive versus NotInterested pairwise AUC are separate; score ties
count one half, while MMR uses its actual rank for AUC. Metrics are macro averages
over profiles with the required labels and available ranking. Each metric includes
its contributing profile count or null and a reason; unavailable ranking and split
reasons have counts. Coverage includes per-fold sums of pool, pool embeddings,
eligible titles, labelled classes, deletions, ambiguity, ineligible changes and
events, plus train events and split/profile counts. Pool counts are summed across
profiles, not unique catalog counts.

`0` means the report meets a **diagnostic minimum**, not statistical sufficiency or
better quality. `3` writes `insufficient_sample` when the test fold has fewer than
30 eligible positive titles (the existing low-sample warning threshold), or no
combined positive fold. Available metrics remain descriptive and must not be used
to claim model quality. `1` means a sanitized failure; `2` means invalid arguments.
As with preparation, an interrupted output may be partial and must not be consumed.
The current catalog and embeddings and incomplete rating history make this an
approximation, not a faithful production ranking or perfect historical backtest.

## Local model adapter self-test

The benchmark tool can validate both local embedding adapters without opening a
snapshot or sending text over the network:

```text
dotnet run --project tools/Pickarr.RecoBench --no-build --no-restore -- model-self-test <absolute-current-model-directory> <absolute-multilingual-model-directory>
```

The current-model directory contains `model.onnx` and `vocab.txt`, matching the
application model artifacts. The multilingual directory contains
`onnx/model_quantized.onnx` and `tokenizer.json` from
`Xenova/paraphrase-multilingual-MiniLM-L12-v2` revision
`2c4055b12046f11709e9df2c122e59ffbdc2f900`. The tool rejects missing or
fingerprint-mismatched artifacts; it does not download either model. Restore the
normal project dependencies, then acquire the pinned public files separately if
the self-test is needed.

The model descriptor pins the current model revision and both artifact hashes;
the multilingual descriptor pins revision, ONNX and tokenizer SHA-256, 384
dimensions, 128 tokens, `<s>`/`</s>` IDs, and attention-masked mean pooling with
L2 normalization. The current model retains its existing 384-dimension,
256-token, WordPiece and mean-pooling contract. The multilingual adapter uses the
Hugging Face `tokenizer.json` implementation and preserves the end token when
truncating. Model-specific vectors are never combined or written to the catalog.
Six fixed multilingual tokenizer golden fingerprints are checked at runtime in
addition to empty-input special-token IDs.

Pinned SHA-256 values are:

| Model artifact | SHA-256 |
| --- | --- |
| Current `model.onnx` | `AFDB6F1A0E45B715D0BB9B11772F032C399BABD23BFC31FED1C170AFC848BDB1` |
| Current `vocab.txt` | `07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3` |
| Multilingual `onnx/model_quantized.onnx` | `66FC00F5F29AFCAFF34092E1BDD20008CA3918265A82FB9695A551E510CC4EBC` |
| Multilingual `tokenizer.json` | `B60B6B43406A48BF3638526314F3D232D97058BC93472FF2DE930D43686FA441` |

The command runs six fixed synthetic phrases in English, Italian, French,
Spanish, Japanese and Arabic through both models. It prints only model/artifact
metadata, language labels, dimensions and a repeatability check—not the text or
vectors. It does not evaluate recommendation quality or justify a production
model change. Tests and CI use synthetic inputs and do not download model files.

## Local model comparison (T08)

Run this only on an authorized, closed snapshot in a private local environment.
It never starts the application, jobs or migrations and does not connect to a
cluster or provider. Cluster measurements require separate operational approval
and an isolated environment.

```text
dotnet run --project tools/Pickarr.RecoBench --no-build --no-restore -- model-compare <absolute-snapshot-db> <absolute-new-private-report-json> <integer-seed> <snapshot-observed-at-UTC-O> <repetitions-at-least-2> <current-model-directory> <multilingual-model-directory> <absolute-repository-root> [other-protected-directories...]
```

Provide the pinned local model artifacts described above. The runner verifies
their SHA-256 values and reads catalog text in query-only mode. Both models
compute fresh embeddings for every title in the current catalog pool, using the
same text input, snapshot hash and `temporal-v1` evaluation protocol. This avoids
relying on stored vectors whose original ONNX revision is not recorded. Vectors
from the two models remain separate and are never written to the catalog.

The temporal evaluator requires complete eligible-pool embedding coverage for
each profile before reporting embedding metrics. If the capped subset does not
provide complete coverage, `qualityComparison.status` and the embedding
variant's `unavailableReasons` show that quality is unavailable; the combined
full-pool ranking then uses the declared missing-vector fallback and is not
evidence of a model-quality comparison.

For throughput and p50/p95 embedding latency, both models use the same
deterministic, evenly spaced sample of at most 512 titles from the pool, with
the requested warm-up and repetitions. Each model also makes one full-pool
embedding pass for quality evaluation. The full-pool pass can take substantial
time; it is local inference only and does not send snapshot text over the
network. The evaluator reports embedding quality only when all eligible titles
have vectors for every evaluated profile and fold. Text, title/profile IDs and
vectors stay in memory and are never written to the report. The output is
exclusively created outside the snapshot, repository, and model directories;
retain it under the same access restrictions as the snapshot because aggregate
counts and fingerprints remain private.

The runner warms up on the first 16 sampled texts (or the sample if smaller),
then performs the requested embedding passes over the at-most-512 measurement
sample and one full-pool quality pass per model. It also warms up ranking once
and repeats validation/test evaluation over the complete eligible pool the
requested number of times.
Report fields include per-profile current-rating and event counts by class,
split counts, stored matching-vector coverage, full-pool ranking metrics,
pinned model identities, sampled embedding throughput and p50/p95 latency,
full-pool quality-vector build time, ranking p50/p95 latency, model load and
end-to-end times, process CPU time, and sampled peak working set. Embedding is
one title per ONNX inference with one intra-op thread. The memory peak is
sampled every 50 ms; timings are local measurements, not confidence intervals.
Model order is fixed (current, then multilingual), and resource measurements
are not comparable across different hardware or between local and cluster runs.

`0` means both models have complete embedding metrics and meet the existing
diagnostic minimum, not statistical sufficiency or better recommendation
quality. `3` writes an `insufficient_sample` report when either condition
fails. Neither exit status selects or promotes a model. CI must remain
synthetic: it does not have
access to private snapshots and must not download model weights.

## Post-correction comparison (T11)

The T08 temporal report measures model quality on historical folds. It does **not**
execute a rebuild or a Similar request. Keep its Recall/Hit/AUC and timings separate
from the service-level comparison below. No new model is trained or promoted here.

`tools/Pickarr.PathBench` calls `RecoBuildService.BuildAsync(profile)` and
`RecoQueryService.QuerySimilarAsync` directly, including persistence and DTO
materialization. It does not start the app, scheduler, provider clients, embedding
inference or migrations. Run the **same harness source** in two separate Release
builds, serially, on the same idle host:

1. Use the preserved **pre-T09/T10 T08 product tree** (base commit
   `f9bbb2caa21c549ab4239f1810067b7ebcb27791` with the T02 product overlay).
   `RecoBuildService.cs` SHA-256 must be
   `558D5D44753F70343E4EDD4D23C352D4549002D45A16CCC2D91153892238E617`;
   `RecoQueryService.cs` must be
   `50282F82A5768458DDF2472B580DF61F78E1107474B3106416D0404DDBDFE6A8`.
   Bare HEAD is not T08. The first, failed T13 tree is an optional third
   diagnostic, not the baseline. Do not patch or check out product files in place.
2. Record the SDK, source manifests (including uncommitted corrected sources),
   exact harness source hashes, build configuration and host privately. Use
   separate build directories and the same package versions. The reports also
   identify the Core and Infrastructure binaries. The harness pins the clock at
   the T04 observation and uses product-default settings without reading secrets,
   L3 off, the same cached MiniLM model ID and 384 dimensions. Missing vectors
   stay missing. Synthetic normalized vectors are fixtures, not model output.
3. First exercise both versions with the isolated in-memory fixture:

```powershell
# $repo = corrected source root; $t08 = separate prepared baseline root.
# Copy only harness sources, never bin/obj or any database.
$dst = New-Item -ItemType Directory -Force "$t08\tools\Pickarr.PathBench"
Copy-Item "$repo\tools\Pickarr.PathBench\*.cs","$repo\tools\Pickarr.PathBench\*.csproj" $dst
$dst = New-Item -ItemType Directory -Force "$t08\tools\Pickarr.RecoBench"
Copy-Item "$repo\tools\Pickarr.RecoBench\Snapshot.cs" $dst
$dll = 'tools\Pickarr.PathBench\bin\Release\net10.0\Pickarr.PathBench.dll'
foreach ($root in @($t08,$repo)) {
    dotnet build "$root\tools\Pickarr.PathBench\Pickarr.PathBench.csproj" -c Release -m:1
    if ($LASTEXITCODE) { throw 'Build failed' }
}
dotnet "$t08\$dll" synthetic
dotnet "$repo\$dll" synthetic
```

T09 holds the candidate pool and priors constant. It measures the published rank
of a known winner and effective mass for no vote, an outside-pool vote with no
shared terms, and one with a shared genre. T10 uses 132 candidates, profile and
library exclusions, graph edges, mixed vector coverage, a missing query vector
and winners past position 120. An independent exhaustive reference uses
whole-pool TF-IDF, normalized vectors, identity exclusions and graph scores
before sorting by score then ID. It does not call any product ranking helpers.
Recall is the number of distinct hits divided by the number of oracle slots;
`exactOrdered` additionally tests order, duplicates and extra results.

**Authorized snapshot run only.** Use a local-only trusted host, no concurrent
writer, and an existing private output directory (operator/SYSTEM-only ACL on
Windows) outside the source, all repository/model/data roots and the input
directory. Never use public CI or a shared temp folder. Check free disk for
a full database copy plus journal, and available RAM for the pool and oracle.
The input must have the read-only attribute, no sidecars, and the independently
recorded T04 digest. The runner reads the input only as a read-locked byte
stream, verifies SHA-256 before/after, and exclusively creates a disposable
working copy beside the private report. Only this copy is opened by SQLite;
it is checked for integrity/FKs before use and removed after disposal. The copy
is **as sensitive as the entire snapshot**. On interruption inspect only that
run's private working directory and remove its owned scratch file/sidecars.
Do not pass the output directory itself as a protected root.

Both runs use the same SQLite collection translation (one JSON parameter per
ID collection). The unadjusted T08 provider default fails with `too many SQL
variables` on the full pool; its timings below this limit cannot be extrapolated.
The paired run compares the service algorithms, not legacy SQL translation.

```powershell
# $inputFile, $expected (trusted T04 digest), $private, $repo, $t08, $dataRoot
# and any other protected directories must be set by the authorized operator.
$before = Join-Path $private 't08-service.json'
$after = Join-Path $private 'corrected-service.json'
dotnet "$t08\$dll" snapshot $inputFile $expected $before $repo $t08 $dataRoot
if ($LASTEXITCODE) { throw 'Baseline failed' }
dotnet "$repo\$dll" snapshot $inputFile $expected $after $repo $t08 $dataRoot
if ($LASTEXITCODE) { throw 'Corrected run failed' }
& "$repo\tools\Pickarr.PathBench\Compare-PathBench.ps1" $before $after | Format-List
```

* **T09 snapshot:** each profile is actually rebuilt. The published IDs must
  equal the enabled pool exactly. For profiles with outside-pool explicit
  votes, a second rebuild omits just those votes; the report measures change
  in effective mass and top-10 overlap against that control. Original votes
  are restored on the disposable copy before moving to another profile.
  This diagnoses sensitivity to explicit votes, not implicit-only signals,
  held-out preference quality or the temporal T08 split.
* **T10 snapshot:** for the admin profile, query up to eight evenly spaced IDs
  in each of two sorted pool strata (cached/missing query vector), k=10.
  The oracle is independent of observed results. Report affected queries,
  missing-vector oracle slots, recall by stratum, and whether an oracle winner
  lies beyond the first 120 **eligible IDs in ID order** (a diagnostic, not a
  reconstruction of the old genre heuristic). `NeedsFallback` means a cached
  query has a missing-vector title in its oracle top-k.
* **Timing:** one untimed warm-up per request, then five full service calls.
  Nearest-rank p50/p95 are per stratum and exclude oracle construction,
  warm-up, validation and report I/O. Rebuild wall times and whole-process
  peak working set (including the oracle) are separate. These are warm local
  diagnostics, not representative traffic, confidence intervals or cluster
  measurements. Floating-point near-ties at the top-k boundary may warrant
  investigation rather than attribution to a product defect.

A zero-sized affected subset is **not evidence of equivalence**. Compare only
matching protocol, input digest, model, observation, runtime/host, pool,
oracle denominators and sampling; report regressions and missing coverage.
No report contains title/profile IDs, names, vector contents or ranked lists.
Keep even the aggregates, comparison output and fingerprints private.

### Synthetic fixture and tests

`tools/Pickarr.RecoBench/fixtures/preparation.sql` creates only the real schema subset
required by diagnostics (`Titles.Id`, `Profiles.Id`, and `Ratings.ProfileId/TitleId/
Value/RatedAt`, with composite key and foreign keys). It contains eight synthetic
titles, two synthetic profiles and five ratings; it is not a complete application DB.
Do not initialize it with the application or migrations.

```text
dotnet test tests/Pickarr.Infrastructure.Tests/Pickarr.Infrastructure.Tests.csproj --no-restore --filter "FullyQualifiedName~RecoSnapshotTests|FullyQualifiedName~RecoPreparationTests"
```

Runner tests materialize this SQL locally, invoke the actual CLI, compare report
bytes and input hashes, test malformed/empty input and unsafe/existing outputs, and
remove only their own synthetic workspace. Their unique workspaces are siblings of
the repository under `Pickarr.RecoBench.Tests`, so the real repository can be passed
as a protected root. No HTTP, model download, private snapshot, job or migration is
used. The parent test-workspace directory may remain empty.

## Runner integration

`Snapshot.OpenBenchmark(snapshot, output, protectedDirectories)` validates the
standalone input and opens a SQLite connection with `Mode=ReadOnly` and `query_only`.
It rejects snapshots with sidecars, input in protected directories, and non-new or
protected output paths. The caller must pass the repository and every protected data
directory, keep the input unchanged for the connection lifetime, and dispose the
connection. This method checks the output path but does **not** create or reserve it.
`Snapshot.CreateBenchmarkOutput` repeats path/standalone checks immediately before
exclusive output creation; the preparation runner calls it only after successful
diagnostics and the final input-hash check. Keep directories trusted and
non-concurrent: this is not an atomic filesystem security boundary.
