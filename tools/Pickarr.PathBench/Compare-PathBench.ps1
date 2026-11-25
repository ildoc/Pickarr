param(
    [Parameter(Mandatory)][string]$Baseline,
    [Parameter(Mandatory)][string]$Corrected
)
$ErrorActionPreference = 'Stop'
$a = Get-Content -Raw -LiteralPath $Baseline | ConvertFrom-Json
$b = Get-Content -Raw -LiteralPath $Corrected | ConvertFrom-Json
if ($a.protocol -ne 'pathbench-v1') { throw 'Unexpected protocol.' }
foreach ($key in 'protocol','synthetic','snapshotSha256','observedAt','model','dimensions','level3','runtime','os','arch','cpuCount') {
    if ($a.$key -ne $b.$key) { throw "Non-comparable inputs: $key." }
}
if ($a.infrastructure -eq $b.infrastructure) { throw 'Identical Infrastructure assemblies.' }
foreach ($key in 'pool','cachedPool','queryCases','missingVectorOracleSlots','fallbackOracleQueries','beyond120OracleQueries') {
    if ($a.measurements.$key -ne $b.measurements.$key) { throw "Non-comparable samples: $key." }
}
if ($a.measurements.similar.Count -ne $b.measurements.similar.Count -or
    $a.measurements.rebuilds.Count -ne $b.measurements.rebuilds.Count) { throw 'Stratum count mismatch.' }

for ($i = 0; $i -lt $a.measurements.similar.Count; $i++) {
    $x = $a.measurements.similar[$i]; $y = $b.measurements.similar[$i]
    foreach ($key in 'Subset','K','NeedsFallback','Beyond120','cases','slots','calls') {
        if ($x.$key -ne $y.$key) { throw "Similar stratum mismatch: $key." }
    }
}
for ($i = 0; $i -lt $a.measurements.rebuilds.Count; $i++) {
    $x = $a.measurements.rebuilds[$i]; $y = $b.measurements.rebuilds[$i]
    if ($x.subset -ne $y.subset -or $x.externalRatings -ne $y.externalRatings) { throw 'Rebuild stratum mismatch.' }
}
# The caller's terminal/output must also be private for snapshot comparisons.
for ($i = 0; $i -lt $a.measurements.similar.Count; $i++) {
    $x = $a.measurements.similar[$i]; $y = $b.measurements.similar[$i]
    [pscustomobject]@{ path='T10'; subset=$x.Subset; k=$x.K; needsFallback=$x.NeedsFallback; beyond120=$x.Beyond120;
        cases=$x.cases; oracleSlots=$x.slots; baselineRecall=$x.recall; correctedRecall=$y.recall;
        baselineP50Ms=$x.p50Ms; correctedP50Ms=$y.p50Ms; baselineP95Ms=$x.p95Ms; correctedP95Ms=$y.p95Ms }
}
for ($i = 0; $i -lt $a.measurements.rebuilds.Count; $i++) {
    $x = $a.measurements.rebuilds[$i]; $y = $b.measurements.rebuilds[$i]
    [pscustomobject]@{ path='T09'; subset=$x.subset; externalRatings=$x.externalRatings; baselineN=$x.EffectiveN;
        correctedN=$y.EffectiveN; baselineWinnerRank=$x.expectedWinnerRank; correctedWinnerRank=$y.expectedWinnerRank;
        baselineTop10Overlap=$x.top10Overlap; correctedTop10Overlap=$y.top10Overlap; baselineMs=$x.Ms; correctedMs=$y.Ms }
}
